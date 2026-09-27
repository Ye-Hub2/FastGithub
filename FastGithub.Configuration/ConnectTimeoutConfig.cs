using System;

namespace FastGithub.Configuration
{
    /// <summary>
    /// tcp连接的超时预算
    /// </summary>
    public record ConnectTimeoutConfig
    {
        /// <summary>
        /// 首个ip的连接预算，包含tcp连接与tls握手
        /// </summary>
        public TimeSpan FirstIp { get; init; } = TimeSpan.FromSeconds(10d);

        /// <summary>
        /// 故障转移ip的连接预算
        /// </summary>
        public TimeSpan FailoverIp { get; init; } = TimeSpan.FromSeconds(3d);

        /// <summary>
        /// 整个连接阶段的总预算
        /// </summary>
        public TimeSpan Total { get; init; } = TimeSpan.FromSeconds(15d);

        /// <summary>
        /// 获取经过校验的配置，非正数时回退为默认值
        /// </summary>
        /// <returns></returns>
        public ConnectTimeoutConfig Normalized()
        {
            var defaults = new ConnectTimeoutConfig();
            return new ConnectTimeoutConfig
            {
                FirstIp = this.FirstIp > TimeSpan.Zero ? this.FirstIp : defaults.FirstIp,
                FailoverIp = this.FailoverIp > TimeSpan.Zero ? this.FailoverIp : defaults.FailoverIp,
                Total = this.Total > TimeSpan.Zero ? this.Total : defaults.Total
            };
        }
    }
}
