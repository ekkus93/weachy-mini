#nullable enable

using System;
using ReachyMini.Interop;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma190CoreEndToEndAcceptance
    {
        [Serializable]
        private sealed class AcceptanceReport
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public string application_state = string.Empty;
            public bool network_offline;
            public bool active_network_present;
            public bool active_network_internet_capability;
            public bool active_network_validated;
            public string simulation_service = string.Empty;
            public string behavior_service = string.Empty;
            public string runtime_status = string.Empty;
            public string model_hash = string.Empty;
            public int body_count;
            public int qpos_count;
            public int qvel_count;
            public int actuator_observation_count;
            public bool neutral_reset_succeeded;
            public bool neutral_reset_continuity_advanced;
            public string gesture_kind = string.Empty;
            public string gesture_plan_sha256 = string.Empty;
            public int gesture_plan_frame_count;
            public int gesture_first_submitted_frame_count;
            public int gesture_second_submitted_frame_count;
            public bool gesture_actual_motion_observed;
            public double gesture_first_maximum_motion_radians;
            public double gesture_second_maximum_motion_radians;
            public double gesture_replay_maximum_qpos_error;
            public double gesture_replay_maximum_qvel_error;
            public double gesture_replay_tolerance;
            public bool gesture_replay_within_tolerance;
            public bool behavior_suspended_for_direct_replay;
            public bool human_asr_transcript_injected;
            public bool cloud_credentials_injected;
            public bool local_model_injected;
        }

        private readonly struct NetworkSnapshot
        {
            public NetworkSnapshot(
                bool activeNetworkPresent,
                bool internetCapability,
                bool validated)
            {
                ActiveNetworkPresent = activeNetworkPresent;
                InternetCapability = internetCapability;
                Validated = validated;
            }

            public bool ActiveNetworkPresent { get; }
            public bool InternetCapability { get; }
            public bool Validated { get; }
            public bool Offline =>
                !ActiveNetworkPresent || !InternetCapability || !Validated;
        }

        private readonly struct GesturePass
        {
            public GesturePass(
                ReachySimAuthoritativeStateFrame finalState,
                int submittedFrameCount,
                double maximumMotionRadians)
            {
                FinalState = finalState;
                SubmittedFrameCount = submittedFrameCount;
                MaximumMotionRadians = maximumMotionRadians;
            }

            public ReachySimAuthoritativeStateFrame FinalState { get; }
            public int SubmittedFrameCount { get; }
            public double MaximumMotionRadians { get; }
        }

    }
}
