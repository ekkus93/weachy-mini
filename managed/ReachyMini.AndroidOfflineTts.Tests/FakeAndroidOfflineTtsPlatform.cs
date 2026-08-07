#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal sealed class FakeAndroidOfflineTtsPlatform : IAndroidOfflineTtsPlatform
{
    private readonly List<AndroidOfflineTtsPlatformVoice> voices =
        new List<AndroidOfflineTtsPlatformVoice>();
    private readonly List<AndroidOfflineTtsPlatformEvent> events =
        new List<AndroidOfflineTtsPlatformEvent>();
    private int disposed;

    public AndroidOfflineTtsProbe Probe { get; set; } = new AndroidOfflineTtsProbe(
        26,
        true,
        AndroidOfflineTtsLanguageStatus.ExactAvailable,
        1,
        1,
        0,
        AndroidOfflineTtsProvider.DefaultMaximumInputCharacters,
        "Synthetic Android offline TTS probe.");

    public Exception? ProbeException { get; set; }
    public Exception? VoicesException { get; set; }
    public Exception? StreamException { get; set; }
    public bool BlockSpeechUntilCancellation { get; set; }
    public bool EndWithoutTerminal { get; set; }
    public int ProbeCalls { get; private set; }
    public int VoiceCalls { get; private set; }
    public int SpeakCalls { get; private set; }
    public int CancellationObservations { get; private set; }
    public int DisposeCalls { get; private set; }
    public string? LastVoiceId { get; private set; }
    public string? LastLanguageTag { get; private set; }
    public TaskCompletionSource<bool> SpeechStarted { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeAndroidOfflineTtsPlatform()
    {
        voices.Add(OfflineVoice("offline-en", "Offline English", true));
    }

    public static AndroidOfflineTtsPlatformVoice OfflineVoice(
        string voiceId,
        string displayName,
        bool installed,
        string languageTag = "en-US") =>
        new AndroidOfflineTtsPlatformVoice(
            voiceId,
            displayName,
            languageTag,
            SpeechNetworkRequirement.None,
            installed);

    public static AndroidOfflineTtsPlatformVoice NetworkVoice(
        string voiceId,
        string displayName,
        string languageTag = "en-US") =>
        new AndroidOfflineTtsPlatformVoice(
            voiceId,
            displayName,
            languageTag,
            SpeechNetworkRequirement.Required,
            true);

    public void SetVoices(params AndroidOfflineTtsPlatformVoice[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        voices.Clear();
        voices.AddRange(values);
    }

    public void SetEvents(params AndroidOfflineTtsPlatformEvent[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        events.Clear();
        events.AddRange(values);
    }

    public ValueTask<AndroidOfflineTtsProbe> ProbeAsync(
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ProbeCalls++;
        LastLanguageTag = languageTag;
        if (ProbeException != null)
        {
            throw ProbeException;
        }
        return new ValueTask<AndroidOfflineTtsProbe>(Probe);
    }

    public ValueTask<IReadOnlyList<AndroidOfflineTtsPlatformVoice>> GetVoicesAsync(
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        VoiceCalls++;
        LastLanguageTag = languageTag;
        if (VoicesException != null)
        {
            throw VoicesException;
        }
        IReadOnlyList<AndroidOfflineTtsPlatformVoice> copy = voices.ToArray();
        return new ValueTask<IReadOnlyList<AndroidOfflineTtsPlatformVoice>>(copy);
    }

    public async IAsyncEnumerable<AndroidOfflineTtsPlatformEvent> SpeakAsync(
        string requestId,
        string text,
        string languageTag,
        string voiceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        ThrowIfDisposed();
        SpeakCalls++;
        LastVoiceId = voiceId;
        LastLanguageTag = languageTag;
        SpeechStarted.TrySetResult(true);

        if (StreamException != null)
        {
            await Task.Yield();
            throw StreamException;
        }

        if (BlockSpeechUntilCancellation)
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

        foreach (AndroidOfflineTtsPlatformEvent value in events)
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
            yield return new AndroidOfflineTtsPlatformEvent(
                requestId,
                AndroidOfflineTtsPlatformEventKind.Started);
            yield return new AndroidOfflineTtsPlatformEvent(
                requestId,
                AndroidOfflineTtsPlatformEventKind.Completed);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }
}
