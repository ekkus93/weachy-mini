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
    // ReachyProductionApplicationCompositionProvider and
    // ReachySettingsApplicationCompositionProvider previously stubbed "speech-audio"
    // permanently Unavailable with a message dating to 2026-07-31, before speech was
    // built; this replaces that with a real, on-device probe. Requests the
    // RECORD_AUDIO permission if it has not been granted yet, then probes on-device
    // ASR, system ASR (broader API-level coverage than the explicit on-device path,
    // which requires API 31+), and offline TTS. The service reports Ready if any one
    // of the three is usable, matching how a real subsequent interaction would pick
    // whichever provider actually works on this device.
    internal sealed class ReachyAndroidSpeechCapabilityApplicationService :
        ReachyApplicationServiceBase,
        IReachyAudioService
    {
        private const string ProbeLanguageTag = "en-US";
        private static readonly TimeSpan ProbeUtteranceTimeout = TimeSpan.FromSeconds(15.0);

        private CancellationTokenSource? probeCancellation;
#if UNITY_ANDROID
        private PermissionCallbacks? permissionCallbacks;
#endif

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

            probeCancellation = new CancellationTokenSource();
#if UNITY_ANDROID
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                _ = ProbeAsync(probeCancellation.Token);
                return;
            }

            SetDegraded("Requesting microphone permission before probing speech capability.");
            RequestMicrophonePermission();
#else
            SetUnavailable(
                "This build target does not include Android microphone permission support.");
#endif
        }

        protected override void OnDispose()
        {
#if UNITY_ANDROID
            permissionCallbacks = null;
#endif
            probeCancellation?.Cancel();
            probeCancellation?.Dispose();
            probeCancellation = null;
        }

#if UNITY_ANDROID
        private void RequestMicrophonePermission()
        {
            var callbacks = new PermissionCallbacks();
            permissionCallbacks = callbacks;
            callbacks.PermissionGranted += grantedPermission =>
            {
                permissionCallbacks = null;
                CancellationTokenSource? cancellation = probeCancellation;
                if (cancellation != null)
                {
                    _ = ProbeAsync(cancellation.Token);
                }
            };
            callbacks.PermissionDenied += deniedPermission =>
            {
                permissionCallbacks = null;
                SetUnavailable("Microphone permission was denied.");
            };
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
#endif

        private async Task ProbeAsync(CancellationToken cancellationToken)
        {
            try
            {
                SpeechProviderAvailability onDeviceAsr =
                    await ProbeOnDeviceAsrAsync(cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                SpeechProviderAvailability systemAsr =
                    await ProbeSystemAsrAsync(cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                SpeechProviderAvailability offlineTts =
                    await ProbeOfflineTtsAsync(cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                string message =
                    "on_device_asr=" + onDeviceAsr.State + " (" + onDeviceAsr.Diagnostic + "); " +
                    "system_asr=" + systemAsr.State + " (" + systemAsr.Diagnostic + "); " +
                    "offline_tts=" + offlineTts.State + " (" + offlineTts.Diagnostic + ").";
                if (onDeviceAsr.IsAvailable || systemAsr.IsAvailable || offlineTts.IsAvailable)
                {
                    SetReady(
                        "Microphone capture and speech playback are available. " + message);
                }
                else
                {
                    SetDegraded(
                        "Microphone permission is granted, but no usable ASR or TTS " +
                        "provider was found on this device. " + message);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                SetFaulted(
                    "Speech capability probe failed with " + exception.GetType().Name +
                    ": " + exception.Message);
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
