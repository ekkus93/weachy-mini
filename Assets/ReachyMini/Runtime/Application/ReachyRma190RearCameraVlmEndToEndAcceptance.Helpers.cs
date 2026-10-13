#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Performance;
using ReachyMini.Providers;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyRma190RearCameraVlmEndToEndAcceptance
    {
        private static bool HasAvailableRearCamera(
            ReachyCameraCapabilitySnapshot capabilities)
        {
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (camera.Facing == ReachyDeviceCameraFacing.Rear &&
                    camera.Availability ==
                        ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<ReachyCameraTextureFrameDescriptor>
            WaitForRearTextureAsync(
                ReachyAndroidCameraAcquisition acquisition,
                ReachyAndroidCameraTextureBridge bridge,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                acquisition.RefreshNow();
                ReachyCameraAcquisitionSnapshot acquisitionState =
                    acquisition.State.Current;
                if (acquisitionState.State ==
                        ReachyCameraAcquisitionState.Faulted ||
                    acquisitionState.State ==
                        ReachyCameraAcquisitionState.Unavailable ||
                    acquisitionState.State ==
                        ReachyCameraAcquisitionState.PermissionRevoked)
                {
                    throw new InvalidOperationException(
                        "Rear camera acquisition failed: " +
                        acquisitionState.State + " / " +
                        acquisitionState.Message);
                }

                ReachyCameraTextureFrameDescriptor? descriptor =
                    bridge.Current.Frame;
                if (acquisitionState.State ==
                        ReachyCameraAcquisitionState.Running &&
                    bridge.Current.HasTexture &&
                    descriptor != null &&
                    descriptor.LensFacing ==
                        ReachyDeviceCameraFacing.Rear &&
                    descriptor.SessionId == acquisitionState.SessionId)
                {
                    return descriptor;
                }

                await Task.Delay(
                    ReadyPollMilliseconds,
                    cancellationToken);
            }
        }

        private static async Task<VlmAdmissionEvidence>
            WaitForStableVlmAdmissionAsync(
                ReachyPriorityDegradationRuntimeDriver driver,
                CancellationToken cancellationToken)
        {
            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(AdmissionTimeoutMilliseconds);

            ReachyPriorityDegradationDecision? previousDecision = null;
            int consecutiveStableSamples = 0;
            string lastDiagnostic =
                "No production priority-degradation sample was observed.";

            try
            {
                while (consecutiveStableSamples <
                    RequiredStableAdmissionSamples)
                {
                    ReachyPriorityDegradationDecision decision =
                        await WaitForNextPriorityDecisionAsync(
                            driver,
                            previousDecision,
                            timeout.Token);
                    previousDecision = decision;
                    lastDiagnostic = decision.Diagnostic;

                    bool physicsSignalAvailable =
                        (decision.Reasons &
                            ReachyPriorityDegradationReason
                                .PhysicsSignalUnavailable) == 0;
                    if (decision.VlmAllowed && physicsSignalAvailable)
                    {
                        ++consecutiveStableSamples;
                    }
                    else
                    {
                        consecutiveStableSamples = 0;
                    }

                    if (consecutiveStableSamples <
                        RequiredStableAdmissionSamples)
                    {
                        await Task.Delay(
                            AdmissionObservationMilliseconds,
                            timeout.Token);
                    }
                }
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Production VLM admission did not stabilize within " +
                    AdmissionTimeoutMilliseconds + " ms; last decision: " +
                    lastDiagnostic);
            }

            return new VlmAdmissionEvidence(
                consecutiveStableSamples,
                lastDiagnostic);
        }

        private static async Task<ReachyPriorityDegradationDecision>
            WaitForNextPriorityDecisionAsync(
                ReachyPriorityDegradationRuntimeDriver driver,
                ReachyPriorityDegradationDecision? previousDecision,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyPriorityDegradationDecision? decision =
                    driver.CurrentDecision;
                if (decision != null &&
                    !ReferenceEquals(decision, previousDecision))
                {
                    return decision;
                }

                await Task.Delay(
                    ReadyPollMilliseconds,
                    cancellationToken);
            }
        }

        private readonly struct VlmAdmissionEvidence
        {
            public VlmAdmissionEvidence(
                int stableSamples,
                string lastDiagnostic)
            {
                StableSamples = stableSamples;
                LastDiagnostic = lastDiagnostic;
            }

            public int StableSamples { get; }

            public string LastDiagnostic { get; }
        }

        private static void ValidateRearFrame(
            ReachyVisionFrame frame,
            string cameraId,
            ulong sessionId)
        {
            if (frame.Origin !=
                    VisionFrameOrigin.TransformedReachyEye ||
                !frame.IsObservationEligible ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(
                    VisionResourceKind.ValidityMask))
            {
                throw new InvalidOperationException(
                    "The rear camera did not produce an " +
                    "observation-eligible transformed Reachy-eye frame.");
            }
            if (!string.Equals(
                    frame.Identity.CameraId,
                    cameraId,
                    StringComparison.Ordinal) ||
                frame.Identity.SourceSessionId != sessionId ||
                frame.Identity.SourceSequence == 0UL ||
                frame.Identity.AuthoritativeSequence == 0UL)
            {
                throw new InvalidOperationException(
                    "The transformed frame does not belong to the active " +
                    "real rear-camera session and authoritative state.");
            }
        }

        // The fixture is never a general-purpose provider endpoint. Check the
        // platform security policy independently of the application's RMA-140
        // provider-profile gate, so a dropped Android manifest resource fails
        // explicitly before the costly real-camera acceptance workload.
        private static void RequireLoopbackCleartextPolicy(
            string fixtureBaseUrl)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Uri.TryCreate(
                    fixtureBaseUrl,
                    UriKind.Absolute,
                    out Uri? fixtureUri) ||
                !string.Equals(
                    fixtureUri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase) ||
                !(string.Equals(
                    fixtureUri.Host,
                    "localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(
                    fixtureUri.Host,
                    "127.0.0.1",
                    StringComparison.Ordinal)) ||
                !string.IsNullOrEmpty(fixtureUri.UserInfo) ||
                !string.IsNullOrEmpty(fixtureUri.Query) ||
                !string.IsNullOrEmpty(fixtureUri.Fragment))
            {
                throw new InvalidOperationException(
                    "RMA-190 requires an explicitly configured HTTP loopback " +
                    "fixture, with no credentials, query, or fragment.");
            }

            bool permitted;
            try
            {
                using var policyClass = new AndroidJavaClass(
                    "android.security.NetworkSecurityPolicy");
                using AndroidJavaObject policy =
                    policyClass.CallStatic<AndroidJavaObject>("getInstance");
                permitted = policy.Call<bool>(
                    "isCleartextTrafficPermitted",
                    fixtureUri.Host);
            }
            catch (AndroidJavaException)
            {
                throw new InvalidOperationException(
                    "Android network-security policy could not be queried. " +
                    "Local cleartext has not been authorized.");
            }

            if (!permitted)
            {
                throw new InvalidOperationException(
                    "Android network-security policy forbids HTTP to the " +
                    "explicit loopback fixture. Verify that the packaged APK " +
                    "contains the restricted RMA-190 network-security resource.");
            }
