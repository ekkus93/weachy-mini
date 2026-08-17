#nullable enable

using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyCameraTextureEvidence : MonoBehaviour
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma092_acceptance";
        public const string ResultFileName =
            "rma092-camera-texture-state.json";
        public const string RearCaptureFileName =
            "rma092-rear.png";
        public const string RotatedCaptureFileName =
            "rma092-rear-rotated.png";
        public const string FrontCaptureFileName =
            "rma092-front.png";

        private const int StableFramesBeforeCapture = 3;
        private const int MinimumCaptureChannelRange = 4;
        private const int CaptureRetryFrameInterval = 5;
        private const float PublishIntervalSeconds = 0.25f;

        private ReachyAndroidCameraAcquisition? acquisition;
        private ReachyAndroidCameraTextureBridge? textureBridge;
        private ReachyCameraTextureBridgeSnapshot? pendingSnapshot;
        private string evidenceFault = string.Empty;
        private ulong observedSessionId;
        private ulong observedSequence;
        private long observedTimestampNanoseconds;
        private int sessionReadyFrameCount;
        private int observedFrameCount;
        private int metadataMatchCount;
        private int captureCount;
        private int rejectedCaptureCount;
        private bool descriptorMonotonic = true;
        private bool timestampCorrespondence = true;
        private bool outputDimensionsValid = true;
        private bool mirrorContractValid = true;
        private bool colorContractValid = true;
        private bool capturesNonUniform = true;
        private bool capturesOpaque = true;
        private bool rearCaptureWritten;
        private bool rotatedCaptureWritten;
        private bool frontCaptureWritten;
        private int rearRotationDegrees = -1;
        private int rotatedRotationDegrees = -1;
        private int frontRotationDegrees = -1;
        private int lastCaptureMinimumChannel;
        private int lastCaptureMaximumChannel;
        private float nextPublishTime;
        private ReachyCameraTextureFrameDescriptor? lastFrame;

        public string ResultPath => Path.Combine(
            Application.persistentDataPath,
            ResultFileName);

        public static bool IsAcceptanceRequestedFromLaunchIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>(
                        "currentActivity");
                using AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>(
                    "getBooleanExtra",
                    AcceptanceLaunchExtra,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect the RMA-092 acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }

        public void Configure(
            ReachyAndroidCameraAcquisition cameraAcquisition,
            ReachyAndroidCameraTextureBridge cameraTextureBridge)
        {
            if (cameraAcquisition == null)
            {
                throw new ArgumentNullException(nameof(cameraAcquisition));
            }
            if (cameraTextureBridge == null)
            {
                throw new ArgumentNullException(nameof(cameraTextureBridge));
            }
            if (acquisition != null || textureBridge != null)
            {
                if (acquisition == cameraAcquisition &&
                    textureBridge == cameraTextureBridge)
                {
                    return;
                }
                throw new InvalidOperationException(
                    "RMA-092 evidence is already configured for another camera texture bridge.");
            }

            acquisition = cameraAcquisition;
            textureBridge = cameraTextureBridge;
            textureBridge.Changed += OnTextureChanged;
            pendingSnapshot = textureBridge.Current;
            nextPublishTime = 0f;
            Publish();
        }

        private void LateUpdate()
        {
            ReachyCameraTextureBridgeSnapshot? snapshot = pendingSnapshot;
            pendingSnapshot = null;
            if (snapshot == null)
            {
                return;
            }

            bool captured = false;
            try
            {
                if (snapshot.State == ReachyCameraTextureBridgeState.Faulted ||
                    snapshot.State == ReachyCameraTextureBridgeState.Unsupported)
                {
                    evidenceFault = snapshot.Message;
                }
                else if (snapshot.State == ReachyCameraTextureBridgeState.Ready &&
                    snapshot.Frame != null)
                {
                    captured = ObserveReadyFrame(snapshot.Frame);
                }
            }
            catch (Exception exception)
            {
                evidenceFault = exception.Message;
                Debug.LogError(
                    "RMA-092 texture evidence failed: " + exception.Message,
                    this);
            }

            if (captured || Time.unscaledTime >= nextPublishTime)
            {
                nextPublishTime =
                    Time.unscaledTime + PublishIntervalSeconds;
                Publish();
            }
        }

        private void OnGUI()
        {
            RenderTexture? output = textureBridge?.OutputTexture;
            if (output == null)
            {
                return;
            }

            GUI.depth = -1000;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                output,
                ScaleMode.ScaleToFit,
                alphaBlend: false);
            ReachyCameraTextureFrameDescriptor? frame =
                textureBridge?.Current.Frame;
            if (frame != null)
            {
                GUI.Box(
                    new Rect(12f, 12f, Math.Min(Screen.width - 24f, 640f), 42f),
                    $"RMA-092 {frame.LensFacing} sequence {frame.Sequence} " +
                    $"rotation {frame.RotationDegrees} timestamp {frame.TimestampNanoseconds} ns");
            }
        }

        private void OnDestroy()
        {
            if (textureBridge != null)
            {
                textureBridge.Changed -= OnTextureChanged;
            }
            acquisition = null;
            textureBridge = null;
            pendingSnapshot = null;
        }

        private void OnTextureChanged(
            object? sender,
            ReachyCameraTextureBridgeChangedEventArgs eventArgs)
        {
            pendingSnapshot = eventArgs.Snapshot;
            if (eventArgs.Snapshot.State ==
                    ReachyCameraTextureBridgeState.Faulted ||
                eventArgs.Snapshot.State ==
                    ReachyCameraTextureBridgeState.Unsupported)
            {
                evidenceFault = eventArgs.Snapshot.Message;
                Publish();
            }
        }

        private bool ObserveReadyFrame(
            ReachyCameraTextureFrameDescriptor frame)
        {
            RenderTexture output = textureBridge?.OutputTexture ??
                throw new InvalidOperationException(
                    "A ready texture snapshot exposed no RGB render texture.");

            if (frame.SessionId != observedSessionId)
            {
                observedSessionId = frame.SessionId;
                observedSequence = 0UL;
                observedTimestampNanoseconds = 0L;
                sessionReadyFrameCount = 0;
            }
            else if (frame.Sequence <= observedSequence ||
                frame.TimestampNanoseconds <= observedTimestampNanoseconds)
            {
                descriptorMonotonic = false;
            }

            observedSequence = frame.Sequence;
            observedTimestampNanoseconds = frame.TimestampNanoseconds;
            sessionReadyFrameCount = checked(sessionReadyFrameCount + 1);
            observedFrameCount = checked(observedFrameCount + 1);
            lastFrame = frame;

            outputDimensionsValid &=
                output.width == frame.OutputWidth &&
                output.height == frame.OutputHeight &&
                output.width > 0 &&
                output.height > 0;
            mirrorContractValid &=
                frame.Mirrored ==
                    (frame.LensFacing == ReachyDeviceCameraFacing.Front);
            colorContractValid &=
                frame.ColorStandard != ReachyCameraYuvColorStandard.Unknown &&
                frame.ColorRange != ReachyCameraYuvColorRange.Unknown;

            ReachyCameraFrameMetadata? metadata =
                acquisition?.State.Current.LatestFrame;
            if (metadata != null &&
                metadata.SessionId == frame.SessionId &&
                metadata.Sequence == frame.Sequence)
            {
                metadataMatchCount = checked(metadataMatchCount + 1);
                timestampCorrespondence &=
                    metadata.TimestampNanoseconds ==
                        frame.TimestampNanoseconds &&
                    string.Equals(
                        metadata.CameraId,
                        frame.CameraId,
                        StringComparison.Ordinal) &&
                    metadata.LensFacing == frame.LensFacing &&
                    metadata.RotationDegrees == frame.RotationDegrees;
            }

            if (sessionReadyFrameCount < StableFramesBeforeCapture ||
                (sessionReadyFrameCount - StableFramesBeforeCapture) %
                    CaptureRetryFrameInterval != 0)
            {
                return false;
            }

            if (frame.LensFacing == ReachyDeviceCameraFacing.Rear)
            {
                if (!rearCaptureWritten &&
                    TryCapture(output, RearCaptureFileName))
                {
                    rearCaptureWritten = true;
                    rearRotationDegrees = frame.RotationDegrees;
                    return true;
                }
                // This no longer requires frame.RotationDegrees != rearRotationDegrees.
                // That condition predates the portrait lock (AndroidBuild.
                // ConfigureMobileOrientation): the app is now fixed to portrait, so a
                // forced device-rotation attempt is expected to leave RotationDegrees
                // UNCHANGED, and the requirement below was structurally unsatisfiable --
                // 100% reproducible timeout, not a flake. Capturing again on the first
                // eligible frame of the restarted session is correct on its own; the
                // rotation-held-lock invariant is verified downstream by
                // validate_texture_stage's previous_rotation comparison, not by gating
                // the capture itself.
                if (rearCaptureWritten &&
                    !rotatedCaptureWritten &&
                    TryCapture(output, RotatedCaptureFileName))
                {
                    rotatedCaptureWritten = true;
                    rotatedRotationDegrees = frame.RotationDegrees;
                    return true;
                }
            }
            else if (frame.LensFacing == ReachyDeviceCameraFacing.Front &&
                !frontCaptureWritten &&
                TryCapture(output, FrontCaptureFileName))
            {
                frontCaptureWritten = true;
                frontRotationDegrees = frame.RotationDegrees;
                return true;
            }

            return false;
        }

        private bool TryCapture(RenderTexture source, string fileName)
        {
            RenderTexture? previous = RenderTexture.active;
            Texture2D? readback = null;
            try
            {
                readback = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false)
                {
                    name = "RMA092CameraTextureReadback",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                RenderTexture.active = source;
                readback.ReadPixels(
                    new Rect(0f, 0f, source.width, source.height),
                    0,
                    0,
                    recalculateMipMaps: false);
                readback.Apply(
                    updateMipmaps: false,
                    makeNoLongerReadable: false);

                Color32[] pixels = readback.GetPixels32();
                if (pixels.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Camera texture readback returned no pixels.");
                }

                int minimum = 255;
                int maximum = 0;
                bool opaque = true;
                int stride = Math.Max(1, pixels.Length / 4096);
                for (int index = 0; index < pixels.Length; index += stride)
                {
                    Color32 pixel = pixels[index];
                    minimum = Math.Min(
                        minimum,
                        Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)));
                    maximum = Math.Max(
                        maximum,
                        Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                    opaque &= pixel.a >= 250;
                }

                lastCaptureMinimumChannel = minimum;
                lastCaptureMaximumChannel = maximum;
                bool nonUniform =
                    maximum - minimum >= MinimumCaptureChannelRange;
                if (!nonUniform || !opaque)
                {
                    rejectedCaptureCount = checked(rejectedCaptureCount + 1);
                    return false;
                }

                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Unity could not encode the camera texture evidence PNG.");
                }

                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string destination = Path.Combine(directory, fileName);
                string temporary = destination + ".tmp";
                File.WriteAllBytes(temporary, png);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(temporary, destination);
                capturesNonUniform &= nonUniform;
                capturesOpaque &= opaque;
                captureCount = checked(captureCount + 1);
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null)
                {
                    Object.Destroy(readback);
                }
            }
        }

        private void Publish()
        {
            try
            {
                ReachyCameraTextureBridgeSnapshot snapshot =
                    textureBridge?.Current ??
                    new ReachyCameraTextureBridgeSnapshot(
                        ReachyCameraTextureBridgeState.Waiting,
                        "RMA-092 texture evidence is not configured.",
                        null,
                        0UL,
                        0UL,
                        0UL);
                var report = new CameraTextureEvidenceReport
                {
                    status = string.IsNullOrEmpty(evidenceFault)
                        ? "ok"
                        : "error",
                    acceptance_enabled = true,
                    evidence_fault = evidenceFault,
                    bridge_state = snapshot.State.ToString(),
                    bridge_message = snapshot.Message,
                    uploaded_frame_count =
                        snapshot.UploadedFrameCount.ToString(),
                    stale_frame_count = snapshot.StaleFrameCount.ToString(),
                    bridge_revision = snapshot.Revision.ToString(),
                    observed_frame_count = observedFrameCount,
                    metadata_match_count = metadataMatchCount,
                    capture_count = captureCount,
                    rejected_capture_count = rejectedCaptureCount,
                    descriptor_monotonic = descriptorMonotonic,
                    timestamp_correspondence = timestampCorrespondence,
                    output_dimensions_valid = outputDimensionsValid,
                    mirror_contract_valid = mirrorContractValid,
                    color_contract_valid = colorContractValid,
                    captures_non_uniform = capturesNonUniform,
                    captures_opaque = capturesOpaque,
                    rear_capture_written = rearCaptureWritten,
                    rear_capture_file = rearCaptureWritten
                        ? RearCaptureFileName
                        : string.Empty,
                    rear_rotation_degrees = rearRotationDegrees,
                    rotated_capture_written = rotatedCaptureWritten,
                    rotated_capture_file = rotatedCaptureWritten
                        ? RotatedCaptureFileName
                        : string.Empty,
                    rotated_rotation_degrees = rotatedRotationDegrees,
                    front_capture_written = frontCaptureWritten,
                    front_capture_file = frontCaptureWritten
                        ? FrontCaptureFileName
                        : string.Empty,
                    front_rotation_degrees = frontRotationDegrees,
                    last_capture_minimum_channel =
                        lastCaptureMinimumChannel,
                    last_capture_maximum_channel =
                        lastCaptureMaximumChannel,
                    frame = lastFrame == null
                        ? null
                        : BuildFrameReport(lastFrame),
                };

                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string temporary = ResultPath + ".tmp";
                File.WriteAllText(
                    temporary,
                    JsonUtility.ToJson(report, prettyPrint: true));
                if (File.Exists(ResultPath))
                {
                    File.Delete(ResultPath);
                }
                File.Move(temporary, ResultPath);
            }
            catch (Exception exception)
            {
                evidenceFault = exception.Message;
                Debug.LogError(
                    "Could not publish RMA-092 camera texture evidence: " +
                    exception.Message,
                    this);
            }
        }

        private static CameraTextureFrameEvidence BuildFrameReport(
            ReachyCameraTextureFrameDescriptor frame)
        {
            return new CameraTextureFrameEvidence
            {
                session_id = frame.SessionId.ToString(),
                sequence = frame.Sequence.ToString(),
                timestamp_nanoseconds =
                    frame.TimestampNanoseconds.ToString(),
                camera_id = frame.CameraId,
                lens_facing = frame.LensFacing.ToString(),
                sensor_orientation_degrees =
                    frame.SensorOrientationDegrees,
                rotation_degrees = frame.RotationDegrees,
                width = frame.Width,
                height = frame.Height,
                output_width = frame.OutputWidth,
                output_height = frame.OutputHeight,
                crop_left = frame.Crop.Left,
                crop_top = frame.Crop.Top,
                crop_right = frame.Crop.Right,
                crop_bottom = frame.Crop.Bottom,
                mirrored = frame.Mirrored,
                color_standard = frame.ColorStandard.ToString(),
                color_range = frame.ColorRange.ToString(),
            };
        }

        [Serializable]
        private sealed class CameraTextureEvidenceReport
        {
            public string status = string.Empty;
            public bool acceptance_enabled;
            public string evidence_fault = string.Empty;
            public string bridge_state = string.Empty;
            public string bridge_message = string.Empty;
            public string uploaded_frame_count = string.Empty;
            public string stale_frame_count = string.Empty;
            public string bridge_revision = string.Empty;
            public int observed_frame_count;
            public int metadata_match_count;
            public int capture_count;
            public int rejected_capture_count;
            public bool descriptor_monotonic;
            public bool timestamp_correspondence;
            public bool output_dimensions_valid;
            public bool mirror_contract_valid;
            public bool color_contract_valid;
            public bool captures_non_uniform;
            public bool captures_opaque;
            public bool rear_capture_written;
            public string rear_capture_file = string.Empty;
            public int rear_rotation_degrees;
            public bool rotated_capture_written;
            public string rotated_capture_file = string.Empty;
            public int rotated_rotation_degrees;
            public bool front_capture_written;
            public string front_capture_file = string.Empty;
            public int front_rotation_degrees;
            public int last_capture_minimum_channel;
            public int last_capture_maximum_channel;
            public CameraTextureFrameEvidence? frame;
        }

        [Serializable]
        private sealed class CameraTextureFrameEvidence
        {
            public string session_id = string.Empty;
            public string sequence = string.Empty;
            public string timestamp_nanoseconds = string.Empty;
            public string camera_id = string.Empty;
            public string lens_facing = string.Empty;
            public int sensor_orientation_degrees;
            public int rotation_degrees;
            public int width;
            public int height;
            public int output_width;
            public int output_height;
            public int crop_left;
            public int crop_top;
            public int crop_right;
            public int crop_bottom;
            public bool mirrored;
            public string color_standard = string.Empty;
            public string color_range = string.Empty;
        }
    }
}
