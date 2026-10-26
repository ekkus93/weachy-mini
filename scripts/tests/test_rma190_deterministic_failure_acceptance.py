#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "managed" / "ReachyMini.Core.Tests"
WORKFLOW = ROOT / ".github" / "workflows" / "rma190-deterministic-failure-acceptance.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190DeterministicFailureAcceptanceContracts(unittest.TestCase):
    def test_harness_crosses_required_production_boundaries(self) -> None:
        combined = "\n".join(
            read(path)
            for path in sorted(CORE.glob("Program.Rma190DeterministicFailureAcceptance*.cs"))
        )
        for token in (
            "ReachySharedHttpTransport",
            "OpenAiChatCompletionsLlmProvider",
            "LocalModelPackageManager",
            "LocalLlmProvider.CreateForTestingAsync",
            "ReachySimulationWorker",
            "ReachySimSession.Create",
            "ReachySimResetPose.NeutralAwake",
        ):
            self.assertIn(token, combined)

    def test_harness_covers_every_deterministic_failure_case_fail_closed(self) -> None:
        combined = "\n".join(
            read(path)
            for path in sorted(CORE.glob("Program.Rma190DeterministicFailureAcceptance*.cs"))
        )
        for token in (
            "HttpRequestException",
            "HttpStatusCode.TooManyRequests",
            '"not-json"',
            "ReachyLlmGenerationStatus.Cancelled",
            "LocalModelPackageFailure.Sha256Mismatch",
            "LocalLlmGenerationStatus.ResourceExhausted",
            "LocalLlmProviderState.Faulted",
            "LocalLlmReloadStatus.Reloaded",
            "ReachySimulationRunState.Faulted",
            "hidden_fallback_used",
        ):
            self.assertIn(token, combined)

    def test_machine_readable_report_is_gated_and_validated_by_dedicated_workflow(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("REACHY_RMA190_DETERMINISTIC_FAILURE_ACCEPTANCE: '1'", workflow)
        self.assertIn("REACHY_RMA190_FAILURE_REPORT:", workflow)
        self.assertIn("rma190-deterministic-failure-acceptance.json", workflow)
        for field in (
            "network_loss",
            "rate_limit",
            "malformed_response",
            "cancellation",
            "model_corruption",
            "model_oom_recovery",
            "solver_fault",
            "controlled_reset",
        ):
            self.assertIn(field, workflow)
        self.assertNotIn("actions/upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()
