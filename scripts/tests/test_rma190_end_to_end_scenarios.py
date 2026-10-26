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
            read(path) for path in sorted(APP.glob("ReachyRma190CoreEndToEndAcceptance*.cs"))
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
        self.assertIn("WaitForSettledStateAsync", source)
        self.assertNotIn("Task.Delay(250", source)
        self.assertIn("actuator_observation_count = neutral.ActuatorObservationCount", source)
        self.assertNotIn("ReachySimFake", source)
        self.assertNotIn("requested_target_used_as_motion_proof = true", source)

    def test_unity_metadata_is_captured_before_worker_thread_awaits(self) -> None:
        source = "\n".join(
            read(path) for path in sorted(APP.glob("ReachyRma190CoreEndToEndAcceptance*.cs"))
        )
        device_index = source.index("string deviceModel = SystemInfo.deviceModel;")
        api_index = source.index("int androidApiLevel = ReadApiLevel();")
        worker_index = source.index(".ConfigureAwait(false);")
        self.assertLess(device_index, worker_index)
        self.assertLess(api_index, worker_index)
        self.assertIn("device_model = deviceModel", source)
        self.assertIn("android_api_level = androidApiLevel", source)

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
        self.assertIn('report["actuator_observation_count"] == 9', runner)
        self.assertIn('report["human_asr_transcript_injected"] is False', runner)
        self.assertIn('report["cloud_credentials_injected"] is False', runner)
        self.assertIn('report["local_model_injected"] is False', runner)

    def test_core_evidence_is_private_and_scoped(self) -> None:
        runner = read(RUNNER)
        for forbidden in (
            "logcat -d",
            "logcat -c",
            "dumpsys connectivity",
            "dumpsys wifi",
            "dumpsys activity activities",
            "shell getprop >",
            "screencap -p",
        ):
            self.assertNotIn(forbidden, runner)
        self.assertIn('read_report > "${REPORT_DIR}/device-report.json"', runner)
        self.assertIn('> "${REPORT_DIR}/network-disabled.txt"', runner)
        self.assertIn("settings get global wifi_on", runner)
        self.assertIn("settings get global mobile_data", runner)
        self.assertIn('report["network_offline"] is True', runner)
        self.assertIn('report["active_network_validated"] is False', runner)

    def test_dedicated_workflow_runs_core_rma190_independently(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("name: RMA-190 Core End-to-End Android Acceptance", workflow)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn("run_rma190_core_end_to_end_android.sh", workflow)
        self.assertIn("rma190-core-end-to-end-report-${{ github.sha }}", workflow)
        upload = workflow.split("- name: Upload RMA-190 core end-to-end evidence", 1)[1]
        upload_header = upload.split("uses: actions/upload-artifact@v4", 1)[0]
        self.assertIn("continue-on-error: true", upload_header)
        acceptance = workflow.split("- name: Run RMA-190 core end-to-end acceptance", 1)[1]
        acceptance = acceptance.split("- name: Upload RMA-190 core end-to-end evidence", 1)[0]
        self.assertNotIn("continue-on-error: true", acceptance)
        self.assertNotIn("RMA-195", workflow)

    def test_roadmap_reconciles_core_evidence_and_keeps_remaining_open(self) -> None:
        todo = read(TODO)
        start = todo.index("## RMA-190")
        end = todo.index("## RMA-191", start)
        section = todo[start:end]

        for item in (
            "Offline launch with no network.",
            "MuJoCo full model load and neutral reset.",
            "Deterministic gesture replay.",
        ):
            self.assertIn(f"- [x] {item}", section)
            self.assertNotIn(f"- [ ] {item}", section)

        for item in (
            "Front camera -> head rotation -> transformed Reachy-eye frame.",
            "Rear-camera visual question with optional VLM.",
            "Independent cloud provider combinations.",
            "Permission denial/revocation.",
            "Network loss, rate limit, malformed response, and cancellation.",
            "Model corruption/OOM.",
            "Solver fault and controlled reset.",
        ):
            self.assertIn(f"- [x] {item}", section)
            self.assertNotIn(f"- [ ] {item}", section)

        for item in ("On-device ASR -> local LLM -> behavior -> offline TTS.",):
            self.assertIn(f"- [ ] {item}", section)
            self.assertNotIn(f"- [x] {item}", section)

        self.assertIn("36806841674", section)
        self.assertIn("1c741bced63e62d5dcac5130f9ee9e981660b6cd", section)
        self.assertIn("37605466118", section)
        self.assertIn("37605466032", section)
        self.assertIn("859aa4ed9fc04030e5056511d027af83e0baa85a", section)
        self.assertIn("37757168563", section)
        self.assertIn("b9f4d7397630f6f6392f2dee9bf586ab1bb68f7b", section)
        self.assertIn("11540502224", section)
        self.assertIn("37759478803", section)
        self.assertIn("6f3019c4c9e8a8c963161267defaff71a966a658", section)
        self.assertIn("11541646432", section)
        self.assertIn("37994546094", section)
        self.assertIn("3895bb93f5faef7fef1169ffeb747a4347f88c21", section)
        self.assertIn("11648195872", section)
        self.assertIn("**not** a live-cloud provider test", section)
        self.assertIn("37760354266", section)
        self.assertIn("405711d5dd0d03253959742dcf66a957012ad0a8", section)
        self.assertNotIn("**Status:** Complete", section)


if __name__ == "__main__":
    unittest.main()
