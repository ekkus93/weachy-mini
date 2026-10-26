#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


class Rma190FailureModeScenarioContracts(unittest.TestCase):
    def test_permission_denial_and_revocation_fail_closed(self) -> None:
        camera = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyAndroidCameraAcquisition.StateTranslation.cs"
        )
        asr = read(
            "managed/ReachyMini.AndroidOnDeviceAsr.Tests/"
            "AndroidOnDeviceAsrTests.Availability.cs"
        )
        self.assertIn('case "PermissionRevoked":', camera)
        self.assertIn("desiredActive = false;", camera)
        self.assertIn("state.MarkPermissionRevoked(detail);", camera)
        self.assertIn("SpeechAvailabilityState.PermissionRequired", asr)
        self.assertIn("SpeechErrorCategory.Permission", asr)
        self.assertIn('"recognizer starts before permission"', asr)

    def test_network_and_provider_failures_are_explicit_non_success(self) -> None:
        provider = read(
            "managed/ReachyMini.Core.Tests/"
            "Rma142Rma143OpenAiLlmAdapterContractTests.cs"
        )
        for contract in (
            "NetworkLossSurfacesAsHttpFailure",
            "RateLimitSurfacesAsHttpFailure",
            "MalformedAssistantJsonSurfacesAsInvalidNotThrown",
            "CancellationBeforeSendSurfacesAsCancelled",
        ):
            self.assertIn(contract, provider)
        self.assertIn("ReachyLlmGenerationStatus.HttpFailure", provider)
        self.assertIn("ReachyLlmGenerationStatus.BehaviorIntentInvalid", provider)
        self.assertIn("ReachyLlmGenerationStatus.Cancelled", provider)

    def test_corrupt_model_and_oom_require_explicit_recovery(self) -> None:
        packages = read("managed/ReachyMini.LocalModelPackages.Tests/Program.cs")
        llm = read("managed/ReachyMini.LocalLlm.Tests/Program.cs")
        oom = read("managed/ReachyMini.LocalLlm.Tests/Program.OutOfMemoryTests.cs")
        self.assertIn("LocalModelPackageFailure.Sha256Mismatch", packages)
        self.assertIn("LocalModelPackageFailure.InstalledArtifactCorrupt", packages)
        self.assertIn("TestOutOfMemoryBeforeNativeHandleAsync", llm)
        self.assertIn("TestOutOfMemoryAfterNativeStartAsync", llm)
        self.assertIn("TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync", llm)
        self.assertIn("Explicit reload did not recover the provider after OOM", oom)
        self.assertIn(
            "Second generation after explicit OOM reload did not succeed",
            oom,
        )

    def test_solver_fault_stops_then_recreates_and_neutral_resets(self) -> None:
        solver = read(
            "managed/ReachyMini.Core.Tests/Program.Rma032WarningAndFaultTests.cs"
        )
        self.assertIn("ReachySimulationRunState.Faulted", solver)
        self.assertIn("recoveryCreateResult", solver)
        self.assertIn("ReachySimResetPose.NeutralAwake", solver)
        self.assertIn('"post-fault recreated worker progress"', solver)
        self.assertIn('"recovery worker shutdown"', solver)


if __name__ == "__main__":
    unittest.main()
