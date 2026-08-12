#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static partial class AndroidOnDeviceAsrTests
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new (string, Func<Task>)[]
        {
            ("Descriptor is explicit on-device with no network", DescriptorIsExplicitOnDevice),
            ("API 30 reports explicit provider unavailable", Api30Unavailable),
            ("API 31 missing explicit recognizer reports unavailable", Api31RecognizerUnavailable),
            ("Microphone permission is required before support or start", PermissionRequired),
            ("API 31 skips unavailable language preflight without faking support", Api31PreflightUnavailable),
            ("API 33 installed language is available", InstalledLanguageAvailable),
            ("Missing language model requires setup", ModelDownloadRequired),
            ("Pending language model requires setup", ModelDownloadPending),
            ("Unsupported language is unavailable", UnsupportedLanguage),
            ("Recognizer support preflight inability stays visible", PreflightUnavailableStillAvailable),
            ("Support discovery fault is faulted", SupportFaulted),
            ("Configured language is enforced", ConfiguredLanguageEnforced),
            ("Partial and final results preserve order", PartialAndFinalResults),
            ("No-match remains explicit", NoMatch),
            ("Concurrent utterances fail busy without queueing", BusyWithoutQueue),
            ("Speech timeout maps to typed timeout", PlatformSpeechTimeout),
            ("Operation timeout cancels the Android platform", OperationTimeoutCancels),
            ("Caller cancellation reaches the Android platform", CallerCancellationReachesPlatform),
            ("Service disconnect is visible", ServiceDisconnected),
            ("Language model absence is visible", LanguageModelUnavailable),
            ("Runtime unsupported language is visible", RuntimeUnsupportedLanguage),
            ("Unexpected network failure is a locality contract violation", NetworkFailureIsContractViolation),
            ("Mismatched callback request identity fails closed", CallbackIdentityMismatch),
            ("Missing terminal callback fails visibly", StreamEndsWithoutTerminal),
            ("Platform stream exception fails visibly", PlatformExceptionVisible),
            ("Provider selection cannot redirect to another instance", ProviderRedirectRejected),
            ("Provider does not retry a failed utterance", NoAutomaticRetry),
            ("Disposal cancels an active utterance and destroys platform", DisposeCancelsActive),
            ("Disposed provider rejects new availability checks", DisposedProviderRejectsUse),
            ("Maximum utterance duration caps a longer operation timeout", MaximumUtteranceCapsTimeout),
            ("Java bridge uses only explicit on-device recognizer", Rma121SourceContracts.JavaBridgeUsesOnlyExplicitOnDeviceRecognizer),
            ("ASR Android manifest declares audio and service visibility", Rma121SourceContracts.ManifestDeclaresAudioAndRecognitionServiceVisibility),
            ("Unity bridge marshals callbacks without fallback", Rma121SourceContracts.UnityBridgeMarshalsCallbacksWithoutFallback),
        };
}
