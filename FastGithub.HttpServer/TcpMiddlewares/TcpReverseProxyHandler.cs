using FastGithub.Configuration;
using FastGithub.DomainResolve;
using Microsoft.AspNetCore.Connections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.HttpServer.TcpMiddlewares
{
    /// <summary>
    /// tcp协议代理处理者
    /// </summary>
    abstract class TcpReverseProxyHandler : ConnectionHandler
    {
        private readonly IDomainResolver domainResolver;
        private readonly FastGithubConfig fastGithubConfig;
        private readonly DnsEndPoint endPoint;

        /// <summary>
        /// tcp协议代理处理者
        /// </summary>
        /// <param name="domainResolver"></param>
        /// <param name="endPoint"></param>
        /// <param name="fastGithubConfig"></param>
        public TcpReverseProxyHandler(IDomainResolver domainResolver, DnsEndPoint endPoint, FastGithubConfig fastGithubConfig)
        {
            this.domainResolver = domainResolver;
            this.endPoint = endPoint;
            this.fastGithubConfig = fastGithubConfig;
        }

        /// <summary>
        /// tcp连接后
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task OnConnectedAsync(ConnectionContext context)
        {
            var cancellationToken = context.ConnectionClosed;
            using var connection = await CreateConnectionAsync(cancellationToken);
            var task1 = connection.CopyToAsync(context.Transport.Output, cancellationToken);
            var task2 = context.Transport.Input.CopyToAsync(connection, cancellationToken);
            await Task.WhenAny(task1, task2);
        }

        /// <summary>
        /// 创建连接
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="AggregateException"></exception>
        private async Task<Stream> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            var innerExceptions = new List<Exception>();
            var connectTimeout = this.fastGithubConfig.ConnectTimeout;
            var stopwatch = Stopwatch.StartNew();
            var attemptIndex = 0;

            await foreach (var address in domainResolver.ResolveAsync(endPoint, cancellationToken))
            {
                var remaining = connectTimeout.Total - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var isFirstAttempt = attemptIndex == 0;
                attemptIndex++;

                var attemptTimeout = isFirstAttempt ? connectTimeout.FirstIp : connectTimeout.FailoverIp;
                if (attemptTimeout > remaining)
                {
                    attemptTimeout = remaining;
                }

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    using var timeoutTokenSource = new CancellationTokenSource(attemptTimeout);
                    using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
                    await socket.ConnectAsync(address, endPoint.Port, linkedTokenSource.Token);

                    // ownsSocket必须为true，否则连接结束后socket不会被释放
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    innerExceptions.Add(ex);
                }
            }
            throw new AggregateException($"无法连接到{endPoint.Host}:{endPoint.Port}", innerExceptions);
        }
    }
}
