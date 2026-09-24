#!/usr/bin/env python3
"""Static guardrails for the deterministic RMA-190 failure-scenario harness."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE_PROGRAM = ROOT / "managed/ReachyMini.Core.Tests/Program.cs"
CORE_SCENARIOS = ROOT / "managed/ReachyMini.Core.Tests/Program.Rma190FailureScenarioTests.cs"
CAMERA_TESTS = ROOT / "managed/ReachyMini.Camera.Tests/Program.cs"
MODEL_TESTS = ROOT / "managed/ReachyMini.LocalModelPackages.Tests/Program.cs"
GOVERNOR_TESTS = ROOT / "managed/ReachyMini.ResourceGovernor.Tests/Program.cs"
LLM_TESTS = ROOT / "managed/ReachyMini.Core.Tests/Rma142Rma143OpenAiLlmAdapterContractTests.cs"
VLM_TESTS = ROOT / "managed/ReachyMini.Core.Tests/Rma195VlmTransportContractTests.cs"
PRODUCTION_RUNTIME = ROOT / "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs"
SETTINGS_COMPOSITION = ROOT / "Assets/ReachyMini/Runtime/Application/ReachySettingsApplicationCompositionProvider.cs"
NATIVE_CONTROLS = ROOT / "native/reachy_sim/tests/reachy_sim_fake_backend.c"
WORKFLOW = ROOT / ".github/workflows/rma190-failure-scenarios.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190FailureScenarioContracts(unittest.TestCase):
    def test_core_entrypoint_executes_http_and_native_recovery_scenarios(self) -> None:
        program = read(CORE_PROGRAM)
        self.assertIn("TestRma190HttpFailureScenarioMatrix();", program)
        self.assertIn("TestRma190SolverFaultControlledReset();", program)
        self.assertIn(
            'Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS")',
            program,
        )

    def test_network_failure_matrix_is_fail_closed(self) -> None:
        source = read(CORE_SCENARIOS)
        for token in (
            "new HttpRequestException",
            "ReachyHttpErrorCategory.Network",
            "ReachyHttpErrorCategory.QuotaOrRateLimited",
            "ReachyHttpErrorCategory.Cancelled",
            "result.Succeeded",
            "handler.CallCount",
        ):
            self.assertIn(token, source)
        self.assertIn("maximumAttempts: 2", source)
        self.assertIn("maximumAttempts: 1", source)

    def test_malformed_provider_responses_are_explicit_failures(self) -> None:
        llm = read(LLM_TESTS)
        vlm = read(VLM_TESTS)
        self.assertIn("MalformedAssistantJsonSurfacesAsInvalidNotThrown", llm)
        self.assertIn(
            "ReachyLlmGenerationStatus.BehaviorIntentInvalid",
            llm,
        )
        self.assertIn(
            "MalformedJsonSurfacesAsProtocolFailureNotThrown",
            vlm,
        )
        self.assertIn("OpenAiVisionTransportStatus.ProtocolFailure", vlm)

    def test_solver_fault_requires_recreation_then_neutral_reset(self) -> None:
        source = read(CORE_SCENARIOS)
        native_controls = read(NATIVE_CONTROLS)
        runtime = read(PRODUCTION_RUNTIME)
        composition = read(SETTINGS_COMPOSITION)
        self.assertIn("SetStepFailure(failStep: true)", source)
        self.assertIn("WaitForWorkerFault", source)
        self.assertIn("ReachySimulationRunState.Faulted", source)
        self.assertIn('"step"', source)
        self.assertIn("ReachySimErrorCode.NumericFailure", source)
        self.assertIn("ReachySimRecoverability.RecreateHandle", source)
        self.assertGreaterEqual(
            source.count("ReachySimSession.Create(modelBytes)"),
            2,
        )
        self.assertIn("REACHY_SIM_STATUS_NUMERIC_FAILURE", native_controls)
        self.assertIn("simulated numeric solver failure", native_controls)
        self.assertIn("ReachySimResetPose.NeutralAwake", source)
        self.assertIn("RMA-190 controlled neutral reset", source)
        self.assertIn("RecoverFaultedAndResetNeutral", runtime)
        self.assertIn("StartProductionRuntime();", runtime)
        self.assertIn("nextCommandSequence = 1UL;", runtime)
        self.assertIn("renderer.enabled = true;", runtime)
        self.assertIn("runtime.RecoverFaultedAndResetNeutral()", composition)
        self.assertIn(
            "runtime.Status == ReachyProductionRuntimeStatus.Faulted",
            composition,
        )

    def test_permission_corruption_and_oom_supporting_contracts_are_present(self) -> None:
        camera = read(CAMERA_TESTS)
        models = read(MODEL_TESTS)
        governor = read(GOVERNOR_TESTS)
        self.assertIn("PermissionTransitionsRemainExplicit", camera)
        self.assertIn("ReachyCameraPermissionState.Denied", camera)
        self.assertIn("ReachyCameraPermissionState.Revoked", camera)
        self.assertIn("corrupt model exposed approved path", models)
        self.assertIn("latched OOM recovery", governor)
        self.assertIn("RecentOutOfMemory", governor)

    def test_dedicated_workflow_runs_all_deterministic_scenario_projects(self) -> None:
        workflow = read(WORKFLOW)
        for project in (
            "ReachyMini.Core.Tests",
            "ReachyMini.Camera.Tests",
            "ReachyMini.RemoteVlm.Tests",
            "ReachyMini.LocalModelPackages.Tests",
            "ReachyMini.ResourceGovernor.Tests",
        ):
            self.assertIn(project, workflow)
        self.assertIn("REACHY_MANAGED_NATIVE_TESTS=1", workflow)
        self.assertNotIn("upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()
