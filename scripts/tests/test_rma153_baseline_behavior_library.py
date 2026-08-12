import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOR = ROOT / "Assets/ReachyMini/Runtime/Core/Behavior"
MANAGED = ROOT / "managed/ReachyMini.Core.Tests"
POLICY = ROOT / "models/reachy-mini/baseline-behavior-library-v1.json"


class Rma153BaselineBehaviorLibraryContracts(unittest.TestCase):
    def test_source_set_is_complete(self) -> None:
        required = {
            BEHAVIOR / "ReachyBaselineBehaviorContracts.cs": "class ReachyBaselineBehaviorRequest",
            BEHAVIOR / "ReachyBaselineBehaviorPolicy.cs": "class ReachyBaselineBehaviorPolicy",
            BEHAVIOR / "ReachyBaselineBehaviorLibrary.cs": "class ReachyBaselineBehaviorLibrary",
            BEHAVIOR / "ReachyBaselineLifecycleResetMapping.cs": (
                "class ReachyBaselineLifecycleResetMapping"
            ),
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Baseline.cs": "PlanBaseline",
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.BaselinePoses.cs": "CreateIdlePoses",
            BEHAVIOR / "ReachyDeterministicBehaviorPlanner.BaselineGaze.cs": (
                "ResolveLostTargetSearch"
            ),
            MANAGED / "Rma153BaselineBehaviorLibraryContractTests.cs": (
                "CatalogRequestsAreExplicitAndBounded"
            ),
            MANAGED / "Rma153BaselineBehaviorLibraryContractTests.Planning.cs": (
                "SpeakingEnergyControlsMotionWithoutAccumulation"
            ),
            MANAGED / "Rma153BaselineBehaviorLibraryContractTests.GazeLifecycle.cs": (
                "SleepAndWakeExposeExplicitLifecycleSequence"
            ),
            MANAGED / "Rma153BaselineBehaviorLibraryContractTests.Fixtures.cs": (
                "AssertScheduledPlanWithinPolicy"
            ),
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_catalog_covers_every_normative_baseline_behavior(self) -> None:
        source = (BEHAVIOR / "ReachyBaselineBehaviorContracts.cs").read_text(encoding="utf-8")
        for kind in (
            "NeutralIdle",
            "Listening",
            "Speaking",
            "Acknowledgment",
            "Curiosity",
            "Surprise",
            "GazeAcquisition",
            "GazeSearch",
            "UnavailableError",
            "SleepRest",
            "Wake",
        ):
            self.assertIn(kind, source)
        for factory in (
            "NeutralIdle()",
            "Listening()",
            "SpeakingFromTiming()",
            "SpeakingFromAudioEnergy(",
            "Acknowledgment()",
            "Curiosity()",
            "Surprise()",
            "GazeAcquisition(",
            "GazeSearch(",
            "UnavailableError()",
            "SleepRest()",
            "Wake()",
        ):
            self.assertIn(factory, source)

    def test_machine_policy_matches_runtime_defaults(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        self.assertEqual("rma153_baseline_behavior_library_v1", policy["contract_id"])
        self.assertEqual("engineering_estimate", policy["quality"])
        self.assertEqual(
            {
                "neutral_idle",
                "listening",
                "speaking",
                "acknowledgment",
                "curiosity",
                "surprise",
                "gaze_acquisition",
                "gaze_search",
                "unavailable_error",
                "sleep_rest",
                "wake",
            },
            set(policy["behavior_catalog"]),
        )
        runtime = (BEHAVIOR / "ReachyBaselineBehaviorPolicy.cs").read_text(encoding="utf-8")
        named_values = {
            "idlePitchAmplitudeRadians": policy["idle"]["pitch_amplitude_radians"],
            "idleRollAmplitudeRadians": policy["idle"]["roll_amplitude_radians"],
            "idleAntennaAmplitudeRadians": policy["idle"]["antenna_amplitude_radians"],
            "speakingTimingIntensity": policy["speaking"]["timing_intensity"],
            "speakingMaximumPitchRadians": policy["speaking"]["maximum_pitch_radians"],
            "speakingMaximumRollRadians": policy["speaking"]["maximum_roll_radians"],
            "speakingMaximumAntennaRadians": policy["speaking"]["maximum_antenna_radians"],
            "minimumSearchConfidence": policy["gaze_search"]["minimum_confidence"],
            "maximumSearchTargetAgeNanoseconds": policy["gaze_search"][
                "maximum_target_age_nanoseconds"
            ],
            "searchCenterBodyYawRadians": policy["gaze_search"]["center_body_yaw_radians"],
            "searchCenterHeadYawRadians": policy["gaze_search"]["center_head_yaw_radians"],
            "searchCenterHeadPitchRadians": policy["gaze_search"]["center_head_pitch_radians"],
            "searchBodyYawAmplitudeRadians": policy["gaze_search"]["body_yaw_amplitude_radians"],
            "searchHeadYawAmplitudeRadians": policy["gaze_search"]["head_yaw_amplitude_radians"],
            "wakeNeutralPositionToleranceRadians": policy["wake"][
                "neutral_position_tolerance_radians"
            ],
            "wakeNeutralVelocityToleranceRadiansPerSecond": policy["wake"][
                "neutral_velocity_tolerance_radians_per_second"
            ],
            "wakeHeadPitchRadians": policy["wake"]["head_pitch_radians"],
            "wakeAntennaRadians": policy["wake"]["antenna_radians"],
        }
        for name, expected in named_values.items():
            match = re.search(rf"{name}:\s*([0-9_.]+)L?", runtime)
            self.assertIsNotNone(match, name)
            assert match is not None
            actual = float(match.group(1).replace("_", ""))
            self.assertAlmostEqual(float(expected), actual, places=12, msg=name)

    def test_expressive_motion_stays_inside_rma152_planner(self) -> None:
        sources = "\n".join(
            (BEHAVIOR / filename).read_text(encoding="utf-8")
            for filename in (
                "ReachyBaselineBehaviorLibrary.cs",
                "ReachyDeterministicBehaviorPlanner.Baseline.cs",
                "ReachyDeterministicBehaviorPlanner.BaselinePoses.cs",
                "ReachyDeterministicBehaviorPlanner.BaselineGaze.cs",
            )
        )
        for required in (
            "planner.PlanBaseline(",
            "BuildTrajectory(",
            "PlanSafeRest(",
            "ValidateMotionSnapshot",
            "ResolveAndApplyGaze",
            "ResolveLostTargetSearch",
            "ValidateBaselinePolicy",
        ):
            self.assertIn(required, sources)
        for forbidden in (
            "NativeReachySim",
            "ReachySimSession",
            "SubmitCommandsRaw",
            "ReachySimulationCommandBatch",
            "torque",
        ):
            self.assertNotIn(forbidden, sources)

    def test_lost_target_search_is_exact_bounded_and_coverage_gated(self) -> None:
        source = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.BaselineGaze.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "string.Equals(",
            "entity.IsCurrentlyVisible",
            "MaximumSearchTargetAgeNanoseconds",
            "MinimumSearchConfidence",
            "currentCoverageTimestampNanoseconds",
            "gaze-search-current-coverage-outside-age-bound",
            "currentCoverage.ShouldStopVisionDrivenTurning",
            "MinimumValidCoverageFraction",
            "SearchBodyYawAmplitudeRadians",
            "SearchHeadYawAmplitudeRadians",
            "CopyTarget(baseTarget)",
        ):
            self.assertIn(required, source)
        self.assertNotIn("CurrentlyVisibleEntities[0]", source)
        self.assertNotIn("entity.Coverage.ShouldStopVisionDrivenTurning", source)

    def test_speaking_and_idle_cycles_return_without_hidden_randomness(self) -> None:
        poses = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.BaselinePoses.cs").read_text(
            encoding="utf-8"
        )
        contracts = (BEHAVIOR / "ReachyBaselineBehaviorContracts.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("SpeakingFromAudioEnergy", contracts)
        self.assertGreaterEqual(poses.count("CopyTarget(baseTarget)"), 4)
        for forbidden in (
            "DateTime",
            "Environment.TickCount",
            "Random",
            "Guid.NewGuid",
            "Thread.Sleep",
            "Task.Run",
        ):
            self.assertNotIn(forbidden, poses)

    def test_sleep_and_wake_lifecycle_actions_are_explicit_and_fail_safe(self) -> None:
        contracts = (BEHAVIOR / "ReachyBaselineBehaviorContracts.cs").read_text(
            encoding="utf-8"
        )
        planner = (BEHAVIOR / "ReachyDeterministicBehaviorPlanner.Baseline.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "EnterSleepRest",
            "WakeNeutral",
            "PrePlanningLifecycleAction",
            "RequiredPostExecutionLifecycleAction",
            "ResolvePostExecutionLifecycleAction",
            "Succeeded",
        ):
            self.assertIn(required, contracts)
        mapping = (BEHAVIOR / "ReachyBaselineLifecycleResetMapping.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("ReachySimResetPose.SleepRest", mapping)
        self.assertIn("ReachySimResetPose.NeutralAwake", mapping)
        self.assertIn("PlanSafeRest(", planner)
        self.assertIn("IsNeutralWakeSource", planner)
        self.assertIn("wake-requires-fresh-neutral-awake-authoritative-state", planner)
        self.assertIn("executionResult.Completed", contracts)


if __name__ == "__main__":
    unittest.main()
