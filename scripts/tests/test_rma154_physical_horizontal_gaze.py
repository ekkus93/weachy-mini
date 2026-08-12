import math
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GAZE_PLANNER = (
    ROOT
    / "Assets/ReachyMini/Runtime/Core/Behavior/ReachyDeterministicBehaviorPlanner.GazeAndPoses.cs"
)


class Rma154PhysicalHorizontalGazeContracts(unittest.TestCase):
    def test_direct_horizontal_gaze_uses_body_yaw_without_stewart_yaw(self) -> None:
        source = GAZE_PLANNER.read_text(encoding="utf-8")
        start = source.index("private ReachyBehaviorPlanResult? ResolveAndApplyGaze")
        end = source.index("private static void ApplyExpression", start)
        direct_gaze = source[start:end]

        self.assertIn(
            "double horizontal = -Math.Atan2(direction.X, direction.Z);",
            direct_gaze,
        )
        self.assertIn("horizontal * 0.45", direct_gaze)
        self.assertIn("yawRadians: 0.0", direct_gaze)
        self.assertNotIn("double headYaw", direct_gaze)
        self.assertNotIn("horizontal - bodyYaw", direct_gaze)

    def test_rma154_edge_target_converges_with_bounded_body_yaw_gain(self) -> None:
        center_x = 0.72
        for _ in range(3):
            target_angle = math.atan2((2.0 * center_x) - 1.0, 1.0)
            body_yaw = max(-0.35, min(0.35, -target_angle * 0.45))
            residual_angle = target_angle + body_yaw
            center_x = 0.5 + (math.tan(residual_angle) * 0.5)

        self.assertLessEqual(abs(center_x - 0.5), 0.06)


if __name__ == "__main__":
    unittest.main()
