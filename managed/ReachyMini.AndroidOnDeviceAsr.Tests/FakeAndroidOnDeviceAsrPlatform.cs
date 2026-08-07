#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal sealed class FakeAndroidOnDeviceAsrPlatform :
    IAndroidOnDeviceAsrPlatform
{
    private readonly List<Func<string, AndroidOnDeviceAsrPlatformEvent>>
        eventFactories =
            new List<Func<string, AndroidOnDeviceAsrPlatformEvent>>();
    private int disposed;
    private int cancellationObserved;

    public FakeAndroidOnDeviceAsrPlatform()
    {
        Probe = new AndroidOnDeviceAsrProbe(33, true, true, true);
        Support = new AndroidOnDeviceAsrSupportResult(
            AndroidOnDeviceAsrSupportState.Installed,
            "installed");
        SetEvents(
            requestId => new AndroidOnDeviceAsrPlatformEvent(
                requestId,
                AndroidOnDeviceAsrPlatformEventKind.FinalResult,
                "recognized text"));
    }

    public AndroidOnDeviceAsrProbe Probe { get; set; }

    public AndroidOnDeviceAsrSupportResult Support { get; set; }

    public bool BlockUntilCancelled { get; set; }

    public bool ThrowRecognitionException { get; set; }

    public TimeSpan ProbeDelay { get; set; }

    public int ProbeCount { get; private set; }

    public int SupportCheckCount { get; private set; }

    public int RecognitionStartCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool CancellationObserved =>
        Volatile.Read(ref cancellationObserved) != 0;

    public TaskCompletionSource<bool> RecognitionStarted { get; } =
        new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetEvents(
        params Func<string, AndroidOnDeviceAsrPlatformEvent>[] factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        eventFactories.Clear();
        foreach (Func<string, AndroidOnDeviceAsrPlatformEvent> factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            eventFactories.Add(factory);
        }
    }

    public async ValueTask<AndroidOnDeviceAsrProbe> ProbeAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ProbeCount++;
        if (ProbeDelay > TimeSpan.Zero)
        {
            await Task.Delay(ProbeDelay, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Probe;
    }

    public ValueTask<AndroidOnDeviceAsrSupportResult> CheckSupportAsync(
        AsrOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        SupportCheckCount++;
        return new ValueTask<AndroidOnDeviceAsrSupportResult>(Support);
    }

    public async IAsyncEnumerable<AndroidOnDeviceAsrPlatformEvent> RecognizeAsync(
        string requestId,
        AsrOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        RecognitionStartCount++;
        RecognitionStarted.TrySetResult(true);

        if (ThrowRecognitionException)
        {
            await Task.Yield();
            throw new InvalidOperationException("synthetic platform stream failure");
        }

        if (BlockUntilCancelled)
        {
            bool cancelled = false;
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }

            if (cancelled)
            {
                Interlocked.Exchange(ref cancellationObserved, 1);
                yield return new AndroidOnDeviceAsrPlatformEvent(
                    requestId,
                    AndroidOnDeviceAsrPlatformEventKind.Cancelled);
                yield break;
            }
        }

        foreach (
            Func<string, AndroidOnDeviceAsrPlatformEvent> factory
            in eventFactories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return factory(requestId);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            DisposeCount++;
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
