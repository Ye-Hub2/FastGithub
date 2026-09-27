using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;

namespace FastGithub.Configuration
{
    /// <summary>
    /// FastGithub配置
    /// </summary>
    public class FastGithubConfig
    {
        private SortedDictionary<DomainPattern, DomainConfig> domainConfigs;
        private ConcurrentDictionary<string, DomainConfig?> domainConfigCache;

        /// <summary>
        /// http代理端口
        /// </summary>
        public int HttpProxyPort { get; set; }

        /// <summary>
        /// 回退的dns
        /// </summary>
        public IPEndPoint[] FallbackDns { get; set; }

        /// <summary>
        /// dns解析失败后的否定缓存时长
        /// </summary>
        public TimeSpan DnsNegativeCacheTimeout { get; set; }

        /// <summary>
        /// 域名测速的并行数
        /// </summary>
        public int TestSpeedParallelCount { get; set; }

        /// <summary>
        /// tcp连接的超时预算
        /// </summary>
        public ConnectTimeoutConfig ConnectTimeout { get; set; }

        /// <summary>
        /// 下载优先模式下是否主动探测各节点的下载速率
        /// </summary>
        public bool ThroughputProbeEnabled { get; set; }

        /// <summary>
        /// 主动探测时每个节点最多读取的字节数
        /// </summary>
        public int ThroughputProbeMaxBytes { get; set; }

        /// <summary>
        /// 同一个节点两次主动探测的最小间隔毫秒数
        /// </summary>
        public long ThroughputProbeIntervalMilliseconds { get; set; }

        /// <summary>
        /// FastGithub配置
        /// </summary>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public FastGithubConfig(IOptionsMonitor<FastGithubOptions> options)
        {
            var opt = options.CurrentValue;

            this.HttpProxyPort = opt.HttpProxyPort;
            this.FallbackDns = opt.FallbackDns;
            this.DnsNegativeCacheTimeout = opt.GetDnsNegativeCacheTimeout();
            this.TestSpeedParallelCount = opt.GetTestSpeedParallelCount();
            this.ConnectTimeout = opt.GetConnectTimeout();
            this.ThroughputProbeEnabled = opt.ThroughputProbeEnabled;
            this.ThroughputProbeMaxBytes = opt.GetThroughputProbeMaxBytes();
            this.ThroughputProbeIntervalMilliseconds = opt.GetThroughputProbeIntervalSeconds() * 1000L;
            this.domainConfigs = ConvertDomainConfigs(opt.DomainConfigs);
            this.domainConfigCache = new ConcurrentDictionary<string, DomainConfig?>();

            options.OnChange(opt => this.Update(opt));
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        /// <param name="options"></param>
        private void Update(FastGithubOptions options)
        {
            this.HttpProxyPort = options.HttpProxyPort;
            this.FallbackDns = options.FallbackDns;
            this.DnsNegativeCacheTimeout = options.GetDnsNegativeCacheTimeout();
            this.TestSpeedParallelCount = options.GetTestSpeedParallelCount();
            this.ConnectTimeout = options.GetConnectTimeout();
            this.ThroughputProbeEnabled = options.ThroughputProbeEnabled;
            this.ThroughputProbeMaxBytes = options.GetThroughputProbeMaxBytes();
            this.ThroughputProbeIntervalMilliseconds = options.GetThroughputProbeIntervalSeconds() * 1000L;
            this.domainConfigs = ConvertDomainConfigs(options.DomainConfigs);
            this.domainConfigCache = new ConcurrentDictionary<string, DomainConfig?>();
        }

        /// <summary>
        /// 配置转换
        /// </summary>
        /// <param name="domainConfigs"></param>
        /// <returns></returns>
        private static SortedDictionary<DomainPattern, DomainConfig> ConvertDomainConfigs(Dictionary<string, DomainConfig> domainConfigs)
        {
            var result = new SortedDictionary<DomainPattern, DomainConfig>();
            foreach (var kv in domainConfigs)
            {
                result.Add(new DomainPattern(kv.Key), kv.Value);
            }
            return result;
        }

        /// <summary>
        /// 是否匹配指定的域名
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        public bool IsMatch(string domain)
        {
            return this.TryGetDomainConfig(domain, out _);
        }

        /// <summary>
        /// 尝试获取域名配置
        /// </summary>
        /// <param name="domain"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetDomainConfig(string domain, [MaybeNullWhen(false)] out DomainConfig value)
        {
            value = this.domainConfigCache.GetOrAdd(domain, GetDomainConfig);
            return value != null;

            DomainConfig? GetDomainConfig(string domain)
            {
                var key = this.domainConfigs.Keys.FirstOrDefault(item => item.IsMatch(domain));
                return key == null ? null : this.domainConfigs[key];
            }
        }

        /// <summary>
        /// 获取所有域名表达式
        /// </summary>
        /// <returns></returns>
        public DomainPattern[] GetDomainPatterns()
        {
            return this.domainConfigs.Keys.ToArray();
        }
    }
}
