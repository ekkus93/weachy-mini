#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RUNNER = ROOT / "scripts/run_rma190_core_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-core-end-to-end.yml"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190EndToEndScenarioContracts(unittest.TestCase):
    def test_core_physical_acceptance_uses_shipping_runtime_and_behavior(self) -> None:
        source = "\n".join(
            read(path)
            for path in sorted(APP.glob("ReachyRma190CoreEndToEndAcceptance*.cs"))
        )
        self.assertIn("FindAnyObjectByType<ReachyApplicationHostBehaviour>", source)
        self.assertIn("ReachyProductionSimulationApplicationService", source)
        self.assertIn("ReachyBaselineBehaviorApplicationService", source)
        self.assertIn("FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>", source)
        self.assertIn("runtime.ResetNeutral()", source)
        self.assertIn("ReachyBaselineBehaviorRequest.Acknowledgment()", source)
        self.assertIn("ReachyProductionBehaviorControllerTargetSink(runtime)", source)
        self.assertIn("[Serializable]", source)
        self.assertIn("MaximumQposDifference", source)
        self.assertIn("MaximumQvelDifference", source)
        self.assertNotIn("ReachySimFake", source)
        self.assertNotIn("requested_target_used_as_motion_proof = true", source)

    def test_offline_runner_disables_and_restores_network(self) -> None:
        runner = read(RUNNER)
        self.assertIn("svc wifi disable", runner)
        self.assertIn("svc data disable", runner)
        self.assertIn("restore_network", runner)
        self.assertIn("wifi_before", runner)
        self.assertIn("data_before", runner)
        self.assertIn("pm clear", runner)
        self.assertIn("--ez reachy_rma190_core_e2e_acceptance true", runner)
        self.assertIn('report["network_offline"] is True', runner)
        self.assertIn('report["active_network_validated"] is False', runner)
        self.assertIn('report["human_asr_transcript_injected"] is False', runner)
        self.assertIn('report["cloud_credentials_injected"] is False', runner)
        self.assertIn('report["local_model_injected"] is False', runner)

    def test_dedicated_workflow_separates_contract_and_physical_gates(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("Validate RMA-190 source contracts", workflow)
        self.assertIn("python3 scripts/tests/test_rma190_end_to_end_scenarios.py", workflow)
        self.assertIn("Run RMA-190 core scenarios on Android", workflow)
        self.assertIn("github.event_name != 'pull_request'", workflow)
        self.assertIn("run_rma190_core_end_to_end_android.sh", workflow)
        self.assertIn("rma190-core-end-to-end-report-${{ github.sha }}", workflow)
        self.assertNotIn("RMA-195 application-composition acceptance", workflow)

    def test_roadmap_keeps_unimplemented_rma190_scenarios_open(self) -> None:
        todo = read(TODO)
        start = todo.index("## RMA-190")
        end = todo.index("## RMA-191", start)
        section = todo[start:end]
        for item in (
            "Front camera -> head rotation -> transformed Reachy-eye frame.",
            "On-device ASR -> local LLM -> behavior -> offline TTS.",
            "Rear-camera visual question with optional VLM.",
            "Independent cloud provider combinations.",
            "Permission denial/revocation.",
            "Network loss, rate limit, malformed response, and cancellation.",
            "Model corruption/OOM.",
            "Solver fault and controlled reset.",
        ):
            self.assertIn(f"- [ ] {item}", section)
        self.assertNotIn("**Status:** Complete", section)


if __name__ == "__main__":
    unittest.main()
