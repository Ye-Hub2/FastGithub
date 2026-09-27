using FastGithub.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// IP服务
    /// 域名IP关系缓存10分钟
    /// IPEndPoint时延缓存5分钟
    /// IPEndPoint连接超时5秒
    /// </summary>
    sealed class IPAddressService
    {
        private record DomainAddress(string Domain, IPAddress Address);
        private readonly TimeSpan domainAddressExpiration = TimeSpan.FromMinutes(10d);
        private readonly IMemoryCache domainAddressCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        private record AddressElapsed(IPEndPoint EndPoint, TimeSpan Elapsed);

        /// <summary>
        /// 下载优先模式下的参考体积
        /// </summary>
        private const int REFERENCE_BYTES = 512 * 1024;

        /// <summary>
        /// 没有速率样本时的假定速率(字节/秒)
        /// </summary>
        private const double ASSUMED_BYTES_PER_SECOND = 1024d * 1024d;
        private readonly TimeSpan problemElapsedExpiration = TimeSpan.FromMinutes(1d);
        private readonly TimeSpan normalElapsedExpiration = TimeSpan.FromMinutes(5d);
        private readonly TimeSpan connectTimeout = TimeSpan.FromSeconds(5d);
        private readonly IMemoryCache addressElapsedCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        private readonly DnsClient dnsClient;
        private readonly IThroughputSampler throughputSampler;
        private readonly SpeedModeService speedModeService;

        /// <summary>
        /// IP服务
        /// </summary>
        /// <param name="dnsClient"></param>
        /// <param name="throughputSampler"></param>
        /// <param name="speedModeService"></param>
        public IPAddressService(
            DnsClient dnsClient,
            IThroughputSampler throughputSampler,
            SpeedModeService speedModeService)
        {
            this.dnsClient = dnsClient;
            this.throughputSampler = throughputSampler;
            this.speedModeService = speedModeService;
        }

        /// <summary>
        /// 并行获取可连接的IP
        /// </summary>
        /// <param name="dnsEndPoint"></param>
        /// <param name="oldAddresses"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IPAddress[]> GetAddressesAsync(DnsEndPoint dnsEndPoint, IEnumerable<IPAddress> oldAddresses, CancellationToken cancellationToken)
        {
            var ipEndPoints = new HashSet<IPEndPoint>();

            // 历史未过期的IP节点
            foreach (var address in oldAddresses)
            {
                var domainAddress = new DomainAddress(dnsEndPoint.Host, address);
                if (this.domainAddressCache.TryGetValue(domainAddress, out _))
                {
                    ipEndPoints.Add(new IPEndPoint(address, dnsEndPoint.Port));
                }
            }

            // 新解析出的IP节点
            await foreach (var address in this.dnsClient.ResolveAsync(dnsEndPoint, fastSort: false, cancellationToken))
            {
                ipEndPoints.Add(new IPEndPoint(address, dnsEndPoint.Port));
                var domainAddress = new DomainAddress(dnsEndPoint.Host, address);
                this.domainAddressCache.Set(domainAddress, default(object), this.domainAddressExpiration);
            }

            if (ipEndPoints.Count == 0)
            {
                return Array.Empty<IPAddress>();
            }

            var addressElapsedTasks = ipEndPoints.Select(item => this.GetAddressElapsedAsync(item, cancellationToken));
            var addressElapseds = await Task.WhenAll(addressElapsedTasks);

            return addressElapseds
                .Where(item => item.Elapsed < TimeSpan.MaxValue)
                .OrderBy(item => this.GetScore(item.EndPoint, item.Elapsed))
                .Select(item => item.EndPoint.Address)
                .ToArray();
        }


        /// <summary>
        /// 获取节点的下载速率(字节/秒)
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public double? GetBytesPerSecond(IPAddress address, int port)
        {
            return this.throughputSampler.GetBytesPerSecond(new IPEndPoint(address, port));
        }

        /// <summary>
        /// 计算排序评分
        /// 延迟优先为握手耗时；下载优先为预计下载耗时(握手耗时+参考体积/速率)
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="elapsed"></param>
        /// <returns></returns>
        private double GetScore(IPEndPoint endPoint, TimeSpan elapsed)
        {
            if (this.speedModeService.Mode == SpeedMode.Latency)
            {
                return elapsed.TotalMilliseconds;
            }

            var bytesPerSecond = this.throughputSampler.GetBytesPerSecond(endPoint) ?? ASSUMED_BYTES_PER_SECOND;
            if (bytesPerSecond <= 0d)
            {
                bytesPerSecond = ASSUMED_BYTES_PER_SECOND;
            }
            return elapsed.TotalMilliseconds + REFERENCE_BYTES / bytesPerSecond * 1000d;
        }

        /// <summary>
        /// 获取IP节点的时延
        /// </summary> 
        /// <param name="endPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<AddressElapsed> GetAddressElapsedAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            if (this.addressElapsedCache.TryGetValue<AddressElapsed>(endPoint, out var addressElapsed))
            {
                return addressElapsed;
            }

            var stopWatch = Stopwatch.StartNew();
            try
            {
                using var timeoutTokenSource = new CancellationTokenSource(this.connectTimeout);
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
                using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endPoint, linkedTokenSource.Token);

                addressElapsed = new AddressElapsed(endPoint, stopWatch.Elapsed);
                return this.addressElapsedCache.Set(endPoint, addressElapsed, this.normalElapsedExpiration);
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                addressElapsed = new AddressElapsed(endPoint, TimeSpan.MaxValue);
                var expiration = IsLocalNetworkProblem(ex) ? this.problemElapsedExpiration : this.normalElapsedExpiration;
                return this.addressElapsedCache.Set(endPoint, addressElapsed, expiration);
            }
            finally
            {
                stopWatch.Stop();
            }
        }

        /// <summary>
        /// 是否为本机网络问题
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private static bool IsLocalNetworkProblem(Exception ex)
        {
            if (ex is not SocketException socketException)
            {
                return false;
            }

            var code = socketException.SocketErrorCode;
            return code == SocketError.NetworkDown || code == SocketError.NetworkUnreachable;
        }
    }
}
