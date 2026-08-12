import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ACCEPTANCE = (
    ROOT
    / "Assets/ReachyMini/Runtime/Application/ReachyRma154VisualServoAcceptance.cs"
)
NATIVE_RESET = (
    ROOT
    / "native/reachy_sim/src/reachy_sim_backend_mujoco/model_and_reset.inc"
)


class Rma154ResetSettleBarrierContracts(unittest.TestCase):
    def test_acceptance_waits_for_stable_authoritative_reset_state(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        for required in (
            "ResetSettleSampleDelayMilliseconds = 20",
            "ResetSettleConsecutiveSamples = 8",
            "ResetSettleMaximumMotionRadians = 5.0e-4",
            "ResetSettleMaximumVelocityRadiansPerSecond = 2.0e-2",
            "frame.ContinuityId == previousContinuityId",
            "frame.Sequence <= lastSequence",
            "MaximumHeadBodyMotion(previousMotion, currentMotion)",
            "MaximumHeadBodyVelocity(currentMotion)",
            "stableSampleCount >= ResetSettleConsecutiveSamples",
            "reset continuity changed while waiting for the neutral mechanism to settle",
        ):
            self.assertIn(required, source)

    def test_native_neutral_reset_does_not_integrate_to_equilibrium(self) -> None:
        source = NATIVE_RESET.read_text(encoding="utf-8")
        start = source.index("static ReachySimStatus reset_context")
        end = source.index("static void mujoco_destroy", start)
        reset_context = source[start:end]
        self.assertIn("mj_resetData(context->model, context->data);", reset_context)
        self.assertIn("mj_forward(context->model, context->data);", reset_context)
        self.assertNotIn("mj_step(", reset_context)


if __name__ == "__main__":
    unittest.main()
