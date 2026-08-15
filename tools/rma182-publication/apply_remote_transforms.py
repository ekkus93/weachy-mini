#!/usr/bin/env python3
"""Apply RMA-182 edits that must be rebased onto the exact current master files."""
from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

EXPECTED_BLOBS = {
    "Assets/ReachyMini/Runtime/Application/ReachyApplicationHostBehaviour.cs": "d86ae31428e918ca1a2d7f7f8053fbe918cebbf9",
    "Assets/ReachyMini/Runtime/Application/ReachySettingsApplicationCompositionProvider.cs": "02bd4eb693cede8906747de6f6652972b265f28c",
    "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs": "e84234f2c6666ae2a66f2a9b6c75e354c92cb953",
    "managed/ReachyMini.Core.Tests/Program.cs": "73456d50c690b8b285850501f91b5f1408a86935",
    "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md": "7b826202884f02dacccbd6c4985d5be3f1695d02",
}


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


def verify_blob(root: Path, relative: str, expected: str) -> None:
    path = root / relative
    actual = subprocess.check_output(
        ["git", "-C", str(root), "hash-object", str(path)],
        text=True,
    ).strip()
    if actual != expected:
        raise RuntimeError(
            f"{relative}: expected master blob {expected}, found {actual}; refusing stale transform"
        )


def transform_host(root: Path) -> None:
    relative = "Assets/ReachyMini/Runtime/Application/ReachyApplicationHostBehaviour.cs"
    path = root / relative
    text = read(path)
    text = replace_once(
        text,
        "        private ReachyApplicationHost? host;\n        private bool startupEntered;",
        "        private ReachyApplicationHost? host;\n"
        "        private ReachyApplicationInterruptionCoordinator? interruptionCoordinator;\n"
        "        private bool startupEntered;",
        f"{relative}: coordinator field",
    )
    text = replace_once(
        text,
        "                host.HealthChanged += OnHealthChanged;\n                host.Start();",
        "                host.HealthChanged += OnHealthChanged;\n"
        "                host.Start();\n"
        "                interruptionCoordinator =\n"
        "                    new ReachyApplicationInterruptionCoordinator(host);",
        f"{relative}: coordinator startup",
    )
    text = replace_once(
        text,
        "        public void ShutdownApplication()\n        {\n"
        "            ReachyApplicationHost? activeHost = host;",
        "        public void ShutdownApplication()\n        {\n"
        "            ReachyApplicationInterruptionCoordinator? coordinator =\n"
        "                interruptionCoordinator;\n"
        "            interruptionCoordinator = null;\n"
        "            coordinator?.Dispose();\n\n"
        "            ReachyApplicationHost? activeHost = host;",
        f"{relative}: coordinator shutdown",
    )
    lifecycle = '''        private void OnApplicationPause(bool paused)\n        {\n            ReachyApplicationInterruptionCoordinator? coordinator =\n                interruptionCoordinator;\n            if (coordinator == null || host == null)\n            {\n                return;\n            }\n\n            ReachyAndroidCameraAcquisition? acquisition =\n                UnityEngine.Object.FindAnyObjectByType<\n                    ReachyAndroidCameraAcquisition>();\n            try\n            {\n                ReachyApplicationInterruptionResult result;\n                if (paused)\n                {\n                    acquisition?.PauseForApplicationInterruption();\n                    result = coordinator.Pause();\n                }\n                else\n                {\n                    result = coordinator.Resume();\n                    if (result.Succeeded)\n                    {\n                        acquisition?.ResumeAfterApplicationInterruption();\n                    }\n                }\n\n                if (!result.Succeeded)\n                {\n                    EnterFault(\n                        "Application interruption transition failed: " +\n                        result.Diagnostic);\n                }\n            }\n            catch (Exception exception)\n            {\n                EnterFault(\n                    "Application interruption transition failed (" +\n                    exception.GetType().Name + ").");\n            }\n        }\n\n'''
    text = replace_once(
        text,
        "        private void OnDestroy()\n",
        lifecycle + "        private void OnDestroy()\n",
        f"{relative}: lifecycle ingress",
    )
    write(path, text)


