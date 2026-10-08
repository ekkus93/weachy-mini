#!/usr/bin/env python3
"""RMA-181 transition evidence must come from the exact policy input sample."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachyPriorityDegradationRuntime.cs"
DRIVER = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyPriorityDegradationRuntimeDriver.cs"


class Rma181ResourceTransitionEvidence(unittest.TestCase):
    def test_sample_and_policy_decision_share_one_capture(self) -> None:
        core = CORE.read_text(encoding="utf-8")
        driver = DRIVER.read_text(encoding="utf-8")
        self.assertIn("LastSignals = signals;", core)
        self.assertIn("coordinator.EvaluateAndApply(signals)", core)
        self.assertIn("priorityRuntime.LastSignals", driver)
        self.assertIn("lastReportedLevel != decision.Level", driver)
        self.assertIn("criticalReasonChanged", driver)
        for name in (
            "TotalMemoryBytes",
            "AvailableMemoryBytes",
            "LowMemoryThresholdBytes",
            "SystemReportsLowMemory",
            "PhysicsBudgetState",
        ):
            self.assertIn("sampled." + name, driver)
        self.assertIn("CurrentDecision = decision;", driver)


if __name__ == "__main__":
    unittest.main()
