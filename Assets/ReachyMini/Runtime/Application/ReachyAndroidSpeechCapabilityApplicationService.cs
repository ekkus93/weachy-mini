#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ReachyMini.AppState
{
    // Bridges the real, already-implemented ASR/TTS provider pipeline (the same
    // provider factories RMA-184's device probe uses to measure real hardware) into
    // the application-service composition layer. Both
    // application composition and
    // ReachySettingsApplicationCompositionProvider previously stubbed "speech-audio"
    // permanently Unavailable with a message dating to 2026-07-31, before speech was
    // built; this replaces that with a real, on-device probe of on-device ASR,
    // system ASR (broader API-level coverage than the explicit on-device path, which
    // requires API 31+), and offline TTS. The service reports Ready if any one of the
    // three is usable, matching how a real subsequent interaction would pick
    // whichever provider actually works on this device.
    //
    // This deliberately never requests the RECORD_AUDIO permission itself: RMA-090
    // requires that the app not proactively surface a permission dialog on launch
    // (only in response to an explicit user action), the same contract camera
    // permission already follows. If the permission has not been granted yet, this
    // reports Unavailable rather than requesting it; a later explicit user action
    // (tapping the microphone) is the appropriate place to request it, not
    // composition startup.
    internal sealed class ReachyAndroidSpeechCapabilityApplicationService :
        ReachyApplicationServiceBase,
        IReachyAudioService
    {
        private const string ProbeLanguageTag = "en-US";
        private static readonly TimeSpan ProbeUtteranceTimeout = TimeSpan.FromSeconds(15.0);

        private CancellationTokenSource? probeCancellation;

        public ReachyAndroidSpeechCapabilityApplicationService()
            : base(
                "speech-audio",
                ReachyServiceKind.Audio,
                ReachyServiceCriticality.Optional)
        {
        }

        protected override void OnInitialize()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                SetUnavailable(
                    "Microphone capture and speech playback require an Android device.");
                return;
            }

#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                SetUnavailable(
                    "Microphone permission has not been granted. Grant it via an explicit " +
                    "user action; it is never requested automatically on launch.");
                return;
            }
            // The shared service base marks any still-Initializing service Ready
            // when OnInitialize returns. Never report readiness before the
            // shipping Android ASR and TTS providers have been probed.
            SetDegraded(
                "Speech capability verification is pending; no ASR or TTS " +
                "provider has been confirmed available yet.");
            probeCancellation = new CancellationTokenSource();
            _ = ProbeAsync(probeCancellation.Token);
#else
            SetUnavailable(
                "This build target does not include Android microphone permission support.");
#endif
        }

        protected override void OnDispose()
        {
            probeCancellation?.Cancel();
            probeCancellation?.Dispose();
            probeCancellation = null;
        }

        private static async Task<SpeechProviderAvailability> ProbeIndependentlyAsync(
            Func<CancellationToken, Task<SpeechProviderAvailability>> probe,
            string providerName,
            CancellationToken cancellationToken)
        {
            try
            {
                return await probe(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                // A failed capability check must not prevent the independent
                // offline providers from reporting their own availability.
                // Do not expose platform exception messages or select a fallback.
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Faulted,
                    providerName + " capability probe failed with " +
                    exception.GetType().Name + ".");
            }
        }

        private async Task ProbeAsync(CancellationToken cancellationToken)
        {
            try
            {
                SpeechProviderAvailability onDeviceAsr =
                    await ProbeIndependentlyAsync(
                        ProbeOnDeviceAsrAsync,
                        "on_device_asr",
                        cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                SpeechProviderAvailability systemAsr =
                    await ProbeIndependentlyAsync(
                        ProbeSystemAsrAsync,
                        "system_asr",
                        cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                SpeechProviderAvailability offlineTts =
                    await ProbeIndependentlyAsync(
                        ProbeOfflineTtsAsync,
                        "offline_tts",
                        cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Providers can include platform exception messages in their
                // diagnostics. Aggregate speech health is surfaced to the UI,
                // so expose typed states without copying arbitrary provider text.
                // Setup guidance below remains explicit and network-fail-closed.
                string message =
                    "on_device_asr=" + onDeviceAsr.State + "; " +
                    "system_asr=" + systemAsr.State + "; " +
                    "offline_tts=" + offlineTts.State + ".";
                if (onDeviceAsr.IsAvailable || systemAsr.IsAvailable || offlineTts.IsAvailable)
                {
                    SetReady(
                        "At least one speech capability is available. " +
                        "Check individual ASR/TTS states before starting a turn. " + message);
                }
                else
                {
                    SetDegraded(
                        "Microphone permission is granted, but no usable ASR or TTS " +
                        "provider was found on this device. Install or enable an " +
                        "explicit on-device ASR service (requires Android API 31+) " +
                        "and an offline TTS voice in device settings, then retry. " + message);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // A provider may fail while disposal is cancelling the
                    // probe. Never overwrite Disposed with a late fault.
                    return;
                }
                // Android provider exceptions may contain private payloads.
                // Preserve only the exception class in visible diagnostics.
                SetFaulted(
                    "Speech capability probe failed with " + exception.GetType().Name +
                    ". No network speech provider was selected automatically.");
            }
        }

        private static async Task<SpeechProviderAvailability> ProbeOnDeviceAsrAsync(
            CancellationToken cancellationToken)
        {
            IAsrProvider provider = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                "main-screen-on-device-asr-probe", ProbeLanguageTag, ProbeUtteranceTimeout);
            try
            {
                return await provider.CheckAvailabilityAsync(
                    new AsrOptions(ProbeLanguageTag, requestPartialResults: false),
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task<SpeechProviderAvailability> ProbeSystemAsrAsync(
            CancellationToken cancellationToken)
        {
            IAsrProvider provider = ReachyAndroidSystemAsrProviderFactory.Create(
                "main-screen-system-asr-probe", ProbeLanguageTag, ProbeUtteranceTimeout);
            try
            {
                return await provider.CheckAvailabilityAsync(
                    new AsrOptions(ProbeLanguageTag, requestPartialResults: false),
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task<SpeechProviderAvailability> ProbeOfflineTtsAsync(
            CancellationToken cancellationToken)
        {
            ITtsProvider provider = ReachyAndroidOfflineTtsProviderFactory.Create(
                "main-screen-offline-tts-probe", ProbeLanguageTag);
            try
            {
                return await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }
    }
}
