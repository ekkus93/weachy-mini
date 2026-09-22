#nullable enable

using System;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma190CameraEndToEndAcceptance
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
            public string camera_id = string.Empty;
            public string camera_facing = string.Empty;
            public int source_width;
            public int source_height;
            public int output_width;
            public int output_height;
            public bool front_camera_mirrored;
            public bool deterministic_uncalibrated_fixture_used;
            public string calibration_profile_id = string.Empty;
            public string baseline_frame_origin = string.Empty;
            public string post_motion_frame_origin = string.Empty;
            public bool baseline_frame_observation_eligible;
            public bool post_motion_frame_observation_eligible;
            public bool baseline_validity_mask;
            public bool post_motion_validity_mask;
            public double baseline_coverage_fraction;
            public double post_motion_coverage_fraction;
            public ulong baseline_source_sequence;
            public ulong post_motion_source_sequence;
            public ulong baseline_authoritative_sequence;
            public ulong post_motion_authoritative_sequence;
            public uint continuity_id;
            public double camera_body_rotation_radians;
            public bool authoritative_head_rotation_observed;
            public bool transformed_frame_after_rotation_observed;
            public bool raw_camera_frame_injected;
            public bool synthetic_camera_frame_injected;
        }
    }
}
