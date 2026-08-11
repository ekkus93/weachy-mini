#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum TtsEncodedAudioFormat
    {
        Mp3 = 0,
        Opus = 1,
        Aac = 2,
        Flac = 3,
        Wav = 4,
        Pcm = 5,
    }

    public sealed class BufferedTtsAudio : IDisposable
    {
        private byte[]? audioBytes;
        private int disposed;

        public BufferedTtsAudio(
            byte[] audioBytes,
            TtsEncodedAudioFormat format,
            string contentType)
        {
            if (audioBytes == null)
            {
                throw new ArgumentNullException(nameof(audioBytes));
            }
            if (audioBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Buffered TTS audio cannot be empty.",
                    nameof(audioBytes));
            }
            if (!Enum.IsDefined(typeof(TtsEncodedAudioFormat), format))
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            if (!IsSafeContentType(contentType))
            {
                throw new ArgumentException(
                    "Buffered TTS audio requires a bounded response content type.",
                    nameof(contentType));
            }

            this.audioBytes = (byte[])audioBytes.Clone();
            Format = format;
            ContentType = contentType;
        }

        public TtsEncodedAudioFormat Format { get; }

        public string ContentType { get; }

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
            byte[] source = audioBytes ??
                throw new ObjectDisposedException(nameof(BufferedTtsAudio));
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
                throw new ObjectDisposedException(nameof(BufferedTtsAudio));
            }
        }

        private static bool IsSafeContentType(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 256)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (character < 0x21 || character > 0x7e)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public interface IBufferedTtsAudioSink : IAsyncDisposable
    {
        ValueTask PlayAsync(
            string requestId,
            BufferedTtsAudio audio,
            CancellationToken cancellationToken);
    }
}
