import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOR = ROOT / "Assets/ReachyMini/Runtime/Core/Behavior"
RENDERING = ROOT / "Assets/ReachyMini/Runtime/Rendering"
MANAGED = ROOT / "managed/ReachyMini.Core.Tests"
APPLICATION = ROOT / "Assets/ReachyMini/Runtime/Application"
POLICY = ROOT / "models/reachy-mini/visual-servo-gaze-policy-v1.json"
WORKFLOW = ROOT / ".github/workflows/local-unity-android-validation.yml"
DEVICE_SCRIPT = ROOT / "scripts/run_rma154_visual_servo_acceptance_android.sh"


class Rma154VisualServoGazeContracts(unittest.TestCase):
    def test_source_set_is_complete(self) -> None:
        required = {
            BEHAVIOR / "ReachyVisualServoPolicy.cs": "class ReachyVisualServoPolicy",
            BEHAVIOR / "ReachyVisualServoFeedback.cs": (
                "interface IReachyVisualServoFeedbackSource"
            ),
            BEHAVIOR / "ReachyVisualServoResult.cs": "enum ReachyVisualServoStatus",
            BEHAVIOR / "ReachyVisualServoGazeLoop.cs": "class ReachyVisualServoGazeLoop",
            RENDERING / "ReachyProductionVisualServoFeedbackSource.cs": (
                "class ReachyProductionVisualServoFeedbackSource"
            ),
            APPLICATION / "ReachyRma154VisualServoAcceptance.cs": (
                "partial class ReachyRma154VisualServoAcceptance"
            ),
            APPLICATION / "ReachyRma154VisualServoAcceptance.Feedback.cs": (
                "class SyntheticOpticalTargetFeedbackSource"
            ),
            APPLICATION / "ReachyRma154VisualServoAcceptance.Report.cs": (
                "private sealed class Report"
            ),
            MANAGED / "Rma154VisualServoGazeLoopContractTests.cs": (
                "EdgeTargetRecentersOnlyAfterAuthoritativeMotionAndNewFrame"
            ),
            MANAGED / "Rma154VisualServoGazeLoopContractTests.Feedback.cs": (
                "RequestedTargetsDoNotCountAsMotionFeedback"
            ),
            MANAGED / "Rma154VisualServoGazeLoopContractTests.Fixtures.cs": (
                "ScriptedFeedbackSource"
            ),
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_machine_policy_matches_runtime_defaults(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        self.assertEqual("rma154_visual_servo_gaze_v1", policy["contract_id"])
        self.assertEqual("engineering_estimate", policy["quality"])
        feedback = policy["authoritative_feedback"]
        self.assertFalse(feedback["requested_target_is_motion_evidence"])
        self.assertTrue(
            feedback["requires_frame_authoritative_sequence_at_or_after_observed_motion"]
        )

        runtime = (BEHAVIOR / "ReachyVisualServoPolicy.cs").read_text(encoding="utf-8")
        named_values = {
            "horizontalToleranceNormalized": policy["tracking"]["horizontal_tolerance_normalized"],
            "verticalToleranceNormalized": policy["tracking"]["vertical_tolerance_normalized"],
            "minimumValidCoverageFraction": policy["tracking"]["minimum_valid_coverage_fraction"],
            "minimumObservedMotionRadians": feedback["minimum_observed_motion_radians"],
            "feedbackPollDelayMilliseconds": policy["loop"]["feedback_poll_delay_milliseconds"],
            "maximumIterations": policy["loop"]["maximum_iterations"],
            "maximumLoopDurationMilliseconds": policy["loop"]["maximum_loop_duration_milliseconds"],
        }
        for name, expected in named_values.items():
            match = re.search(rf"{name}:\s*([0-9_.]+)(?:e-?[0-9]+)?", runtime)
            self.assertIsNotNone(match, name)
            assert match is not None
            token = match.group(0).split(":", 1)[1].strip().rstrip(",")
            actual = float(token.replace("_", ""))
            self.assertAlmostEqual(float(expected), actual, places=12, msg=name)

    def test_loop_requires_actual_motion_then_post_motion_transformed_frame(self) -> None:
        source = (BEHAVIOR / "ReachyVisualServoGazeLoop.cs").read_text(encoding="utf-8")
        for required in (
            "HasObservedPhysicalMotion(beforeMotion, next.MotionSnapshot)",
            "observedMotionSequence = next.AuthoritativeStateSequence",
            "IsNewerTransformedFrame(beforeFrame, nextFrame)",
            "nextFrame.AuthoritativeSequence >= observedMotionSequence",
            "HasFrameRegressed(lastFrame, nextFrame)",
            "entity.Bounds.CenterX - 0.5",
            "entity.Bounds.CenterY - 0.5",
            "CreateGazeCorrectionIntent(entityId)",
            "expression: null",
            "gesture: null",
            "executor.ExecuteAsync(plan, loopToken)",
        ):
            self.assertIn(required, source)

        self.assertNotIn("plan.Frames[plan.Frames.Count - 1]", source)
        self.assertNotIn("TargetPositionsRadians", source)
        self.assertNotIn("ReachyBaselineBehaviorRequest.GazeAcquisition", source)

    def test_stop_conditions_are_explicit_and_fail_closed(self) -> None:
        source = (BEHAVIOR / "ReachyVisualServoGazeLoop.cs").read_text(encoding="utf-8")
        for required in (
            "ReachyVisualServoStatus.Centered",
            "ReachyVisualServoStatus.TargetLost",
            "ReachyVisualServoStatus.CoverageBlocked",
            "ReachyVisualServoStatus.LoadLimit",
            "ReachyVisualServoStatus.SafetyInterlock",
            "ReachyVisualServoStatus.TimedOut",
            "ReachyVisualServoStatus.Cancelled",
            "ReachyVisualServoStatus.FrameDiscontinuity",
            "ReachyVisualServoStatus.FeedbackUnavailable",
            "LoadLimitActive",
            "ShouldStopVisionDrivenTurning",
            "MaximumIterations",
            "MaximumLoopDurationMilliseconds",
        ):
            self.assertIn(required, source)

    def test_production_feedback_is_read_only_and_authoritative(self) -> None:
        source = (RENDERING / "ReachyProductionVisualServoFeedbackSource.cs").read_text(
            encoding="utf-8"
        )
        runtime = (RENDERING / "ReachyProductionAuthoritativeRuntime.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "TryCaptureLatestAuthoritativeState",
            "ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot",
            "ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot",
            "worldModel.GetSnapshot",
        ):
            self.assertIn(required, source)
        self.assertIn("TryCreateAuthoritativeStateFrame", runtime)
        self.assertIn("TryCaptureLatestAuthoritativeState", runtime)
        combined = (
            source + "\n" + (BEHAVIOR / "ReachyVisualServoGazeLoop.cs").read_text(encoding="utf-8")
        )
        for forbidden in (
            "NativeReachySim",
            "ReachySimSession",
            "SubmitCommandsRaw",
            "ReachySimulationCommandBatch",
            "SetQpos",
            "SubmitTorque",
            "TorqueCommand",
        ):
            self.assertNotIn(forbidden, combined)

    def test_physical_android_acceptance_closes_real_feedback_path(self) -> None:
        acceptance = "\n".join(
            (APPLICATION / filename).read_text(encoding="utf-8")
            for filename in (
                "ReachyRma154VisualServoAcceptance.cs",
                "ReachyRma154VisualServoAcceptance.Feedback.cs",
                "ReachyRma154VisualServoAcceptance.Report.cs",
            )
        )
        for required in (
            "ReachyProductionVisualServoFeedbackSource",
            "WaitForResetStateAsync",
            "frame.ContinuityId != previousContinuityId",
            "if (calibration == null)",
            "CreateBaselineCalibration(transformState)",
            "binding.NeutralMujocoWorldFromOptical.Transposed()",
            "QuaternionFromRotationMatrix",
            "ReachyProductionBehaviorControllerTargetSink",
            "ReachyCameraRelativeRotationCalculator.Calculate",
            "ReachyCameraHomographyCalculator.Build",
            "ReachyCameraValidCoverageCalculator.Calculate",
            "ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot",
            "runtime.TryCaptureLatestAuthoritativeState",
            "ObservedPhysicalMotion",
            "ObservedPostMotionTransformedFrame",
            "result.Centered",
            "maximumPhysicalMotion",
        ):
            self.assertIn(required, acceptance)
        self.assertNotIn(
            "neutralReachyFromPhoneRotation: ReachyQuaternionD.Identity",
            acceptance,
        )

        for forbidden in (
            "NativeReachySim",
            "ReachySimSession",
            "SubmitCommandsRaw",
            "SetQpos",
            "SubmitTorque",
            "TorqueCommand",
        ):
            self.assertNotIn(forbidden, acceptance)

        script = DEVICE_SCRIPT.read_text(encoding="utf-8")
        for required in (
            "--ez reachy_rma154_acceptance true",
            '"centered",',
            '"actual_motion_observed",',
            '"post_motion_frame_observed",',
            '"requested_target_used_as_motion_proof",',
            '"raw_joint_command_used",',
            '"torque_command_used",',
            'report.get("maximum_authoritative_motion_radians"',
        ):
            self.assertIn(required, script)

        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("run_rma154_visual_servo_acceptance_android.sh", workflow)
        self.assertIn("rma154-visual-servo-report-${{ github.sha }}", workflow)

    def test_replay_contract_is_covered(self) -> None:
        test_source = (MANAGED / "Rma154VisualServoGazeLoopContractTests.Feedback.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "FeedbackRegressionFailsClosed",
            "ObservationReplayProducesRepeatableTrajectories",
            "firstSink.Submitted.Count",
            "secondSink.Submitted.Count",
            "replay trajectory actuator",
            "does not accumulate left expressive antenna offset",
            "does not accumulate right expressive antenna offset",
        ):
            self.assertIn(required, test_source)


if __name__ == "__main__":
    unittest.main()
