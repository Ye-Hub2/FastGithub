using FastGithub.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 下载速率主动探测器
    /// 仅对配置了SpeedTestUri的域名生效，且同一个节点存在探测间隔限制
    /// </summary>
    sealed class ThroughputProbe
    {
        private const int PROBE_TIMEOUT_MILLISECONDS = 4000;
        private const int MAX_PROBE_COUNT = 3;
        private const int READ_BUFFER_SIZE = 64 * 1024;

        private readonly FastGithubConfig fastGithubConfig;
        private readonly IThroughputSampler throughputSampler;
        private readonly ILogger<ThroughputProbe> logger;
        private readonly ConcurrentDictionary<IPEndPoint, long> lastProbeTicks = new();

        /// <summary>
        /// 下载速率主动探测器
        /// </summary>
        /// <param name="fastGithubConfig"></param>
        /// <param name="throughputSampler"></param>
        /// <param name="logger"></param>
        public ThroughputProbe(
            FastGithubConfig fastGithubConfig,
            IThroughputSampler throughputSampler,
            ILogger<ThroughputProbe> logger)
        {
            this.fastGithubConfig = fastGithubConfig;
            this.throughputSampler = throughputSampler;
            this.logger = logger;
        }

        /// <summary>
        /// 为没有速率样本的节点发起后台探测
        /// 本方法不阻塞也不抛出异常
        /// </summary>
        /// <param name="target">目标域名节点</param>
        /// <param name="candidates">候选节点(按握手耗时升序)</param>
        public void TryProbe(DnsEndPoint target, IEnumerable<IPEndPoint> candidates)
        {
            if (this.fastGithubConfig.ThroughputProbeEnabled == false)
            {
                return;
            }

            if (this.fastGithubConfig.TryGetDomainConfig(target.Host, out var domainConfig) == false)
            {
                return;
            }

            var speedTestUri = domainConfig.SpeedTestUri;
            if (speedTestUri == null)
            {
                return;
            }

            var count = 0;
            foreach (var candidate in candidates)
            {
                if (count >= MAX_PROBE_COUNT)
                {
                    break;
                }
                if (candidate.Port != 443)
                {
                    continue;
                }
                if (this.throughputSampler.GetBytesPerSecond(candidate) != null)
                {
                    continue;
                }
                if (this.CanProbe(candidate) == false)
                {
                    continue;
                }

                count++;
                _ = this.ProbeAsync(target.Host, speedTestUri, candidate);
            }
        }

        /// <summary>
        /// 是否到达允许探测的时间
        /// </summary>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        private bool CanProbe(IPEndPoint endPoint)
        {
            var now = Environment.TickCount64;
            if (this.lastProbeTicks.TryGetValue(endPoint, out var tick) &&
                now - tick < this.fastGithubConfig.ThroughputProbeIntervalMilliseconds)
            {
                return false;
            }

            this.lastProbeTicks[endPoint] = now;
            return true;
        }

        /// <summary>
        /// 探测指定节点的下载速率
        /// </summary>
        private async Task ProbeAsync(string host, Uri speedTestUri, IPEndPoint endPoint)
        {
            try
            {
                var maxBytes = this.fastGithubConfig.ThroughputProbeMaxBytes;
                using var timeoutTokenSource = new CancellationTokenSource(PROBE_TIMEOUT_MILLISECONDS);
                var cancellationToken = timeoutTokenSource.Token;

                using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endPoint, cancellationToken);

                var stream = (Stream)new NetworkStream(socket, ownsSocket: false);
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host
                }, cancellationToken);
                stream = sslStream;

                using (stream)
                {
                    var request = $"GET {speedTestUri.PathAndQuery} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: FastGithub\r\nAccept: */*\r\nRange: bytes=0-{maxBytes - 1}\r\nConnection: close\r\n\r\n";
                    var requestBytes = Encoding.ASCII.GetBytes(request);
                    await stream.WriteAsync(requestBytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);

                    var stopwatch = Stopwatch.StartNew();
                    var buffer = new byte[READ_BUFFER_SIZE];
                    var total = 0L;
                    while (stopwatch.ElapsedMilliseconds < PROBE_TIMEOUT_MILLISECONDS && total < maxBytes)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read <= 0)
                        {
                            break;
                        }
                        total += read;
                    }
                    stopwatch.Stop();

                    if (total > 0)
                    {
                        this.throughputSampler.Record(endPoint, total, stopwatch.Elapsed);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug($"探测{endPoint}下载速率失败：{ex.Message}");
            }
        }
    }
}
