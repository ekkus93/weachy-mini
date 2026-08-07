#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class SpeechContractTests
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new List<(string Name, Func<Task> Run)>
        {
            ("ASR and TTS are separate interfaces", SeparateInterfaces),
            ("On-device provider cannot claim network use", OnDeviceNetworkContract),
            ("Cloud provider must disclose required network", CloudNetworkContract),
            ("Local-network provider must disclose required network", LocalNetworkContract),
            ("Device-service provider may be provider-controlled", DeviceServiceNetworkContract),
            ("Provider descriptor rejects empty identity", DescriptorRejectsEmptyIdentity),
            ("Availability is explicit", AvailabilityIsExplicit),
            ("Structured error preserves category and code", StructuredErrorPreservesDetail),
            ("Structured error text is bounded", StructuredErrorIsBounded),
            ("Provider selection kind cannot change", SelectionKindCannotChange),
            ("Provider selection epoch advances", SelectionEpochAdvances),
            ("Old operation context becomes stale", OldContextBecomesStale),
            ("Operation timeout is bounded", TimeoutIsBounded),
            ("Provider redirect is rejected", ProviderRedirectRejected),
            ("Event provider redirect is rejected", EventProviderRedirectRejected),
            ("Event request substitution is rejected", EventRequestRejected),
            ("Default policy disables provider fallback", NoProviderFallback),
            ("Default policy disables privacy-boundary fallback", NoPrivacyFallback),
            ("Default policy disables automatic retry", NoAutomaticRetry),
            ("ASR capabilities require languages", AsrContractTests.RequiresLanguages),
            ("ASR languages are unique", AsrContractTests.LanguagesAreUnique),
            ("ASR requires cancellation", AsrContractTests.RequiresCancellation),
            ("ASR request rejects TTS selection", AsrContractTests.RejectsTtsSelection),
            ("ASR transcript appears only on result events", AsrContractTests.TranscriptInvariant),
            ("ASR failure requires structured error", AsrContractTests.FailureInvariant),
            ("ASR interface exposes cancellation tokens", AsrContractTests.CancellationSignature),
            ("ASR cancellation reaches provider", AsrContractTests.CancellationPropagation),
            ("ASR dispose is explicit", AsrContractTests.DisposeIsExplicit),
            ("TTS requires cancellation", TtsContractTests.RequiresCancellation),
            ("TTS input limit is bounded", TtsContractTests.InputLimitIsBounded),
            ("TTS request rejects ASR selection", TtsContractTests.RejectsAsrSelection),
            ("TTS failure requires structured error", TtsContractTests.FailureInvariant),
            ("Voice network requirement is visible", TtsContractTests.VoiceNetworkRequirement),
            ("TTS interface exposes cancellation tokens", TtsContractTests.CancellationSignature),
            ("TTS cancellation reaches provider", TtsContractTests.CancellationPropagation),
            ("TTS dispose is explicit", TtsContractTests.DisposeIsExplicit),
        };

    private static Task SeparateInterfaces()
    {
        AssertEx.True(typeof(IAsrProvider).IsInterface, "IAsrProvider must be an interface.");
        AssertEx.True(typeof(ITtsProvider).IsInterface, "ITtsProvider must be an interface.");
        AssertEx.False(
            typeof(IAsrProvider).IsAssignableFrom(typeof(ITtsProvider)),
            "TTS must not inherit ASR.");
        AssertEx.False(
            typeof(ITtsProvider).IsAssignableFrom(typeof(IAsrProvider)),
            "ASR must not inherit TTS.");
        return Task.CompletedTask;
    }

    private static Task OnDeviceNetworkContract()
    {
        AssertEx.Throws<ArgumentException>(() =>
            _ = SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-on-device",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.ProviderControlled));
        return Task.CompletedTask;
    }

    private static Task CloudNetworkContract()
    {
        AssertEx.Throws<ArgumentException>(() =>
            _ = SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-cloud",
                SpeechProviderLocation.Cloud,
                SpeechNetworkRequirement.None));
        SpeechProviderDescriptor descriptor = SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "asr-cloud",
            SpeechProviderLocation.Cloud,
            SpeechNetworkRequirement.Required);
        AssertEx.True(
            descriptor.RequiresNetworkDisclosure,
            "Cloud provider must disclose networking.");
        return Task.CompletedTask;
    }

    private static Task LocalNetworkContract()
    {
        AssertEx.Throws<ArgumentException>(() =>
            _ = SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.TextToSpeech,
                "tts-lan",
                SpeechProviderLocation.LocalNetwork,
                SpeechNetworkRequirement.ProviderControlled));
        return Task.CompletedTask;
    }

    private static Task DeviceServiceNetworkContract()
    {
        SpeechProviderDescriptor descriptor = SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "android-system-asr",
            SpeechProviderLocation.DeviceService,
            SpeechNetworkRequirement.ProviderControlled);
        AssertEx.True(
            descriptor.MayUseNetwork,
            "Provider-controlled service must be labeled as potentially networked.");
        return Task.CompletedTask;
    }

    private static Task DescriptorRejectsEmptyIdentity()
    {
        AssertEx.Throws<ArgumentException>(() =>
            _ = new SpeechProviderDescriptor(
                SpeechProviderKind.TextToSpeech,
                string.Empty,
                "instance",
                "display",
                "1",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        return Task.CompletedTask;
    }

    private static Task AvailabilityIsExplicit()
    {
        var available = new SpeechProviderAvailability(
            SpeechAvailabilityState.Available,
            "ready");
        var setup = new SpeechProviderAvailability(
            SpeechAvailabilityState.SetupRequired,
            "install language data");
        AssertEx.True(available.IsAvailable, "Available must report true.");
        AssertEx.False(setup.IsAvailable, "SetupRequired must not be available.");
        return Task.CompletedTask;
    }

    private static Task StructuredErrorPreservesDetail()
    {
        var error = new SpeechProviderError(
            SpeechErrorCategory.Permission,
            "microphone_denied",
            "Microphone permission is required.",
            false);
        AssertEx.Equal(
            SpeechErrorCategory.Permission,
            error.Category,
            "Error category mismatch.");
        AssertEx.Equal("microphone_denied", error.Code, "Error code mismatch.");
        AssertEx.False(error.IsRetryable, "Fixture permission denial is not retryable.");
        return Task.CompletedTask;
    }

    private static Task StructuredErrorIsBounded()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _ = new SpeechProviderError(
                SpeechErrorCategory.Unknown,
                new string('c', SpeechProviderError.MaximumCodeCharacters + 1),
                "detail",
                false));
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _ = new SpeechProviderError(
                SpeechErrorCategory.Unknown,
                "code",
                new string('d', SpeechProviderError.MaximumDiagnosticCharacters + 1),
                false));
        return Task.CompletedTask;
    }

    private static Task SelectionKindCannotChange()
    {
        var selection = new SpeechProviderSelection(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-a",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Throws<ArgumentException>(() =>
            _ = selection.Select(
                SpeechContractFixtures.CreateDescriptor(
                    SpeechProviderKind.TextToSpeech,
                    "tts-a",
                    SpeechProviderLocation.OnDevice,
                    SpeechNetworkRequirement.None)));
        return Task.CompletedTask;
    }

    private static Task SelectionEpochAdvances()
    {
        var selection = new SpeechProviderSelection(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-a",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        ulong first = selection.Current.Epoch;
        SpeechProviderSelectionSnapshot second = selection.Select(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-b",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Equal(first + 1UL, second.Epoch, "Selection epoch mismatch.");
        AssertEx.Equal("asr-b", second.ProviderInstanceId, "Provider mismatch.");
        return Task.CompletedTask;
    }

    private static Task OldContextBecomesStale()
    {
        SpeechProviderDescriptor first = SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "asr-a",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);
        var selection = new SpeechProviderSelection(first);
        var context = new SpeechOperationContext(
            "request",
            selection.Current,
            TimeSpan.FromSeconds(10));
        _ = selection.Select(SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "asr-b",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None));
        AssertEx.False(
            selection.IsCurrent(context),
            "Old operation context must become stale after selection changes.");
        return Task.CompletedTask;
    }

    private static Task TimeoutIsBounded()
    {
        SpeechProviderSelectionSnapshot snapshot = new SpeechProviderSelection(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None)).Current;
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _ = new SpeechOperationContext(
                "request",
                snapshot,
                TimeSpan.Zero));
        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _ = new SpeechOperationContext(
                "request",
                snapshot,
                SpeechOperationContext.MaximumTimeout + TimeSpan.FromTicks(1)));
        return Task.CompletedTask;
    }

    private static Task ProviderRedirectRejected()
    {
        SpeechProviderDescriptor selected = SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "asr-selected",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(selected);
        SpeechProviderDescriptor fallback = SpeechContractFixtures.CreateDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "asr-fallback",
            SpeechProviderLocation.Cloud,
            SpeechNetworkRequirement.Required);
        AssertEx.Throws<InvalidOperationException>(() =>
            SpeechProviderContract.ValidateProviderForOperation(fallback, context));
        return Task.CompletedTask;
    }

    private static Task EventProviderRedirectRejected()
    {
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "asr-selected",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Throws<InvalidOperationException>(() =>
            SpeechProviderContract.ValidateEventOrigin(
                context,
                "asr-fallback",
                context.RequestId));
        return Task.CompletedTask;
    }

    private static Task EventRequestRejected()
    {
        SpeechOperationContext context = SpeechContractFixtures.CreateContext(
            SpeechContractFixtures.CreateDescriptor(
                SpeechProviderKind.TextToSpeech,
                "tts-selected",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None));
        AssertEx.Throws<InvalidOperationException>(() =>
            SpeechProviderContract.ValidateEventOrigin(
                context,
                context.ProviderInstanceId,
                "other-request"));
        return Task.CompletedTask;
    }

    private static Task NoProviderFallback()
    {
        AssertEx.False(
            new SpeechProviderPolicy().AutomaticProviderFallbackEnabled,
            "Automatic provider fallback must default off.");
        return Task.CompletedTask;
    }

    private static Task NoPrivacyFallback()
    {
        AssertEx.False(
            new SpeechProviderPolicy().CrossPrivacyBoundaryFallbackEnabled,
            "Cross-privacy-boundary fallback must default off.");
        return Task.CompletedTask;
    }

    private static Task NoAutomaticRetry()
    {
        AssertEx.False(
            new SpeechProviderPolicy().AutomaticRetryEnabled,
            "Automatic retry must default off at the provider-contract layer.");
        return Task.CompletedTask;
    }

    internal static MethodInfo RequireMethod(Type type, string name)
    {
        MethodInfo? method = type.GetMethod(name);
        return method ?? throw new InvalidOperationException(
            $"Missing method {type.Name}.{name}.");
    }

    internal static void AssertLastParameterIsCancellationToken(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        AssertEx.True(parameters.Length > 0, $"{method.Name} must accept parameters.");
        AssertEx.Equal(
            typeof(CancellationToken),
            parameters[^1].ParameterType,
            $"{method.Name} must end with CancellationToken.");
    }
}
