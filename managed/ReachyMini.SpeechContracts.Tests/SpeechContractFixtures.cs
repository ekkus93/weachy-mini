#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class SpeechContractFixtures
{
    public static SpeechProviderDescriptor CreateDescriptor(
        SpeechProviderKind kind,
        string instanceId,
        SpeechProviderLocation location,
        SpeechNetworkRequirement networkRequirement)
    {
        return new SpeechProviderDescriptor(
            kind,
            $"provider-{instanceId}",
            instanceId,
            instanceId,
            "1",
            location,
            networkRequirement);
    }

    public static SpeechOperationContext CreateContext(
        SpeechProviderDescriptor descriptor,
        string requestId = "request-1")
    {
        var selection = new SpeechProviderSelection(descriptor);
        return new SpeechOperationContext(
            requestId,
            selection.Current,
            TimeSpan.FromSeconds(30));
    }

    public static SpeechProviderError CreateError()
    {
        return new SpeechProviderError(
            SpeechErrorCategory.ServiceFailure,
            "fixture_failure",
            "Fixture failure.",
            false);
    }
}

internal sealed class FakeAsrProvider : IAsrProvider
{
    private int disposed;

    public SpeechProviderDescriptor Descriptor { get; } =
        SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "fixture-asr",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);

    public AsrCapabilities Capabilities { get; } = new AsrCapabilities(
        new[] { "en-US" },
        true,
        true,
        TimeSpan.FromMinutes(1));

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
        AsrOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<SpeechProviderAvailability>(
            new SpeechProviderAvailability(
                SpeechAvailabilityState.Available,
                "ready"));
    }

    public async IAsyncEnumerable<AsrEvent> RecognizeAsync(
        AsrRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SpeechProviderContract.ValidateProviderForOperation(
            Descriptor,
            request.Context);
        yield return new AsrEvent(
            Descriptor.InstanceId,
            request.Context.RequestId,
            1UL,
            AsrEventKind.Started);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref disposed, 1);
        GC.SuppressFinalize(this);
        return default;
    }
}

internal sealed class FakeTtsProvider : ITtsProvider
{
    private static readonly IReadOnlyList<TtsVoice> Voices = new[]
    {
        new TtsVoice(
            "offline",
            "Offline",
            "en-US",
            SpeechNetworkRequirement.None,
            true),
    };

    private int disposed;

    public SpeechProviderDescriptor Descriptor { get; } =
        SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.TextToSpeech,
            "fixture-tts",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);

    public TtsCapabilities Capabilities { get; } = new TtsCapabilities(
        true,
        10_000);

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<SpeechProviderAvailability>(
            new SpeechProviderAvailability(
                SpeechAvailabilityState.Available,
                "ready"));
    }

    public ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<TtsVoice>>(Voices);
    }

    public async IAsyncEnumerable<TtsEvent> SpeakAsync(
        TtsRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SpeechProviderContract.ValidateProviderForOperation(
            Descriptor,
            request.Context);
        yield return new TtsEvent(
            Descriptor.InstanceId,
            request.Context.RequestId,
            1UL,
            TtsEventKind.Started);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref disposed, 1);
        GC.SuppressFinalize(this);
        return default;
    }
}
