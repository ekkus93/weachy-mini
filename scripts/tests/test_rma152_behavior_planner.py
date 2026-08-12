import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOR = ROOT / "Assets/ReachyMini/Runtime/Core/Behavior"
RENDERING = ROOT / "Assets/ReachyMini/Runtime/Rendering"
MANAGED = ROOT / "managed/ReachyMini.Core.Tests"
POLICY = ROOT / "models/reachy-mini/behavior-planner-policy.json"
RMA065 = ROOT / "models/reachy-mini/collision-hard-stop-baseline.json"
AUDIT = ROOT / "models/reachy-mini/model-parameter-audit.json"


class Rma152BehaviorPlannerContracts(unittest.TestCase):
    def test_planner_source_set_is_complete(self) -> None:
        required = {
            BEHAVIOR / "ReachyBehaviorPlannerPolicy.cs": "class ReachyBehaviorPlannerPolicy",
            BEHAVIOR / "ReachyBehaviorPlannerState.cs": "class ReachyBehaviorMotionSnapshot",
            BEHAVIOR / "ReachyBehaviorTrajectoryContracts.cs": "class ReachyBehaviorTrajectoryPlan",
            BEHAVIOR / "ReachyBehaviorTrajectoryExecutor.cs": (
                "class ReachyBehaviorTrajectoryExecutor"
            ),
            BEHAVIOR / "ReachyBehaviorAuthoritativeSafety.cs": (
                "class ReachyBehaviorAuthoritativeSafety"
            ),
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.cs": (
                "class ReachyDeterministicBehaviorPlanner"
            ),
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs": (
                "public ReachyBehaviorPlanResult Plan"
            ),
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs": (
                "ResolveAndApplyGaze"
            ),
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.TrajectorySafety.cs": (
                "MinimumSafeSegmentMilliseconds"
            ),
            RENDERING / "ReachyProductionBehaviorControllerTargetSink.cs": (
                "class ReachyProductionBehaviorControllerTargetSink"
            ),
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.cs": (
                "CurrentHighConfidenceGazeTargetIsResolved"
            ),
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Planning.cs": (
                "GestureTrajectoryIsDeterministicAndBounded"
            ),
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Slew.cs": (
                "TrajectoryFramesSlewInsteadOfDelayedTargetStep"
            ),
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Safety.cs": (
                "CancellationRequiresExplicitFreshSafeRestPlan"
            ),
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Fixtures.cs": (
                "AssertPlanWithinPolicy"
            ),
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_policy_is_bound_to_rma065_soft_ranges(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        rma065 = json.loads(RMA065.read_text(encoding="utf-8"))
        audit = json.loads(AUDIT.read_text(encoding="utf-8"))
        self.assertEqual("rma152_deterministic_behavior_planner_v2", policy["contract_id"])
        self.assertEqual("engineering_estimate", policy["quality"])
        self.assertEqual(9, len(policy["actuator_order"]))
        planning = policy["planning"]
        self.assertLessEqual(
            planning["maximum_plan_duration_milliseconds"],
            planning["maximum_trajectory_frame_count"] * planning["command_interval_milliseconds"],
        )
        limits = {item["name"]: item for item in policy["actuator_limits"]}
        joints = {item["name"]: item for item in audit["joints"]}
        hard_stops = {item["joint"]: item for item in rma065["hard_stops"]}
        for name in policy["actuator_order"][:7]:
            source_range = joints[name]["range_radians"]
            inset = hard_stops[name]["soft_limit_inset_radians"]
            self.assertAlmostEqual(
                source_range[0] + inset,
                limits[name]["minimum_position_radians"],
                places=12,
            )
            self.assertAlmostEqual(
                source_range[1] - inset,
                limits[name]["maximum_position_radians"],
                places=12,
            )
        for name in ("right_antenna", "left_antenna"):
            self.assertEqual(
                hard_stops[name]["soft_range_radians"][0],
                limits[name]["minimum_position_radians"],
            )
            self.assertEqual(
                hard_stops[name]["soft_range_radians"][1],
                limits[name]["maximum_position_radians"],
            )

    def test_runtime_policy_matches_machine_readable_policy(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        source = (BEHAVIOR / "ReachyBehaviorPlannerPolicy.cs").read_text(encoding="utf-8")

        named_values = {
            "minimumGazeConfidence": policy["world_model"]["minimum_gaze_confidence"],
            "minimumValidCoverageFraction": policy["world_model"][
                "minimum_valid_coverage_fraction"
            ],
            "maximumWorldSnapshotAgeNanoseconds": policy["world_model"][
                "maximum_snapshot_age_nanoseconds"
            ],
            "minimumSegmentMilliseconds": policy["planning"]["minimum_segment_milliseconds"],
            "commandIntervalMilliseconds": policy["planning"]["command_interval_milliseconds"],
            "maximumTrajectoryFrameCount": policy["planning"]["maximum_trajectory_frame_count"],
            "maximumPlanDurationMilliseconds": policy["planning"][
                "maximum_plan_duration_milliseconds"
            ],
            "maximumGazeBodyYawRadians": policy["planning"]["maximum_gaze_body_yaw_radians"],
            "maximumGazeHeadYawRadians": policy["planning"]["maximum_gaze_head_yaw_radians"],
            "maximumGazeHeadPitchRadians": policy["planning"]["maximum_gaze_head_pitch_radians"],
        }
        for name, expected in named_values.items():
            match = re.search(rf"{name}:\s*([0-9_.]+)L?", source)
            if match is None:
                self.fail(f"runtime planner policy value missing: {name}")
            raw = match.group(1).replace("_", "")
            actual = float(raw)
            self.assertAlmostEqual(float(expected), actual, places=12, msg=name)

        blocks = re.findall(
            r"new ReachyBehaviorActuatorLimit\((.*?)\)",
            source,
            flags=re.DOTALL,
        )
        self.assertEqual(9, len(blocks))
        runtime_limits = []
        for block in blocks:
            values = [item.strip() for item in block.split(",")]
            self.assertEqual(4, len(values))
            evaluated = []
            for value in values:
                if value.endswith("+ inset"):
                    evaluated.append(float(value[: -len("+ inset")].strip()) + 0.015)
                elif value.endswith("- inset"):
                    evaluated.append(float(value[: -len("- inset")].strip()) - 0.015)
                else:
                    evaluated.append(float(value))
            runtime_limits.append(evaluated)

        for machine, runtime in zip(policy["actuator_limits"], runtime_limits, strict=True):
            self.assertAlmostEqual(machine["minimum_position_radians"], runtime[0], places=12)
            self.assertAlmostEqual(machine["maximum_position_radians"], runtime[1], places=12)
            self.assertAlmostEqual(
                machine["maximum_velocity_radians_per_second"], runtime[2], places=12
            )
            self.assertAlmostEqual(
                machine["maximum_acceleration_radians_per_second_squared"],
                runtime[3],
                places=12,
            )

    def test_gaze_resolution_is_fail_closed(self) -> None:
        source = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "WorldSnapshotStale",
            "GazeTargetNotFound",
            "GazeTargetNotVisible",
            "GazeTargetLowConfidence",
            "GazeCoverageBlocked",
            "IsCurrentlyVisible",
            "MinimumGazeConfidence",
            "MinimumValidCoverageFraction",
            "ShouldStopVisionDrivenTurning",
        ):
            self.assertIn(required, source)

    def test_gaze_horizontal_sign_matches_pinned_optical_and_body_yaw_axes(self) -> None:
        gaze = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs").read_text(
            encoding="utf-8"
        )
        search = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.BaselineGaze.cs").read_text(
            encoding="utf-8"
        )
        managed = (
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Planning.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("double horizontal = -Math.Atan2(direction.X, direction.Z);", gaze)
        self.assertIn("double horizontal = -Math.Atan2(direction.X, direction.Z);", search)
        self.assertIn("right-side entity produces negative physical body yaw", managed)
        self.assertIn("right-side gaze applies negative physical body-yaw delta", managed)
        self.assertNotIn("gazeYaw > positions[ReachyBehaviorPlannerActuators.BodyYaw]", managed)

    def test_motion_is_relative_to_authoritative_state(self) -> None:
        planner = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs").read_text(
            encoding="utf-8"
        )
        gaze = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("CopyTarget(\n                motionSnapshot.PositionsRadians)", planner)
        self.assertIn("target[ReachyBehaviorPlannerActuators.BodyYaw] += bodyYaw", gaze)

    def test_motion_limits_and_interlocks_are_explicit(self) -> None:
        contracts = "\n".join(
            (BEHAVIOR / filename).read_text(encoding="utf-8")
            for filename in (
                "ReachyBehaviorPlannerPolicy.cs",
                "ReachyBehaviorPlannerState.cs",
                "ReachyBehaviorTrajectoryContracts.cs",
            )
        )
        planner = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs").read_text(
            encoding="utf-8"
        )
        trajectory = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.TrajectorySafety.cs"
        ).read_text(encoding="utf-8")
        for required in (
            "public const int Count = 9",
            "WorkspaceClear",
            "ActiveFault",
            "ActiveCollision",
            "ActiveHardStop",
            "LoadLimitActive",
            "MaximumVelocityRadiansPerSecond",
            "MaximumAccelerationRadiansPerSecondSquared",
        ):
            self.assertIn(required, contracts)
        self.assertIn("SafetyInterlockActive", planner)
        authoritative = (BEHAVIOR / "ReachyBehaviorAuthoritativeSafety.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "ContactCount",
            "ContactOverloadHealthFlag",
            "HardStopHealthFlag",
            "CreateMotionSnapshot",
            "CreateSafetySnapshot",
            "~KnownHealthFlags",
        ):
            self.assertIn(required, authoritative)
        self.assertIn("ValidateMotionSnapshot", planner)
        self.assertIn("ValidateTarget", trajectory)
        self.assertIn("MinimumSafeSegmentMilliseconds", trajectory)
        self.assertIn("AppendSmoothStepFrames", trajectory)
        self.assertIn("policy.CommandIntervalMilliseconds", trajectory)
        self.assertIn("policy.MaximumTrajectoryFrameCount", trajectory)

    def test_timing_cannot_relax_safety_limits(self) -> None:
        source = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.TrajectorySafety.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("behavior-duration-cannot-meet-velocity-acceleration-limits", source)
        self.assertIn("1.5 * distance / velocity", source)
        self.assertIn("Math.Sqrt(6.0 * distance / acceleration)", source)
        self.assertIn("progress * progress * (3.0 - (2.0 * progress))", source)
        self.assertNotIn("Math.Min(segmentMilliseconds", source)
        fixture = (MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("TooShortTimingCannotOverrideMotionLimits", fixture)

    def test_trajectory_is_scheduled_as_bounded_setpoint_slew(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        planning = policy["planning"]
        self.assertEqual(50, planning["command_interval_milliseconds"])
        self.assertEqual(128, planning["maximum_trajectory_frame_count"])
        self.assertEqual(
            "cubic_smoothstep_zero_endpoint_velocity",
            planning["trajectory_profile"],
        )
        trajectory = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.TrajectorySafety.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("AppendSmoothStepFrames", trajectory)
        self.assertIn("frames.Count + stepCount", trajectory)
        self.assertIn("behavior-trajectory-frame-budget-exceeded", trajectory)
        fixture = (MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.Slew.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("TrajectoryFramesSlewInsteadOfDelayedTargetStep", fixture)
        self.assertIn("setpoint slew uses intermediate frames", fixture)
        self.assertIn("RecoilPreservesUnrelatedBodyYaw", fixture)
        self.assertIn("SafeRestCoversFullSoftEnvelope", fixture)

    def test_cancellation_requires_fresh_explicit_safe_rest(self) -> None:
        planner = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs").read_text(
            encoding="utf-8"
        )
        fixture = (MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("cancelled-safe-rest-replan-required", planner)
        self.assertIn("PlanSafeRest", planner)
        self.assertIn("CancellationRequiresExplicitFreshSafeRestPlan", fixture)
        self.assertIn("SafeRestReturnsAllActuatorsToNeutral", fixture)

    def test_execution_stops_on_cancellation_or_submission_failure(self) -> None:
        source = (BEHAVIOR / "ReachyBehaviorTrajectoryExecutor.cs").read_text(encoding="utf-8")
        self.assertIn("cancellationToken.IsCancellationRequested", source)
        self.assertIn("OperationCanceledException", source)
        self.assertIn("SubmissionRejected", source)
        self.assertIn("targetSink.Submit(frame)", source)
        self.assertNotIn("while (", source)
        self.assertNotIn("retry", source.casefold())
        self.assertNotIn("PlanSafeRest", source)

    def test_production_sink_uses_only_normal_controller_path(self) -> None:
        source = (RENDERING / "ReachyProductionBehaviorControllerTargetSink.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("runtime.SubmitPositionTargets(targets)", source)
        for forbidden in (
            "NativeReachySim",
            "ReachySimSession",
            "SubmitCommandsRaw",
            "ReachySimulationCommandBatch.CreatePositionTargets",
            "torque",
        ):
            self.assertNotIn(forbidden, source)

    def test_planner_has_no_nondeterministic_clock_or_randomness(self) -> None:
        sources = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(BEHAVIOR.glob("ReachyDeterministicBehaviorPlanner*.cs"))
        )
        for forbidden in (
            "DateTime",
            "Environment.TickCount",
            "Random",
            "Guid.NewGuid",
            "Thread.Sleep",
            "Task.Run",
        ):
            self.assertNotIn(forbidden, sources)


if __name__ == "__main__":
    unittest.main()