#else
            throw new PlatformNotSupportedException(
                "RMA-190 loopback policy preflight requires an Android player.");
#endif
        }

        private static string ReadStringLaunchExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<string>("getStringExtra", name) ??
                string.Empty;
#else
            return string.Empty;
#endif
        }

        private static void SetStage(string stage)
        {
            currentStage = stage;
            Debug.Log(
                "RMA-190 rear-camera/VLM acceptance stage: " +
                stage + ".");
        }

        [Serializable]
        private sealed class Report
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string stage = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public bool real_rear_camera;
            public bool transformed_reachy_eye_frame;
            public bool deterministic_calibration_fixture;
            public bool measured_calibration_claimed;
            public string calibration_provenance = string.Empty;
            public string camera_id = string.Empty;
            public ulong camera_session_id;
            public ulong source_sequence;
            public ulong authoritative_sequence;
            public uint continuity_id;
            public double coverage_fraction;
            public bool external_vlm_fixture;
            public bool fixture_is_live_cloud_evidence;
            public bool production_vlm_transport_and_encoder;
            public bool synthetic_camera_frame_injected;
            public bool raw_camera_payload_persisted;
            public int stable_vlm_admission_samples;
            public string vlm_admission_diagnostic = string.Empty;
            public string vlm_response = string.Empty;

            // Capture only already validated camera and admission metadata.
            // Never retain the image itself or claim a VLM result here.
            public static Report ObservedFrame(
                ReachyCameraTextureFrameDescriptor descriptor,
                ReachyVisionFrame frame,
                ReachyCameraCalibrationProfile fixture,
                VlmAdmissionEvidence admission)
            {
                return new Report
                {
                    device_model = SystemInfo.deviceModel,
                    android_api_level = ReadApiLevel(),
                    real_rear_camera =
                        descriptor.LensFacing ==
                            ReachyDeviceCameraFacing.Rear,
                    transformed_reachy_eye_frame =
                        frame.Origin ==
                            VisionFrameOrigin.TransformedReachyEye,
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance =
                        fixture.Provenance.ToString(),
                    camera_id = descriptor.CameraId,
                    camera_session_id = descriptor.SessionId,
                    source_sequence = frame.Identity.SourceSequence,
                    authoritative_sequence =
                        frame.Identity.AuthoritativeSequence,
                    continuity_id = frame.Identity.ContinuityId,
                    coverage_fraction = frame.Coverage.Fraction,
                    external_vlm_fixture = true,
                    fixture_is_live_cloud_evidence = false,
                    synthetic_camera_frame_injected = false,
                    raw_camera_payload_persisted = false,
                    stable_vlm_admission_samples =
                        admission.StableSamples,
                    vlm_admission_diagnostic =
                        admission.LastDiagnostic,
                };
            }

            public static Report Success(
                ReachyCameraTextureFrameDescriptor descriptor,
                ReachyVisionFrame frame,
                ReachyCameraCalibrationProfile fixture,
                VlmAdmissionEvidence admission,
                string response)
            {
                Report report = ObservedFrame(
                    descriptor,
                    frame,
                    fixture,
                    admission);
                report.status = "passed";
                report.stage = "completed";
                report.production_vlm_transport_and_encoder = true;
                report.vlm_response = response;
                return report;
            }

            public static Report Failure(
                string error,
                string stage,
                Report? observedEvidence)
            {
                // Fields not observed remain false/zero/empty. In particular,
                // a failure before a physical capture must not claim that a
                // real rear camera or deterministic fixture was exercised.
                Report report = observedEvidence ?? new Report();
                report.status = "failed";
                report.error = Bound(error, 4096);
                report.stage = Bound(stage, 256);
                report.device_model = SystemInfo.deviceModel;
                report.android_api_level = TryReadApiLevel();
                return report;
            }

        }

        private static int ReadApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version =
                new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
#else
            return 0;
#endif
        }

        private static int TryReadApiLevel()
        {
            try
            {
                return ReadApiLevel();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            if (File.Exists(path + ".tmp"))
            {
                File.Delete(path + ".tmp");
            }
        }

        private static void WriteAtomically(
            string path,
            string content)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "The rear-camera/VLM result path has no parent.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static string Bound(
            string value,
            int maximumCharacters)
        {
            string bounded = string.IsNullOrWhiteSpace(value)
                ? "RMA-190 rear-camera/VLM acceptance failed without " +
                    "diagnostics."
                : value;
            return bounded.Length <= maximumCharacters
                ? bounded
                : bounded.Substring(0, maximumCharacters);
        }
    }
}
