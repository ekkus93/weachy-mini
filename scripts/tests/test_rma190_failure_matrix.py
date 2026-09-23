#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts" / "run_rma190_failure_matrix.sh"
WORKFLOW = ROOT / ".github" / "workflows" / "rma190-failure-matrix.yml"

CAMERA_TEST = (
    ROOT
    / "Assets"
    / "ReachyMini"
    / "Tests"
    / "Editor"
    / "ReachyAndroidCameraDiscoveryTests.cs"
)
ASR_TEST = (
    ROOT
    / "managed"
    / "ReachyMini.AndroidOnDeviceAsr.Tests"
    / "AndroidOnDeviceAsrTests.Availability.cs"
)
VLM_TRANSPORT_TEST = (
    ROOT
    / "managed"
    / "ReachyMini.Core.Tests"
    / "Rma195VlmTransportContractTests.cs"
)
LOCAL_MODEL_TEST = (
    ROOT
    / "managed"
    / "ReachyMini.LocalModelPackages.Tests"
    / "Program.cs"
)
LOCAL_LLM_PROGRAM = (
    ROOT
    / "managed"
    / "ReachyMini.LocalLlm.Tests"
    / "Program.cs"
)
SOLVER_TEST = (
    ROOT
    / "managed"
    / "ReachyMini.Core.Tests"
    / "Program.Rma032WarningAndFaultTests.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190FailureMatrixContracts(unittest.TestCase):
    def test_runner_executes_all_managed_failure_suites(self) -> None:
        runner = read(RUNNER)
        for project in (
            "ReachyMini.AndroidOnDeviceAsr.Tests.csproj",
            "ReachyMini.Core.Tests.csproj",
            "ReachyMini.LocalModelPackages.Tests.csproj",
            "ReachyMini.LocalLlm.Tests.csproj",
        ):
            self.assertIn(project, runner)
        self.assertIn("REACHY_MANAGED_NATIVE_TESTS=1", runner)
        self.assertIn("REACHY_MANAGED_NATIVE_LIBRARY_DIR", runner)

    def test_permission_denial_and_revocation_evidence_remains_present(self) -> None:
        camera = read(CAMERA_TEST)
        self.assertIn("PermissionIsRequestedOnlyAfterUserAction", camera)
        self.assertIn("GrantedPermissionPublishesInventoryAndRevocation", camera)
        self.assertIn("ReachyCameraPermissionState.PermanentlyDenied", camera)
        self.assertIn("ReachyCameraPermissionState.Revoked", camera)

        asr = read(ASR_TEST)
        self.assertIn("private static async Task PermissionRequired()", asr)
        self.assertIn("SpeechAvailabilityState.PermissionRequired", asr)
        self.assertIn(
            "android_on_device_asr_microphone_permission_required",
            asr,
        )

    def test_network_malformed_and_cancellation_evidence_remains_present(self) -> None:
        source = read(VLM_TRANSPORT_TEST)
        self.assertIn("HttpFailureSurfacesAsTransportFailureNotThrown", source)
        self.assertIn("MalformedJsonSurfacesAsProtocolFailureNotThrown", source)
        self.assertIn("CancellationBeforeSendSurfacesAsCancelled", source)
        self.assertIn("OpenAiVisionTransportStatus.ProtocolFailure", source)

    def test_model_corruption_and_oom_evidence_remains_present(self) -> None:
        packages = read(LOCAL_MODEL_TEST)
        self.assertIn('("wrong hash", TestWrongHashAsync)', packages)
        self.assertIn("corrupt model exposed approved path", packages)

        llm = read(LOCAL_LLM_PROGRAM)
        for test_name in (
            "TestOutOfMemoryBeforeNativeHandleAsync",
            "TestOutOfMemoryAfterNativeStartAsync",
            "TestOutOfMemoryCleanupFailureFaultsAsync",
            "TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync",
        ):
            self.assertIn(test_name, llm)

    def test_solver_fault_retention_evidence_remains_present(self) -> None:
        source = read(SOLVER_TEST)
        self.assertIn("TestAuthoritativeSimulationWorkerFaultRetention", source)
        self.assertIn("ReachySimulationRunState.Faulted", source)
        self.assertIn("worker.Shutdown", source)

    def test_workflow_is_artifact_free_and_uses_native_contract_build(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("reachy_sim_managed_contract", workflow)
        self.assertIn("./scripts/run_rma190_failure_matrix.sh", workflow)
        self.assertNotIn("upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()
