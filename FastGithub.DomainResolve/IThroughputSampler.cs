using System;
using System.Net;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 下载速率采样器
    /// </summary>
    public interface IThroughputSampler
    {
        /// <summary>
        /// 记录一次下载采样
        /// </summary>
        /// <param name="endPoint">下载使用的节点</param>
        /// <param name="bytes">读取的字节数</param>
        /// <param name="elapsed">读取耗时</param>
        void Record(IPEndPoint endPoint, long bytes, TimeSpan elapsed);

        /// <summary>
        /// 获取节点的下载速率(字节/秒)
        /// </summary>
        /// <param name="endPoint">下载使用的节点</param>
        /// <returns>没有样本时返回null</returns>
        double? GetBytesPerSecond(IPEndPoint endPoint);
    }
}
