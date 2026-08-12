#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyRma154VisualServoAcceptance
    {
        [Serializable]
        private sealed class Report
        {
            public string status = string.Empty;
            public bool acceptance_enabled;
            public bool centered;
            public bool actual_motion_observed;
            public bool post_motion_frame_observed;
            public bool requested_target_used_as_motion_proof;
            public bool raw_joint_command_used;
            public bool torque_command_used;
            public ulong initial_authoritative_sequence;
            public ulong final_authoritative_sequence;
            public double initial_center_x;
            public double initial_center_y;
            public double final_horizontal_error;
            public double final_vertical_error;
            public int adjustment_count;
            public int submitted_frame_count;
            public double maximum_authoritative_motion_radians;
            public int transformed_frame_count;
            public string message = string.Empty;

            internal static Report Success(
                ulong initialAuthoritativeSequence,
                ulong finalAuthoritativeSequence,
                double initialCenterX,
                double initialCenterY,
                double finalHorizontalError,
                double finalVerticalError,
                int adjustmentCount,
                int submittedFrameCount,
                double maximumAuthoritativeMotionRadians,
                int transformedFrameCount,
                bool actualMotionObserved,
                bool postMotionFrameObserved)
            {
                return new Report
                {
                    status = "passed",
                    acceptance_enabled = true,
                    centered = true,
                    actual_motion_observed = actualMotionObserved,
                    post_motion_frame_observed = postMotionFrameObserved,
                    requested_target_used_as_motion_proof = false,
                    raw_joint_command_used = false,
                    torque_command_used = false,
                    initial_authoritative_sequence =
                        initialAuthoritativeSequence,
                    final_authoritative_sequence = finalAuthoritativeSequence,
                    initial_center_x = initialCenterX,
                    initial_center_y = initialCenterY,
                    final_horizontal_error = finalHorizontalError,
                    final_vertical_error = finalVerticalError,
                    adjustment_count = adjustmentCount,
                    submitted_frame_count = submittedFrameCount,
                    maximum_authoritative_motion_radians =
                        maximumAuthoritativeMotionRadians,
                    transformed_frame_count = transformedFrameCount,
                    message =
                        "RMA-154 real MuJoCo motion recentered a deterministic optical target through post-motion transformed feedback.",
                };
            }

            internal static Report Failure(string message)
            {
                return new Report
                {
                    status = "failed",
                    acceptance_enabled = true,
                    message = string.IsNullOrWhiteSpace(message)
                        ? "RMA-154 acceptance failed without diagnostics."
                        : message,
                };
            }
        }
    }
}
