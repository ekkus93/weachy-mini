import json
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
        self.assertEqual("rma152_deterministic_behavior_planner_v1", policy["contract_id"])
        self.assertEqual("engineering_estimate", policy["quality"])
        self.assertEqual(9, len(policy["actuator_order"]))
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

    def test_gaze_resolution_is_fail_closed(self) -> None:
        source = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs"
        ).read_text(encoding="utf-8")
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

    def test_motion_is_relative_to_authoritative_state(self) -> None:
        planner = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs"
        ).read_text(
            encoding="utf-8"
        )
        gaze = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("CopyTarget(\n                motionSnapshot.PositionsRadians)", planner)
        self.assertIn(
            "target[ReachyBehaviorPlannerActuators.BodyYaw] += bodyYaw", gaze
        )

    def test_motion_limits_and_interlocks_are_explicit(self) -> None:
        contracts = "\n".join(
            (BEHAVIOR / filename).read_text(encoding="utf-8")
            for filename in (
                "ReachyBehaviorPlannerPolicy.cs",
                "ReachyBehaviorPlannerState.cs",
                "ReachyBehaviorTrajectoryContracts.cs",
            )
        )
        planner = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs"
        ).read_text(
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
        ):
            self.assertIn(required, authoritative)
        self.assertIn("ValidateMotionSnapshot", planner)
        self.assertIn("ValidateTarget", trajectory)
        self.assertIn("MinimumSafeSegmentMilliseconds", trajectory)

    def test_timing_cannot_relax_safety_limits(self) -> None:
        source = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.TrajectorySafety.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("behavior-duration-cannot-meet-velocity-acceleration-limits", source)
        self.assertNotIn("Math.Min(segmentMilliseconds", source)
        fixture = (
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("TooShortTimingCannotOverrideMotionLimits", fixture)

    def test_cancellation_requires_fresh_explicit_safe_rest(self) -> None:
        planner = (
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Planning.cs"
        ).read_text(
            encoding="utf-8"
        )
        fixture = (
            MANAGED / "Rma152DeterministicBehaviorPlannerContractTests.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("cancelled-safe-rest-replan-required", planner)
        self.assertIn("PlanSafeRest", planner)
        self.assertIn("CancellationRequiresExplicitFreshSafeRestPlan", fixture)
        self.assertIn("SafeRestReturnsAllActuatorsToNeutral", fixture)

    def test_execution_stops_on_cancellation_or_submission_failure(self) -> None:
        source = (BEHAVIOR / "ReachyBehaviorTrajectoryExecutor.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("cancellationToken.IsCancellationRequested", source)
        self.assertIn("OperationCanceledException", source)
        self.assertIn("SubmissionRejected", source)
        self.assertIn("targetSink.Submit(frame)", source)
        self.assertNotIn("while (", source)
        self.assertNotIn("retry", source.casefold())
        self.assertNotIn("PlanSafeRest", source)

    def test_production_sink_uses_only_normal_controller_path(self) -> None:
        source = (
            RENDERING / "ReachyProductionBehaviorControllerTargetSink.cs"
        ).read_text(encoding="utf-8")
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
