#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal sealed class FakeAndroidSystemAsrPlatform : IAndroidSystemAsrPlatform
{
    private readonly List<AndroidSystemAsrPlatformEvent> events =
        new List<AndroidSystemAsrPlatformEvent>();
    private int disposed;

    public AndroidSystemAsrProbe Probe { get; set; } =
        new AndroidSystemAsrProbe(26, true, true);
    public Exception? ProbeException { get; set; }
    public Exception? StreamException { get; set; }
    public bool BlockRecognitionUntilCancellation { get; set; }
    public bool EndWithoutTerminal { get; set; }
    public int ProbeCalls { get; private set; }
    public int RecognizeCalls { get; private set; }
    public int CancellationObservations { get; private set; }
    public int DisposeCalls { get; private set; }
    public TaskCompletionSource<bool> RecognitionStarted { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetEvents(params AndroidSystemAsrPlatformEvent[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        events.Clear();
        events.AddRange(values);
    }

    public ValueTask<AndroidSystemAsrProbe> ProbeAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ProbeCalls++;
        if (ProbeException != null)
        {
            throw ProbeException;
        }
        return new ValueTask<AndroidSystemAsrProbe>(Probe);
    }

    public async IAsyncEnumerable<AndroidSystemAsrPlatformEvent> RecognizeAsync(
        string requestId,
        AsrOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        RecognizeCalls++;
        RecognitionStarted.TrySetResult(true);

        if (StreamException != null)
        {
            await Task.Yield();
            throw StreamException;
        }

        if (BlockRecognitionUntilCancellation)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    CancellationObservations++;
                }
            }
            yield break;
        }

        foreach (AndroidSystemAsrPlatformEvent value in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return value;
            if (value.IsTerminal)
            {
                yield break;
            }
        }

        if (!EndWithoutTerminal && events.Count == 0)
        {
            yield return new AndroidSystemAsrPlatformEvent(
                requestId,
                AndroidSystemAsrPlatformEventKind.NoMatch);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            DisposeCalls++;
        }
        GC.SuppressFinalize(this);
        return default;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
    }
}

