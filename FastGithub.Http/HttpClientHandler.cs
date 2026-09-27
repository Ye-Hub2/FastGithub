using FastGithub.Configuration;
using FastGithub.DomainResolve;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.Http
{
    /// <summary>
    /// HttpClientHandler
    /// </summary> 
    class HttpClientHandler : DelegatingHandler
    {
        private readonly DomainConfig domainConfig;
        private readonly IDomainResolver domainResolver;

        /// <summary>
        /// tcp连接的超时预算
        /// </summary>
        private readonly ConnectTimeoutConfig connectTimeout;

        /// <summary>
        /// HttpClientHandler
        /// </summary>
        /// <param name="domainConfig"></param>
        /// <param name="domainResolver"></param> 
        public HttpClientHandler(DomainConfig domainConfig, IDomainResolver domainResolver, ConnectTimeoutConfig connectTimeout)
        {
            this.domainConfig = domainConfig;
            this.domainResolver = domainResolver;
            this.connectTimeout = connectTimeout;
            this.InnerHandler = this.CreateSocketsHttpHandler();
        }

        /// <summary>
        /// 发送请求
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri == null)
            {
                throw new FastGithubException("必须指定请求的URI");
            }

            // 请求上下文信息
            var isHttps = uri.Scheme == Uri.UriSchemeHttps;
            var tlsSniValue = this.domainConfig.GetTlsSniPattern().WithDomain(uri.Host).WithRandom();
            request.SetRequestContext(new RequestContext(isHttps, tlsSniValue));

            // 设置请求头host，修改协议为http
            request.Headers.Host = uri.Host;
            request.RequestUri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp }.Uri;

            if (this.domainConfig.Timeout != null)
            {
                using var timeoutTokenSource = new CancellationTokenSource(this.domainConfig.Timeout.Value);
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
                return await base.SendAsync(request, linkedTokenSource.Token);
            }
            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// 创建转发代理的httpHandler
        /// </summary>
        /// <returns></returns>
        private SocketsHttpHandler CreateSocketsHttpHandler()
        {
            return new SocketsHttpHandler
            {
                Proxy = null,
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectCallback = this.ConnectCallback
            };
        }

        /// <summary>
        /// 连接回调
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var innerExceptions = new List<Exception>();
            var ipEndPoints = this.GetIPEndPointsAsync(context.DnsEndPoint, cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            var attemptIndex = 0;

            await foreach (var ipEndPoint in ipEndPoints)
            {
                var remaining = this.connectTimeout.Total - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var isFirstAttempt = attemptIndex == 0;
                attemptIndex++;

                var attemptTimeout = isFirstAttempt ? this.connectTimeout.FirstIp : this.connectTimeout.FailoverIp;
                if (attemptTimeout > remaining)
                {
                    attemptTimeout = remaining;
                }

                try
                {
                    using var timeoutTokenSource = new CancellationTokenSource(attemptTimeout);
                    using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);
                    return await this.ConnectAsync(context, ipEndPoint, linkedTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    innerExceptions.Add(new HttpConnectTimeoutException(ipEndPoint.Address));
                }
                catch (Exception ex)
                {
                    innerExceptions.Add(ex);
                }
            }

            throw new AggregateException("找不到任何可成功连接的IP", innerExceptions);
        }

        /// <summary>
        /// 建立连接
        /// </summary>
        /// <param name="context"></param>
        /// <param name="ipEndPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, IPEndPoint ipEndPoint, CancellationToken cancellationToken)
        {
            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(ipEndPoint, cancellationToken);
            var stream = new NetworkStream(socket, ownsSocket: true);

            var requestContext = context.InitialRequestMessage.GetRequestContext();
            if (requestContext.IsHttps == false)
            {
                return stream;
            }

            var tlsSniValue = requestContext.TlsSniValue.WithIPAddress(ipEndPoint.Address);
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = tlsSniValue.Value,
                RemoteCertificateValidationCallback = ValidateServerCertificate
            }, cancellationToken);

            return sslStream;

            // 验证证书有效性
            bool ValidateServerCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
            {
                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                // 证书链不可信（自签名/过期/未知CA）时直接拒绝，不允许被名字不匹配的分支绕过
                if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors) && this.domainConfig.TlsAllowUntrustedCert == false)
                {
                    return false;
                }

                if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
                    if (this.domainConfig.TlsIgnoreNameMismatch == true)
                    {
                        return true;
                    }

                    var domain = context.DnsEndPoint.Host;
                    var dnsNames = ReadDnsNames(cert);
                    return dnsNames.Any(dns => IsMatch(dns, domain));
                }

                return false;
            }
        }

        /// <summary>
        /// 解析为IPEndPoint
        /// </summary>
        /// <param name="dnsEndPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async IAsyncEnumerable<IPEndPoint> GetIPEndPointsAsync(DnsEndPoint dnsEndPoint, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(dnsEndPoint.Host, out var address))
            {
                yield return new IPEndPoint(address, dnsEndPoint.Port);
            }
            else
            {
                if (this.domainConfig.IPAddress != null)
                {
                    yield return new IPEndPoint(this.domainConfig.IPAddress, dnsEndPoint.Port);
                }

                await foreach (var item in this.domainResolver.ResolveAsync(dnsEndPoint, cancellationToken))
                {
                    yield return new IPEndPoint(item, dnsEndPoint.Port);
                }
            }
        }

        /// <summary>
        /// 读取使用的DNS名称
        /// </summary>
        /// <param name="cert"></param>
        /// <returns></returns>
        private static IEnumerable<string> ReadDnsNames(X509Certificate? cert)
        {
            if (cert is X509Certificate2 x509)
            {
                var extension = x509.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
                if (extension != null)
                {
                    return extension.EnumerateDnsNames();
                }
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// 比较域名
        /// </summary>
        /// <param name="dnsName"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        private static bool IsMatch(string dnsName, string? domain)
        {
            if (domain == null)
            {
                return false;
            }
            if (dnsName == domain)
            {
                return true;
            }
            if (dnsName[0] == '*')
            {
                return domain.EndsWith(dnsName[1..]);
            }
            return false;
        }
    }
}
