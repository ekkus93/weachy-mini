#nullable enable

using System;
using System.IO;
using ReachyMini.Perception;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyRma190CameraEndToEndAcceptance
    {
        [Serializable]
        private sealed class Report
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string stage = string.Empty;
            public string device_model = string.Empty;
            public int android_api_level;
            public bool real_front_camera;
            public bool real_authoritative_head_motion;
            public bool deterministic_calibration_fixture;
            public bool measured_calibration_claimed;
            public string calibration_provenance = string.Empty;
            public string camera_id = string.Empty;
            public ulong camera_session_id;
            public int output_width;
            public int output_height;
            public string baseline_origin = string.Empty;
            public ulong baseline_source_sequence;
            public ulong baseline_authoritative_sequence;
            public double baseline_coverage_fraction;
            public string baseline_coverage_state = string.Empty;
            public string rotated_origin = string.Empty;
            public ulong rotated_source_sequence;
            public ulong rotated_authoritative_sequence;
            public double rotated_coverage_fraction;
            public string rotated_coverage_state = string.Empty;
            public uint continuity_id;
            public double authoritative_head_rotation_radians;
            public int gesture_plan_frame_count;
            public int gesture_submitted_frame_count;
            public bool synthetic_camera_frame_injected;

            public static Report Success(
                ReachyCameraTextureFrameDescriptor descriptor,
                FrameEvidence baseline,
                FrameEvidence rotated,
                double headRotationRadians,
                int planFrameCount,
                int submittedFrameCount,
                ReachyCameraCalibrationProfile fixture)
            {
                return new Report
                {
                    status = "passed",
                    stage = "completed",
                    device_model = SystemInfo.deviceModel,
                    android_api_level = ReadApiLevel(),
                    real_front_camera =
                        descriptor.LensFacing == ReachyDeviceCameraFacing.Front,
                    real_authoritative_head_motion =
                        headRotationRadians >= MinimumHeadRotationRadians,
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance = fixture.Provenance.ToString(),
                    camera_id = descriptor.CameraId,
                    camera_session_id = descriptor.SessionId,
                    output_width = descriptor.OutputWidth,
                    output_height = descriptor.OutputHeight,
                    baseline_origin = VisionFrameOrigin.TransformedReachyEye.ToString(),
                    baseline_source_sequence = baseline.SourceSequence,
                    baseline_authoritative_sequence = baseline.AuthoritativeSequence,
                    baseline_coverage_fraction = baseline.CoverageFraction,
                    baseline_coverage_state = baseline.CoverageState,
                    rotated_origin = VisionFrameOrigin.TransformedReachyEye.ToString(),
                    rotated_source_sequence = rotated.SourceSequence,
                    rotated_authoritative_sequence = rotated.AuthoritativeSequence,
                    rotated_coverage_fraction = rotated.CoverageFraction,
                    rotated_coverage_state = rotated.CoverageState,
                    continuity_id = baseline.ContinuityId,
                    authoritative_head_rotation_radians = headRotationRadians,
                    gesture_plan_frame_count = planFrameCount,
                    gesture_submitted_frame_count = submittedFrameCount,
                    synthetic_camera_frame_injected = false,
                };
            }

            public static Report Failure(
                string error,
                string stage = "")
            {
                return new Report
                {
                    status = "failed",
                    error = Bound(error, 4096),
                    stage = Bound(stage, 256),
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                    deterministic_calibration_fixture = true,
                    measured_calibration_claimed = false,
                    calibration_provenance =
                        ReachyCameraCalibrationProvenance.UncalibratedEstimate.ToString(),
                    synthetic_camera_frame_injected = false,
                };
            }
        }

        private static int ReadApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
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

        private static void WriteAtomically(string path, string content)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "RMA-190 camera result path has no parent directory.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string bounded = string.IsNullOrWhiteSpace(value)
                ? "RMA-190 camera acceptance failed without diagnostics."
                : value;
            return bounded.Length <= maximumCharacters
                ? bounded
                : bounded.Substring(0, maximumCharacters);
        }
    }
}