def transform_settings(root: Path) -> None:
    relative = "Assets/ReachyMini/Runtime/Application/ReachySettingsApplicationCompositionProvider.cs"
    path = root / relative
    text = read(path)
    text = replace_once(
        text,
        "    internal sealed class ReachySettingsMainScreenApplicationService :\n"
        "        ReachyApplicationServiceBase,\n"
        "        IReachyUserInterfaceService\n",
        "    internal sealed class ReachySettingsMainScreenApplicationService :\n"
        "        ReachyApplicationServiceBase,\n"
        "        IReachyUserInterfaceService,\n"
        "        IReachyApplicationInterruptionParticipant\n",
        f"{relative}: participant declaration",
    )
    methods = '''        public void PauseForApplicationInterruption()\n        {\n            _ = stateStore.PauseForApplicationInterruption();\n        }\n\n        public void ResumeAfterApplicationInterruption()\n        {\n            _ = stateStore.ResumeAfterApplicationInterruption();\n        }\n\n'''
    text = replace_once(
        text,
        "        private void OnCameraCapabilitiesChanged(\n",
        methods + "        private void OnCameraCapabilitiesChanged(\n",
        f"{relative}: lifecycle methods",
    )
    write(path, text)


def transform_runtime(root: Path) -> None:
    relative = "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs"
    path = root / relative
    text = read(path)
    methods = '''        public ReachySimulationControlResult PauseForApplicationInterruption()\n        {\n            ReachySimulationWorker? activeWorker = worker;\n            if (activeWorker == null)\n            {\n                return ReachySimulationControlResult.Failure(\n                    ReachySimulationRunState.Stopped,\n                    new ReachySimError(\n                        ReachySimErrorCode.InvalidHandle,\n                        ReachySimRecoverability.RecreateHandle,\n                        "The production simulation worker is unavailable for application pause."));\n            }\n            if (Status == ReachyProductionRuntimeStatus.Paused)\n            {\n                return ReachySimulationControlResult.Success(\n                    ReachySimulationRunState.Paused);\n            }\n            if (Status != ReachyProductionRuntimeStatus.Running)\n            {\n                return ReachySimulationControlResult.Failure(\n                    activeWorker.State,\n                    new ReachySimError(\n                        ReachySimErrorCode.InvalidArgument,\n                        ReachySimRecoverability.Retry,\n                        $"Cannot pause the production runtime while it is {Status}."));\n            }\n\n            ReachySimulationControlResult result =\n                activeWorker.Pause(ControlTimeout);\n            if (result.IsSuccess)\n            {\n                Status = ReachyProductionRuntimeStatus.Paused;\n            }\n            return result;\n        }\n\n        public ReachySimulationControlResult ResumeAfterApplicationInterruption()\n        {\n            ReachySimulationWorker? activeWorker = worker;\n            if (activeWorker == null)\n            {\n                return ReachySimulationControlResult.Failure(\n                    ReachySimulationRunState.Stopped,\n                    new ReachySimError(\n                        ReachySimErrorCode.InvalidHandle,\n                        ReachySimRecoverability.RecreateHandle,\n                        "The production simulation worker is unavailable for application resume."));\n            }\n            if (Status == ReachyProductionRuntimeStatus.Running)\n            {\n                return ReachySimulationControlResult.Success(\n                    ReachySimulationRunState.Running);\n            }\n            if (Status != ReachyProductionRuntimeStatus.Paused)\n            {\n                return ReachySimulationControlResult.Failure(\n                    activeWorker.State,\n                    new ReachySimError(\n                        ReachySimErrorCode.InvalidArgument,\n                        ReachySimRecoverability.Retry,\n                        $"Cannot resume the production runtime while it is {Status}."));\n            }\n\n            ReachySimulationControlResult result =\n                activeWorker.Resume(ControlTimeout);\n            if (result.IsSuccess)\n            {\n                Status = ReachyProductionRuntimeStatus.Running;\n            }\n            return result;\n        }\n\n'''
    text = replace_once(
        text,
        "        public ReachySimulationControlResult ResetNeutral()\n",
        methods + "        public ReachySimulationControlResult ResetNeutral()\n",
        f"{relative}: explicit lifecycle methods",
    )
    start = text.find("        private void OnApplicationPause(bool paused)\n")
    end = text.find("        private void OnDestroy()\n", start)
    if start < 0 or end < 0:
        raise RuntimeError(f"{relative}: expected current OnApplicationPause block")
    if text.find("        private void OnApplicationPause(bool paused)\n", start + 1) >= 0:
        raise RuntimeError(f"{relative}: multiple OnApplicationPause blocks")
    text = text[:start] + text[end:]
    write(path, text)


