namespace FastGithub.Configuration
{
    /// <summary>
    /// 选路模式
    /// </summary>
    public enum SpeedMode
    {
        /// <summary>
        /// 延迟优先：只按tcp握手耗时排序，不产生额外流量
        /// </summary>
        Latency,

        /// <summary>
        /// 下载优先：按实测下载速率参与排序，并主动探测速率
        /// </summary>
        Throughput
    }
}
