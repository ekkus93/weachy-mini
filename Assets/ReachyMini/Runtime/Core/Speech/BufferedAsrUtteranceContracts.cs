#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AsrEncodedAudioFormat
    {
        Flac = 0,
        Mp3 = 1,
        Mp4 = 2,
        Mpeg = 3,
        Mpga = 4,
        M4a = 5,
        Ogg = 6,
        Wav = 7,
        Webm = 8,
    }

    public sealed class BufferedAsrUtterance : IDisposable
    {
        private byte[]? audioBytes;
        private int disposed;

        public BufferedAsrUtterance(
            byte[] audioBytes,
            AsrEncodedAudioFormat format,
            TimeSpan duration)
        {
            if (audioBytes == null)
            {
                throw new ArgumentNullException(nameof(audioBytes));
            }
            if (audioBytes.Length == 0)
            {
                throw new ArgumentException(
                    "A buffered ASR utterance cannot be empty.",
                    nameof(audioBytes));
            }
            if (!Enum.IsDefined(typeof(AsrEncodedAudioFormat), format))
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            this.audioBytes = (byte[])audioBytes.Clone();
            Format = format;
            Duration = duration;
        }

        public AsrEncodedAudioFormat Format { get; }

        public TimeSpan Duration { get; }

        public int Length
        {
            get
            {
                ThrowIfDisposed();
                return audioBytes?.Length ?? 0;
            }
        }

        public byte[] CopyAudioBytes()
        {
            ThrowIfDisposed();
            byte[] source = audioBytes ?? throw new ObjectDisposedException(
                nameof(BufferedAsrUtterance));
            return (byte[])source.Clone();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            byte[]? owned = Interlocked.Exchange(ref audioBytes, null);
            if (owned != null)
            {
                Array.Clear(owned, 0, owned.Length);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(BufferedAsrUtterance));
            }
        }
    }

    public interface IBufferedAsrUtteranceSource : IAsyncDisposable
    {
        ValueTask<BufferedAsrUtterance> CaptureUtteranceAsync(
            string requestId,
            int maximumAudioBytes,
            TimeSpan maximumDuration,
            CancellationToken cancellationToken);
    }
}
