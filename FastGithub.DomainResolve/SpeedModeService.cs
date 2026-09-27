using FastGithub.Configuration;
using Microsoft.Extensions.Options;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 选路模式服务
    /// </summary>
    public sealed class SpeedModeService
    {
        private volatile SpeedMode mode;

        /// <summary>
        /// 当前选路模式
        /// </summary>
        public SpeedMode Mode => this.mode;

        /// <summary>
        /// 选路模式服务
        /// </summary>
        /// <param name="options"></param>
        public SpeedModeService(IOptions<FastGithubOptions> options)
        {
            this.mode = options.Value.SpeedMode;
        }

        /// <summary>
        /// 设置选路模式
        /// </summary>
        /// <param name="mode"></param>
        /// <returns>模式是否发生变化</returns>
        public bool SetMode(SpeedMode mode)
        {
            if (this.mode == mode)
            {
                return false;
            }

            this.mode = mode;
            return true;
        }
    }
}
