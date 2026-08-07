#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    internal sealed class SpeechAudioAcquireAttempt
    {
        public SpeechAudioAcquireResult? Result { get; private set; }
        public bool Cancelled { get; private set; }
        public Exception? Exception { get; private set; }

        public static SpeechAudioAcquireAttempt FromResult(
            SpeechAudioAcquireResult result) =>
            new SpeechAudioAcquireAttempt { Result = result };

        public static SpeechAudioAcquireAttempt FromCancellation() =>
            new SpeechAudioAcquireAttempt { Cancelled = true };

        public static SpeechAudioAcquireAttempt FromException(Exception exception) =>
            new SpeechAudioAcquireAttempt { Exception = exception };
    }

    internal static class SpeechAudioSafeOperations
    {
        public static async ValueTask<SpeechAudioAcquireAttempt> AcquireSafelyAsync(
            SpeechAudioFocusCoordinator coordinator,
            SpeechAudioRole role,
            CancellationToken cancellationToken)
        {
            try
            {
                SpeechAudioAcquireResult result = await coordinator.AcquireAsync(
                    role,
                    cancellationToken).ConfigureAwait(false);
                return SpeechAudioAcquireAttempt.FromResult(result);
            }
            catch (OperationCanceledException)
            {
                return SpeechAudioAcquireAttempt.FromCancellation();
            }
            catch (Exception exception)
            {
                return SpeechAudioAcquireAttempt.FromException(exception);
            }
        }
    }

    internal sealed class MoveNextResult<T>
        where T : class
    {
        public bool HasValue { get; private set; }
        public T? Value { get; private set; }
        public bool Cancelled { get; private set; }
        public Exception? Exception { get; private set; }

        public static MoveNextResult<T> FromValue(T value) =>
            new MoveNextResult<T> { HasValue = true, Value = value };

        public static MoveNextResult<T> Completed() =>
            new MoveNextResult<T>();

        public static MoveNextResult<T> FromCancellation() =>
            new MoveNextResult<T> { Cancelled = true };

        public static MoveNextResult<T> FromException(Exception exception) =>
            new MoveNextResult<T> { Exception = exception };
    }

    internal static class SpeechAudioEnumerator
    {
        public static async ValueTask<MoveNextResult<T>> MoveNextSafelyAsync<T>(
            IAsyncEnumerator<T> enumerator)
            where T : class
        {
            try
            {
                bool moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                return moved
                    ? MoveNextResult<T>.FromValue(enumerator.Current)
                    : MoveNextResult<T>.Completed();
            }
            catch (OperationCanceledException)
            {
                return MoveNextResult<T>.FromCancellation();
            }
            catch (Exception exception)
            {
                return MoveNextResult<T>.FromException(exception);
            }
        }
    }
}
