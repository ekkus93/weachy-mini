#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal sealed class FakeAndroidSystemTtsPlatform : IAndroidSystemTtsPlatform
{
    private readonly List<AndroidSystemTtsPlatformVoice> voices =
        new List<AndroidSystemTtsPlatformVoice>();
    private readonly List<AndroidSystemTtsPlatformEvent> events =
        new List<AndroidSystemTtsPlatformEvent>();
    private int disposed;

    public AndroidSystemTtsProbe Probe { get; set; } = new AndroidSystemTtsProbe(
        26,
        true,
        2,
        1,
        1,
        AndroidSystemTtsProvider.DefaultMaximumInputCharacters,
        "Synthetic Android system TTS probe.");

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
    public bool? LastNetworkVoiceApproved { get; private set; }
    public TaskCompletionSource<bool> SpeechStarted { get; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeAndroidSystemTtsPlatform()
    {
        voices.Add(OfflineVoice("offline-en", "Offline English", true));
        voices.Add(NetworkVoice("network-en", "Network English"));
    }

    public static AndroidSystemTtsPlatformVoice OfflineVoice(
        string voiceId,
        string displayName,
        bool installed,
        string languageTag = "en-US") =>
        new AndroidSystemTtsPlatformVoice(
            voiceId,
            displayName,
            languageTag,
            SpeechNetworkRequirement.None,
            installed);

    public static AndroidSystemTtsPlatformVoice NetworkVoice(
        string voiceId,
        string displayName,
        string languageTag = "en-US") =>
        new AndroidSystemTtsPlatformVoice(
            voiceId,
            displayName,
            languageTag,
            SpeechNetworkRequirement.Required,
            true);

    public void SetVoices(params AndroidSystemTtsPlatformVoice[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        voices.Clear();
        voices.AddRange(values);
    }

    public void SetEvents(params AndroidSystemTtsPlatformEvent[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        events.Clear();
        events.AddRange(values);
    }

    public ValueTask<AndroidSystemTtsProbe> ProbeAsync(
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
        return new ValueTask<AndroidSystemTtsProbe>(Probe);
    }

    public ValueTask<IReadOnlyList<AndroidSystemTtsPlatformVoice>> GetVoicesAsync(
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
        IReadOnlyList<AndroidSystemTtsPlatformVoice> copy = voices.ToArray();
        return new ValueTask<IReadOnlyList<AndroidSystemTtsPlatformVoice>>(copy);
    }

    public async IAsyncEnumerable<AndroidSystemTtsPlatformEvent> SpeakAsync(
        string requestId,
        string text,
        string languageTag,
        string voiceId,
        bool networkVoiceApproved,
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
        LastNetworkVoiceApproved = networkVoiceApproved;
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

        foreach (AndroidSystemTtsPlatformEvent value in events)
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
            yield return new AndroidSystemTtsPlatformEvent(
                requestId,
                AndroidSystemTtsPlatformEventKind.Started);
            yield return new AndroidSystemTtsPlatformEvent(
                requestId,
                AndroidSystemTtsPlatformEventKind.Completed);
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
