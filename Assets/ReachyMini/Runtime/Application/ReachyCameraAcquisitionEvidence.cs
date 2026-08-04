#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyCameraAcquisitionEvidence : MonoBehaviour
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma091_acceptance";
        public const string ResultFileName =
            "rma091-camera-acquisition-state.json";
        public const string CommandFileName =
            "rma091-camera-acquisition-command.json";

        private const float CommandPollIntervalSeconds = 0.10f;

        private ReachyAndroidCameraAcquisition? acquisition;
        private ReachyAndroidCameraDiscovery? discovery;
        private ReachyCameraAcquisitionState previousState =
            ReachyCameraAcquisitionState.Stopped;
        private string permission = "Unknown";
        private string lastCommandId = string.Empty;
        private string lastCommandAction = string.Empty;
        private string lastCommandStatus = "none";
        private string lastCommandMessage =
            "No RMA-091 acceptance command has been processed.";
        private string evidenceFault = string.Empty;
        private float nextCommandPollTime;
        private ulong lastObservedSessionId;
        private ulong lastObservedFrameSessionId;
        private ulong lastObservedFrameSequence;
        private long lastObservedFrameTimestampNanoseconds;
        private int commandCount;
        private int startCommandCount;
        private int stopCommandCount;
        private int sessionCount;
        private int frontSessionCount;
        private int rearSessionCount;
        private int stateTransitionCount;
        private int runningTransitionCount;
        private int pausedTransitionCount;
        private int resumedTransitionCount;
        private int stoppedTransitionCount;
        private int permissionRevokedTransitionCount;
        private int unavailableTransitionCount;
        private int faultedTransitionCount;
        private int applicationPauseCount;
        private int applicationResumeCount;
        private int frameObservationCount;
        private bool metadataMonotonic = true;
        private bool allFramesYuv420888 = true;
        private bool allFramesValidCrop = true;
        private bool allFramesValidIntrinsics = true;
        private bool allFramesPositiveTimestamp = true;
        private bool frontFrameSeen;
        private bool rearFrameSeen;
        private bool rotation0Seen;
        private bool rotation90Seen;
        private bool rotation180Seen;
        private bool rotation270Seen;

        public string ResultPath => Path.Combine(
            Application.persistentDataPath,
            ResultFileName);

        public string CommandPath => Path.Combine(
            Application.persistentDataPath,
            CommandFileName);

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
                    "Could not inspect the RMA-091 acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }

        public void Configure(
            ReachyAndroidCameraAcquisition cameraAcquisition,
            ReachyAndroidCameraDiscovery cameraDiscovery)
        {
            if (cameraAcquisition == null)
            {
                throw new ArgumentNullException(nameof(cameraAcquisition));
            }
            if (cameraDiscovery == null)
            {
                throw new ArgumentNullException(nameof(cameraDiscovery));
            }
            if (acquisition != null || discovery != null)
            {
                if (acquisition == cameraAcquisition &&
                    discovery == cameraDiscovery)
                {
                    return;
                }
                throw new InvalidOperationException(
                    "RMA-091 evidence is already configured for another camera service.");
            }

            acquisition = cameraAcquisition;
            discovery = cameraDiscovery;
            acquisition.State.Changed += OnAcquisitionChanged;
            discovery.State.Changed += OnCapabilitiesChanged;
            permission = discovery.State.Current.Permission.ToString();
            previousState = acquisition.State.Current.State;
            ObserveSnapshot(acquisition.State.Current, countTransition: false);
            nextCommandPollTime = 0f;
            Publish();
        }

        private void Update()
        {
            if (acquisition == null || discovery == null ||
                Time.unscaledTime < nextCommandPollTime)
            {
                return;
            }

            nextCommandPollTime =
                Time.unscaledTime + CommandPollIntervalSeconds;
            ProcessCommandIfPresent();
            Publish();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                applicationPauseCount = checked(applicationPauseCount + 1);
            }
            else
            {
                applicationResumeCount = checked(applicationResumeCount + 1);
            }
            Publish();
        }

        private void OnDestroy()
        {
            if (acquisition != null)
            {
                acquisition.State.Changed -= OnAcquisitionChanged;
            }
            if (discovery != null)
            {
                discovery.State.Changed -= OnCapabilitiesChanged;
            }
            acquisition = null;
            discovery = null;
        }

        private void OnCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            permission = eventArgs.Snapshot.Permission.ToString();
            Publish();
        }

        private void OnAcquisitionChanged(
            object? sender,
            ReachyCameraAcquisitionChangedEventArgs eventArgs)
        {
            ObserveSnapshot(eventArgs.Snapshot, countTransition: true);
            Publish();
        }

        private void ObserveSnapshot(
            ReachyCameraAcquisitionSnapshot snapshot,
            bool countTransition)
        {
            if (snapshot.SessionId != 0UL &&
                snapshot.SessionId != lastObservedSessionId)
            {
                lastObservedSessionId = snapshot.SessionId;
                sessionCount = checked(sessionCount + 1);
                if (snapshot.RequestedFacing ==
                    ReachyDeviceCameraFacing.Front)
                {
                    frontSessionCount = checked(frontSessionCount + 1);
                }
                else if (snapshot.RequestedFacing ==
                    ReachyDeviceCameraFacing.Rear)
                {
                    rearSessionCount = checked(rearSessionCount + 1);
                }
            }

            if (countTransition && snapshot.State != previousState)
            {
                stateTransitionCount = checked(stateTransitionCount + 1);
                switch (snapshot.State)
                {
                    case ReachyCameraAcquisitionState.Running:
                        runningTransitionCount =
                            checked(runningTransitionCount + 1);
                        if (previousState ==
                            ReachyCameraAcquisitionState.Paused)
                        {
                            resumedTransitionCount =
                                checked(resumedTransitionCount + 1);
                        }
                        break;
                    case ReachyCameraAcquisitionState.Paused:
                        pausedTransitionCount =
                            checked(pausedTransitionCount + 1);
                        break;
                    case ReachyCameraAcquisitionState.Stopped:
                        stoppedTransitionCount =
                            checked(stoppedTransitionCount + 1);
                        break;
                    case ReachyCameraAcquisitionState.PermissionRevoked:
                        permissionRevokedTransitionCount =
                            checked(permissionRevokedTransitionCount + 1);
                        break;
                    case ReachyCameraAcquisitionState.Unavailable:
                        unavailableTransitionCount =
                            checked(unavailableTransitionCount + 1);
                        break;
                    case ReachyCameraAcquisitionState.Faulted:
                        faultedTransitionCount =
                            checked(faultedTransitionCount + 1);
                        break;
                }
            }

            previousState = snapshot.State;
            if (snapshot.LatestFrame != null)
            {
                ObserveFrame(snapshot.LatestFrame);
            }
        }

        private void ObserveFrame(ReachyCameraFrameMetadata frame)
        {
            if (frame.SessionId == lastObservedFrameSessionId &&
                frame.Sequence == lastObservedFrameSequence)
            {
                return;
            }

            if (frame.SessionId != lastObservedFrameSessionId)
            {
                lastObservedFrameSessionId = frame.SessionId;
                lastObservedFrameSequence = 0UL;
                lastObservedFrameTimestampNanoseconds = 0L;
            }
            else if (frame.Sequence <= lastObservedFrameSequence ||
                frame.TimestampNanoseconds <=
                    lastObservedFrameTimestampNanoseconds)
            {
                metadataMonotonic = false;
            }

            lastObservedFrameSequence = frame.Sequence;
            lastObservedFrameTimestampNanoseconds =
                frame.TimestampNanoseconds;
            frameObservationCount = checked(frameObservationCount + 1);
            allFramesYuv420888 &=
                frame.PixelFormat == ReachyCameraPixelFormat.Yuv420888;
            allFramesValidCrop &=
                frame.Crop.Left >= 0 &&
                frame.Crop.Top >= 0 &&
                frame.Crop.Right <= frame.Width &&
                frame.Crop.Bottom <= frame.Height &&
                frame.Crop.Width > 0 &&
                frame.Crop.Height > 0;
            allFramesValidIntrinsics &=
                frame.Intrinsics.FocalLengthX > 0f &&
                frame.Intrinsics.FocalLengthY > 0f &&
                !string.IsNullOrWhiteSpace(
                    frame.Intrinsics.Provenance);
            allFramesPositiveTimestamp &= frame.TimestampNanoseconds > 0L;

            frontFrameSeen |=
                frame.LensFacing == ReachyDeviceCameraFacing.Front;
            rearFrameSeen |=
                frame.LensFacing == ReachyDeviceCameraFacing.Rear;
            switch (frame.RotationDegrees)
            {
                case 0:
                    rotation0Seen = true;
                    break;
                case 90:
                    rotation90Seen = true;
                    break;
                case 180:
                    rotation180Seen = true;
                    break;
                case 270:
                    rotation270Seen = true;
                    break;
            }
        }

        private void ProcessCommandIfPresent()
        {
            string commandPath = CommandPath;
            if (!File.Exists(commandPath))
            {
                return;
            }

            CameraEvidenceCommand? command;
            try
            {
                string json = File.ReadAllText(commandPath);
                command = JsonUtility.FromJson<CameraEvidenceCommand>(json);
            }
            catch (Exception exception)
            {
                RecordCommandFailure(
                    "unreadable",
                    "parse",
                    "Could not parse the RMA-091 command: " +
                    exception.Message);
                return;
            }

            if (command == null || string.IsNullOrWhiteSpace(command.id))
            {
                RecordCommandFailure(
                    "invalid",
                    "parse",
                    "The RMA-091 command requires a nonempty id.");
                return;
            }
            if (string.Equals(
                    command.id,
                    lastCommandId,
                    StringComparison.Ordinal))
            {
                return;
            }

            lastCommandId = command.id;
            lastCommandAction = command.action ?? string.Empty;
            commandCount = checked(commandCount + 1);
            try
            {
                ReachyAndroidCameraAcquisition service =
                    acquisition ?? throw new InvalidOperationException(
                        "Camera acquisition evidence is not configured.");
                switch (lastCommandAction)
                {
                    case "start":
                        startCommandCount = checked(startCommandCount + 1);
                        service.StartPreferred(ParseFacing(command.facing));
                        break;
                    case "stop":
                        stopCommandCount = checked(stopCommandCount + 1);
                        service.StopAcquisition();
                        break;
                    case "refresh":
                        service.RefreshNow();
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unsupported RMA-091 command action: " +
                            lastCommandAction);
                }
                lastCommandStatus = "ok";
                lastCommandMessage =
                    "Processed RMA-091 command " + command.id + ".";
            }
            catch (Exception exception)
            {
                lastCommandStatus = "error";
                lastCommandMessage = exception.Message;
            }
        }

        private void RecordCommandFailure(
            string id,
            string action,
            string detail)
        {
            if (string.Equals(id, lastCommandId, StringComparison.Ordinal) &&
                string.Equals(detail, lastCommandMessage, StringComparison.Ordinal))
            {
                return;
            }
            lastCommandId = id;
            lastCommandAction = action;
            lastCommandStatus = "error";
            lastCommandMessage = detail;
            commandCount = checked(commandCount + 1);
        }

        private void Publish()
        {
            try
            {
                ReachyCameraAcquisitionSnapshot snapshot =
                    acquisition?.State.Current ??
                    new ReachyCameraAcquisitionStateStore().Current;
                ReachyCameraFrameMetadata? frame = snapshot.LatestFrame;
                var report = new CameraAcquisitionEvidenceReport
                {
                    status = string.IsNullOrEmpty(evidenceFault)
                        ? "ok"
                        : "error",
                    acceptance_enabled = true,
                    evidence_fault = evidenceFault,
                    permission = permission,
                    current_state = snapshot.State.ToString(),
                    message = snapshot.Message,
                    desired_active = acquisition?.DesiredActive ?? false,
                    preferred_facing =
                        acquisition?.PreferredFacing.ToString() ??
                        ReachyCameraFacing.Unconfigured.ToString(),
                    current_session_id = snapshot.SessionId.ToString(),
                    camera_id = snapshot.CameraId,
                    requested_facing = snapshot.RequestedFacing.ToString(),
                    accepted_frame_count =
                        snapshot.AcceptedFrameCount.ToString(),
                    stale_frame_count = snapshot.StaleFrameCount.ToString(),
                    revision = snapshot.Revision.ToString(),
                    has_frame = frame != null,
                    frame = frame == null
                        ? null
                        : BuildFrameReport(frame),
                    last_command_id = lastCommandId,
                    last_command_action = lastCommandAction,
                    last_command_status = lastCommandStatus,
                    last_command_message = lastCommandMessage,
                    command_count = commandCount,
                    start_command_count = startCommandCount,
                    stop_command_count = stopCommandCount,
                    session_count = sessionCount,
                    front_session_count = frontSessionCount,
                    rear_session_count = rearSessionCount,
                    state_transition_count = stateTransitionCount,
                    running_transition_count = runningTransitionCount,
                    paused_transition_count = pausedTransitionCount,
                    resumed_transition_count = resumedTransitionCount,
                    stopped_transition_count = stoppedTransitionCount,
                    permission_revoked_transition_count =
                        permissionRevokedTransitionCount,
                    unavailable_transition_count =
                        unavailableTransitionCount,
                    faulted_transition_count = faultedTransitionCount,
                    application_pause_count = applicationPauseCount,
                    application_resume_count = applicationResumeCount,
                    frame_observation_count = frameObservationCount,
                    metadata_monotonic = metadataMonotonic,
                    all_frames_yuv420888 = allFramesYuv420888,
                    all_frames_valid_crop = allFramesValidCrop,
                    all_frames_valid_intrinsics =
                        allFramesValidIntrinsics,
                    all_frames_positive_timestamp =
                        allFramesPositiveTimestamp,
                    front_frame_seen = frontFrameSeen,
                    rear_frame_seen = rearFrameSeen,
                    rotation_0_seen = rotation0Seen,
                    rotation_90_seen = rotation90Seen,
                    rotation_180_seen = rotation180Seen,
                    rotation_270_seen = rotation270Seen,
                };

                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string temporaryPath = ResultPath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonUtility.ToJson(report, prettyPrint: true));
                if (File.Exists(ResultPath))
                {
                    File.Delete(ResultPath);
                }
                File.Move(temporaryPath, ResultPath);
            }
            catch (Exception exception)
            {
                evidenceFault = exception.Message;
                Debug.LogError(
                    "Could not publish RMA-091 camera acquisition evidence: " +
                    exception.Message,
                    this);
            }
        }

        private static ReachyCameraFacing ParseFacing(string? facing)
        {
            return string.Equals(
                    facing,
                    "front",
                    StringComparison.OrdinalIgnoreCase)
                ? ReachyCameraFacing.Front
                : ReachyCameraFacing.Rear;
        }

        private static CameraFrameEvidence BuildFrameReport(
            ReachyCameraFrameMetadata frame)
        {
            return new CameraFrameEvidence
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
                crop_left = frame.Crop.Left,
                crop_top = frame.Crop.Top,
                crop_right = frame.Crop.Right,
                crop_bottom = frame.Crop.Bottom,
                pixel_format = frame.PixelFormat.ToString(),
                intrinsics_source = frame.Intrinsics.Source.ToString(),
                focal_length_x = frame.Intrinsics.FocalLengthX,
                focal_length_y = frame.Intrinsics.FocalLengthY,
                principal_point_x = frame.Intrinsics.PrincipalPointX,
                principal_point_y = frame.Intrinsics.PrincipalPointY,
                skew = frame.Intrinsics.Skew,
                intrinsics_provenance = frame.Intrinsics.Provenance,
            };
        }

        [Serializable]
        private sealed class CameraEvidenceCommand
        {
            public string id = string.Empty;
            public string action = string.Empty;
            public string facing = string.Empty;
        }

        [Serializable]
        private sealed class CameraAcquisitionEvidenceReport
        {
            public string status = string.Empty;
            public bool acceptance_enabled;
            public string evidence_fault = string.Empty;
            public string permission = string.Empty;
            public string current_state = string.Empty;
            public string message = string.Empty;
            public bool desired_active;
            public string preferred_facing = string.Empty;
            public string current_session_id = string.Empty;
            public string camera_id = string.Empty;
            public string requested_facing = string.Empty;
            public string accepted_frame_count = string.Empty;
            public string stale_frame_count = string.Empty;
            public string revision = string.Empty;
            public bool has_frame;
            public CameraFrameEvidence? frame;
            public string last_command_id = string.Empty;
            public string last_command_action = string.Empty;
            public string last_command_status = string.Empty;
            public string last_command_message = string.Empty;
            public int command_count;
            public int start_command_count;
            public int stop_command_count;
            public int session_count;
            public int front_session_count;
            public int rear_session_count;
            public int state_transition_count;
            public int running_transition_count;
            public int paused_transition_count;
            public int resumed_transition_count;
            public int stopped_transition_count;
            public int permission_revoked_transition_count;
            public int unavailable_transition_count;
            public int faulted_transition_count;
            public int application_pause_count;
            public int application_resume_count;
            public int frame_observation_count;
            public bool metadata_monotonic;
            public bool all_frames_yuv420888;
            public bool all_frames_valid_crop;
            public bool all_frames_valid_intrinsics;
            public bool all_frames_positive_timestamp;
            public bool front_frame_seen;
            public bool rear_frame_seen;
            public bool rotation_0_seen;
            public bool rotation_90_seen;
            public bool rotation_180_seen;
            public bool rotation_270_seen;
        }

        [Serializable]
        private sealed class CameraFrameEvidence
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
            public int crop_left;
            public int crop_top;
            public int crop_right;
            public int crop_bottom;
            public string pixel_format = string.Empty;
            public string intrinsics_source = string.Empty;
            public float focal_length_x;
            public float focal_length_y;
            public float principal_point_x;
            public float principal_point_y;
            public float skew;
            public string intrinsics_provenance = string.Empty;
        }
    }
}
