#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class TtsContractTests
{
    public static Task RequiresCancellation()
    {
        AssertEx.Throws<ArgumentException>(() => new TtsCapabilities(false, 1000));
        return Task.CompletedTask;
    }

    public static Task InputLimitIsBounded()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            new TtsCapabilities(true, 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            new TtsCapabilities(true, 1_000_001));
        return Task.CompletedTask;
    }

    public static Task RejectsAsrSelection()
    {
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Throws<ArgumentException>(() =>
            new TtsRequest(context, "hello", "voice"));
        return Task.CompletedTask;
    }

    public static Task FailureInvariant()
    {
        SpeechProviderError error = SpeechContractFixtures.CreateError();
        _ = new TtsEvent(
            "tts",
            "request",
            1UL,
            TtsEventKind.Failed,
            error);
        AssertEx.Throws<ArgumentException>(() => new TtsEvent(
            "tts",
            "request",
            1UL,
            TtsEventKind.Failed));
        AssertEx.Throws<ArgumentException>(() => new TtsEvent(
            "tts",
            "request",
            1UL,
            TtsEventKind.Completed,
            error));
        return Task.CompletedTask;
    }

    public static Task VoiceNetworkRequirement()
    {
        var offline = new TtsVoice(
            "offline",
            "Offline",
            "en-US",
            SpeechNetworkRequirement.None,
            true);
        var network = new TtsVoice(
            "network",
            "Network",
            "en-US",
            SpeechNetworkRequirement.Required,
            true);
        AssertEx.False(
            offline.MayUseNetwork,
            "Offline voice must remain explicitly non-networked.");
        AssertEx.True(
            network.MayUseNetwork,
            "Network voice must remain explicitly networked.");
        return Task.CompletedTask;
    }

    public static Task CancellationSignature()
    {
        MethodInfo check = SpeechContractTests.RequireMethod(
            typeof(ITtsProvider),
            nameof(ITtsProvider.CheckAvailabilityAsync));
        MethodInfo voices = SpeechContractTests.RequireMethod(
            typeof(ITtsProvider),
            nameof(ITtsProvider.GetVoicesAsync));
        MethodInfo speak = SpeechContractTests.RequireMethod(
            typeof(ITtsProvider),
            nameof(ITtsProvider.SpeakAsync));
        SpeechContractTests.AssertLastParameterIsCancellationToken(check);
        SpeechContractTests.AssertLastParameterIsCancellationToken(voices);
        SpeechContractTests.AssertLastParameterIsCancellationToken(speak);
        return Task.CompletedTask;
    }

    public static async Task CancellationPropagation()
    {
        await using var provider = new FakeTtsProvider();
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            provider.Descriptor);
        var request = new TtsRequest(context, "hello", "offline");
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<TtsEvent> enumerator = provider
            .SpeakAsync(request, cancellation.Token)
            .GetAsyncEnumerator();
        AssertEx.True(
            await enumerator.MoveNextAsync().ConfigureAwait(false),
            "TTS should publish Started before waiting.");
        cancellation.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task DisposeIsExplicit()
    {
        var provider = new FakeTtsProvider();
        await provider.DisposeAsync().ConfigureAwait(false);
        AssertEx.True(
            provider.IsDisposed,
            "TTS provider disposal must be explicit and observable in fixture.");
    }
}
