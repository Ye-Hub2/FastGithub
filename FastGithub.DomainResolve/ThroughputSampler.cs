using System;
using System.Collections.Concurrent;
using System.Net;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 下载速率采样器
    /// </summary>
    public sealed class ThroughputSampler : IThroughputSampler
    {
        /// <summary>
        /// 样本的最小字节数
        /// </summary>
        private const long MIN_SAMPLE_BYTES = 32 * 1024;

        /// <summary>
        /// 指数平滑系数
        /// </summary>
        private const double ALPHA = 0.3d;

        private readonly TimeSpan sampleExpiration = TimeSpan.FromMinutes(10d);
        private readonly ConcurrentDictionary<IPEndPoint, ThroughputSample> samples = new();

        /// <summary>
        /// 速率样本
        /// </summary>
        /// <param name="BytesPerSecond">字节/秒</param>
        /// <param name="Tick">采样时刻</param>
        private record ThroughputSample(double BytesPerSecond, long Tick);

        /// <summary>
        /// 记录一次下载采样
        /// </summary>
        /// <param name="endPoint">下载使用的节点</param>
        /// <param name="bytes">读取的字节数</param>
        /// <param name="elapsed">读取耗时</param>
        public void Record(IPEndPoint endPoint, long bytes, TimeSpan elapsed)
        {
            if (bytes < MIN_SAMPLE_BYTES || elapsed <= TimeSpan.Zero)
            {
                return;
            }

            var bytesPerSecond = bytes / elapsed.TotalSeconds;
            if (double.IsFinite(bytesPerSecond) == false || bytesPerSecond <= 0d)
            {
                return;
            }

            var tick = Environment.TickCount64;
            this.samples.AddOrUpdate(
                endPoint,
                _ => new ThroughputSample(bytesPerSecond, tick),
                (_, old) => new ThroughputSample(old.BytesPerSecond * (1d - ALPHA) + bytesPerSecond * ALPHA, tick));
        }

        /// <summary>
        /// 获取节点的下载速率(字节/秒)
        /// </summary>
        /// <param name="endPoint">下载使用的节点</param>
        /// <returns>没有样本或样本已过期时返回null</returns>
        public double? GetBytesPerSecond(IPEndPoint endPoint)
        {
            if (this.samples.TryGetValue(endPoint, out var sample) == false)
            {
                return null;
            }

            if (Environment.TickCount64 - sample.Tick > (long)this.sampleExpiration.TotalMilliseconds)
            {
                this.samples.TryRemove(endPoint, out _);
                return null;
            }

            return sample.BytesPerSecond;
        }
    }
}
