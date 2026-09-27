using System;
using System.Collections.Generic;
using System.Net;

namespace FastGithub.Configuration
{
    /// <summary>
    /// FastGithub的配置
    /// </summary>
    public class FastGithubOptions
    {
        /// <summary>
        /// dns否定缓存时长的默认值
        /// </summary>
        private static readonly TimeSpan defaultDnsNegativeCacheTimeout = TimeSpan.FromSeconds(15d);

        /// <summary>
        /// 测速并行数的上限
        /// </summary>
        private const int MAX_TEST_SPEED_PARALLEL_COUNT = 32;

        /// <summary>
        /// http代理端口
        /// </summary>
        public int HttpProxyPort { get; set; } = 38457;

        /// <summary>
        /// 回退的dns
        /// </summary>
        public IPEndPoint[] FallbackDns { get; set; } = Array.Empty<IPEndPoint>();

        /// <summary>
        /// 是否在安装CA证书后关闭git的ssl校验
        /// 默认false，即不修改git的任何配置
        /// </summary>
        public bool DisableGitSslverify { get; set; }

        /// <summary>
        /// dns解析失败后的否定缓存时长
        /// 太短会放大dns查询量，太长会让临时故障的恢复变慢
        /// </summary>
        public TimeSpan DnsNegativeCacheTimeout { get; set; } = defaultDnsNegativeCacheTimeout;

        /// <summary>
        /// 域名测速的并行数
        /// </summary>
        public int TestSpeedParallelCount { get; set; } = 4;

        /// <summary>
        /// tcp连接的超时预算
        /// </summary>
        public ConnectTimeoutConfig? ConnectTimeout { get; set; } = new();

        /// <summary>
        /// 代理的域名配置
        /// </summary>
        public Dictionary<string, DomainConfig> DomainConfigs { get; set; } = new();

        /// <summary>
        /// 获取经过校验的dns否定缓存时长
        /// </summary>
        /// <returns></returns>
        public TimeSpan GetDnsNegativeCacheTimeout()
        {
            return this.DnsNegativeCacheTimeout > TimeSpan.Zero ? this.DnsNegativeCacheTimeout : defaultDnsNegativeCacheTimeout;
        }

        /// <summary>
        /// 获取经过校验的测速并行数
        /// </summary>
        /// <returns></returns>
        public int GetTestSpeedParallelCount()
        {
            return Math.Clamp(this.TestSpeedParallelCount, 1, MAX_TEST_SPEED_PARALLEL_COUNT);
        }

        /// <summary>
        /// 获取经过校验的连接超时预算
        /// </summary>
        /// <returns></returns>
        public ConnectTimeoutConfig GetConnectTimeout()
        {
            return this.ConnectTimeout == null ? new ConnectTimeoutConfig() : this.ConnectTimeout.Normalized();
        }
    }
}
