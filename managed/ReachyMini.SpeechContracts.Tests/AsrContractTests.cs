#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class AsrContractTests
{
    public static Task RequiresLanguages()
    {
        AssertEx.Throws<ArgumentException>(() => new AsrCapabilities(
            Array.Empty<string>(),
            true,
            true,
            TimeSpan.FromMinutes(1)));
        return Task.CompletedTask;
    }

    public static Task LanguagesAreUnique()
    {
        AssertEx.Throws<ArgumentException>(() => new AsrCapabilities(
            new[] { "en-US", "EN-us" },
            true,
            true,
            TimeSpan.FromMinutes(1)));
        return Task.CompletedTask;
    }

    public static Task RequiresCancellation()
    {
        AssertEx.Throws<ArgumentException>(() => new AsrCapabilities(
            new[] { "en-US" },
            true,
            false,
            TimeSpan.FromMinutes(1)));
        return Task.CompletedTask;
    }

    public static Task RejectsTtsSelection()
    {
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.TextToSpeech,
                "tts",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Throws<ArgumentException>(() => new AsrRequest(
            context,
            new AsrOptions("en-US", true)));
        return Task.CompletedTask;
    }

    public static Task TranscriptInvariant()
    {
        _ = new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.FinalResult,
            "hello");
        AssertEx.Throws<ArgumentException>(() => new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.Started,
            "not allowed"));
        AssertEx.Throws<ArgumentException>(() => new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.PartialResult));
        return Task.CompletedTask;
    }

    public static Task FailureInvariant()
    {
        SpeechProviderError error = SpeechContractFixtures.CreateError();
        _ = new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.Failed,
            error: error);
        AssertEx.Throws<ArgumentException>(() => new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.Failed));
        AssertEx.Throws<ArgumentException>(() => new AsrEvent(
            "asr",
            "request",
            1UL,
            AsrEventKind.NoMatch,
            error: error));
        return Task.CompletedTask;
    }

    public static Task CancellationSignature()
    {
        MethodInfo check = SpeechContractTests.RequireMethod(
            typeof(IAsrProvider),
            nameof(IAsrProvider.CheckAvailabilityAsync));
        MethodInfo recognize = SpeechContractTests.RequireMethod(
            typeof(IAsrProvider),
            nameof(IAsrProvider.RecognizeAsync));
        SpeechContractTests.AssertLastParameterIsCancellationToken(check);
        SpeechContractTests.AssertLastParameterIsCancellationToken(recognize);
        return Task.CompletedTask;
    }

    public static async Task CancellationPropagation()
    {
        await using var provider = new FakeAsrProvider();
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            provider.Descriptor);
        var request = new AsrRequest(
            context,
            new AsrOptions("en-US", true));
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<AsrEvent> enumerator = provider
            .RecognizeAsync(request, cancellation.Token)
            .GetAsyncEnumerator();
        AssertEx.True(
            await enumerator.MoveNextAsync().ConfigureAwait(false),
            "ASR should publish Started before waiting.");
        cancellation.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task DisposeIsExplicit()
    {
        var provider = new FakeAsrProvider();
        await provider.DisposeAsync().ConfigureAwait(false);
        AssertEx.True(
            provider.IsDisposed,
            "ASR provider disposal must be explicit and observable in fixture.");
    }
}
