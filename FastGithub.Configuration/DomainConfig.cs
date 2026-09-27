using System;
using System.Net;

namespace FastGithub.Configuration
{
    /// <summary>
    /// 域名配置
    /// </summary>
    public record DomainConfig
    {
        /// <summary>
        /// 是否发送SNI
        /// </summary>
        public bool TlsSni { get; init; }

        /// <summary>
        /// 自定义SNI值的表达式
        /// </summary>
        public string? TlsSniPattern { get; init; }

        /// <summary>
        /// 是否忽略服务器证书域名不匹配
        /// 当不发送SNI时服务器可能发回域名不匹配的证书
        /// </summary>
        public bool TlsIgnoreNameMismatch { get; init; }

        /// <summary>
        /// 是否允许不受信任的服务器证书
        /// 证书链不可信（自签名/过期/未知CA）时是否仍然连接，默认为false
        /// </summary>
        public bool TlsAllowUntrustedCert { get; init; }

        /// <summary>
        /// 使用的ip地址
        /// </summary>
        public IPAddress? IPAddress { get; init; }

        /// <summary>
        /// 请求超时时长
        /// </summary>
        public TimeSpan? Timeout { get; init; }

        /// <summary>
        /// 下载速率主动探测用的地址(可选，必须是https)
        /// 仅下载优先模式下使用，未配置则该域名不做主动探测
        /// </summary>
        public Uri? SpeedTestUri { get; init; }

        /// <summary>
        /// 目的地
        /// 格式为相对或绝对uri
        /// </summary>
        public Uri? Destination { get; init; }

        /// <summary>
        /// 自定义响应
        /// </summary>
        public ResponseConfig? Response { get; init; }

        /// <summary>
        /// 获取TlsSniPattern
        /// </summary>
        /// <returns></returns>
        public TlsSniPattern GetTlsSniPattern()
        {
            if (this.TlsSni == false)
            {
                return Configuration.TlsSniPattern.None;
            }
            if (string.IsNullOrEmpty(this.TlsSniPattern))
            {
                return Configuration.TlsSniPattern.Domain;
            }
            return new TlsSniPattern(this.TlsSniPattern);
        } 
    }
}