def transform_program(root: Path) -> None:
    relative = "managed/ReachyMini.Core.Tests/Program.cs"
    path = root / relative
    text = read(path)
    text = replace_once(
        text,
        "            Rma180PerformanceHarnessContractTests.RunAll();\n",
        "            Rma180PerformanceHarnessContractTests.RunAll();\n"
        "            Rma182ApplicationInterruptionContractTests.RunAll();\n",
        f"{relative}: registration",
    )
    write(path, text)


def transform_roadmap(root: Path) -> None:
    relative = "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
    path = root / relative
    text = read(path)
    old = '''## RMA-182 — Harden pause/resume and interruption handling\n\n- [ ] Pause simulation deterministically.\n- [ ] Stop/release camera and speech resources as required by lifecycle.\n- [ ] Cancel or suspend network and inference jobs safely.\n- [ ] Resume without simulation catch-up.\n- [ ] Restore UI/conversation to a defined state.\n- [ ] Test repeated background/foreground cycles.\n'''
    new = '''## RMA-182 — Harden pause/resume and interruption handling\n\n**Status:** Complete (2026-08-15)\n\n- [x] Pause simulation deterministically.\n- [x] Stop/release camera and speech resources as required by lifecycle.\n- [x] Cancel or suspend network and inference jobs safely.\n- [x] Resume without simulation catch-up.\n- [x] Restore UI/conversation to a defined state.\n- [x] Test repeated background/foreground cycles.\n\n**Completion evidence**\n\n- `ReachyApplicationInterruptionCoordinator` provides the single application-service pause/resume state machine, pausing dependents before dependencies and resuming dependencies before dependents with idempotent repeated callbacks and fail-closed transition faults.\n- `ReachySimulationWorker` retains its existing deterministic pause boundary and resets both the fixed-step accumulator and monotonic clock baseline on pause/resume, so elapsed background wall-clock time is never replayed as simulation catch-up.\n- CameraX acquisition now exposes explicit lifecycle pause/resume operations driven by `ReachyApplicationHostBehaviour`; resume revalidates camera permission before restoring the desired stream.\n- Speech focus, shared HTTP transport, local LLM generation, and VLM scheduling now expose lifecycle interruption hooks. Active work is cancelled, new work is rejected while backgrounded, and resume creates fresh work generations rather than restarting cancelled operations.\n- Conversation and main-screen state use lifecycle-owned interruption states. Active turns are cancelled; resume returns only lifecycle-owned state to Idle while preserving pre-existing Error/Unavailable conditions.\n- Managed contracts exercise deterministic ordering, cancellation, error preservation, and five repeated background/foreground cycles. Static design coverage and local validation are recorded in `docs/RMA_182_LIFECYCLE_HARDENING_SPEC_2026-08-15.md`, `docs/validation/RMA_182_LIFECYCLE_HARDENING_LOCAL_VALIDATION_2026-08-15.md`, and `scripts/tests/test_rma182_lifecycle_hardening.py`.\n'''
    text = replace_once(text, old, new, f"{relative}: RMA-182 block")
    write(path, text)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--skip-blob-guards", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()

    if not args.skip_blob_guards:
        for relative, expected in EXPECTED_BLOBS.items():
            verify_blob(root, relative, expected)

    transform_host(root)
    transform_settings(root)
    transform_runtime(root)
    transform_program(root)
    transform_roadmap(root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
