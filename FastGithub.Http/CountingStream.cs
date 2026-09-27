using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FastGithub.DomainResolve;

namespace FastGithub.Http
{
    /// <summary>
    /// 统计下载字节数的流
    /// 空闲间隔超过阈值时切分样本，避免连接复用时的空闲时间稀释速率
    /// </summary>
    public sealed class CountingStream : Stream
    {
        private readonly Stream inner;
        private readonly IPEndPoint endPoint;
        private readonly IThroughputSampler sampler;
        private readonly long idleGapMilliseconds = (long)TimeSpan.FromSeconds(5d).TotalMilliseconds;

        private long bytes;
        private long windowStartTick;
        private long lastReadTick;

        /// <summary>
        /// 统计下载字节数的流
        /// </summary>
        /// <param name="inner"></param>
        /// <param name="endPoint"></param>
        /// <param name="sampler"></param>
        public CountingStream(Stream inner, IPEndPoint endPoint, IThroughputSampler sampler)
        {
            this.inner = inner;
            this.endPoint = endPoint;
            this.sampler = sampler;
        }

        public override bool CanRead => this.inner.CanRead;

        public override bool CanSeek => this.inner.CanSeek;

        public override bool CanWrite => this.inner.CanWrite;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public override void Flush() => this.inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => this.inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = this.inner.Read(buffer, offset, count);
            this.OnRead(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = this.inner.Read(buffer);
            this.OnRead(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await this.inner.ReadAsync(buffer, cancellationToken);
            this.OnRead(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await this.inner.ReadAsync(buffer, offset, count, cancellationToken);
            this.OnRead(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);

        public override void SetLength(long value) => this.inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => this.inner.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => this.inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => this.inner.WriteAsync(buffer, cancellationToken);

        /// <summary>
        /// 释放资源
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing == true)
            {
                this.FlushSample();
                this.inner.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 读取之后统计字节数
        /// </summary>
        private void OnRead(int read)
        {
            if (read <= 0)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (this.bytes > 0 && now - this.lastReadTick > this.idleGapMilliseconds)
            {
                this.FlushSample();
            }
            if (this.bytes == 0)
            {
                this.windowStartTick = now;
            }

            this.bytes += read;
            this.lastReadTick = now;
        }

        /// <summary>
        /// 结算当前样本
        /// </summary>
        private void FlushSample()
        {
            var bytes = this.bytes;
            var elapsed = TimeSpan.FromMilliseconds(this.lastReadTick - this.windowStartTick);
            this.bytes = 0;
            this.windowStartTick = 0;
            this.lastReadTick = 0;

            if (bytes > 0 && elapsed > TimeSpan.Zero)
            {
                this.sampler.Record(this.endPoint, bytes, elapsed);
            }
        }
    }
}
