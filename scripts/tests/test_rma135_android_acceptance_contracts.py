from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ACCEPTANCE = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.cs"
)
RUNNER = ROOT / "scripts/run_rma135_resource_governor_acceptance_android.sh"


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        raise AssertionError(f"forbidden {label}: {needle}")


def main() -> None:
    text = ACCEPTANCE.read_text(encoding="utf-8")
    runner = RUNNER.read_text(encoding="utf-8")
    require(text, "ReachyProductionAuthoritativeRuntime", "live production runtime")
    require(
        text,
        "FindFirstObjectByType<ReachyProductionAuthoritativeRuntime>",
        "production runtime discovery",
    )
    require(
        text,
        "ReachySimulationLocalLlmPhysicsBudgetSource",
        "authoritative physics budget source",
    )
    require(
        text,
        "ReachyAndroidLocalLlmResourceSignalSource",
        "Android memory and thermal signals",
    )
    require(
        text,
        "LocalLlmGovernedGenerationCoordinator",
        "production governed generation coordinator",
    )
    require(text, "controlled_one_shot_budget_exceeded", "explicitly labeled fault injection")
    require(
        text,
        "ResourceCancelledDuringGeneration",
        "physics-priority cancellation assertion",
    )
    require(text, "worker_steps_after_injection", "worker continuity evidence")
    require(text, "post_load_stabilization_started", "real post-load stabilization")
    require(text, "post_load_stabilized_mode", "post-load recovery evidence")
    require(text, "ProfileFitsWithin", "loaded-profile post-load safety check")
    require(text, "recovery_observations", "explicit recovery evidence")
    require(
        text,
        "post_recovery_provider_status",
        "same-process post-recovery generation evidence",
    )
    require(
        text,
        "report_contains_prompt_or_response_content = false",
        "privacy-safe report marker",
    )
    require(
        text,
        "production_physics_runtime_preserved",
        "non-owning production simulation checkpoint",
    )
    require(text, "requiredConsecutiveAdmissible = 3", "startup stabilization window")
    require(text, "startup_physics_exceeded_observations", "startup miss evidence")
    require(runner, 'mkdir -p "${REPORT_DIR}/checkpoints"', "checkpoint evidence directory")
    require(runner, '"${ADB[@]}" pull "${checkpoint_path}"', "all-checkpoint device pull")
    require(
        runner,
        'r["post_load_stabilization_observations"]',
        "post-load stabilization report validation",
    )
    forbid(text, "CreateAndStartSimulationWorker", "duplicate simulation factory")
    forbid(text, "ReachySimSession.Create", "duplicate native session creation")
    forbid(text, "worker.Dispose()", "production worker disposal")
    forbid(text, "RunPhysicsLoop", "standalone stopwatch physics loop")
    forbid(text, "Thread.Sleep", "acceptance-owned physics scheduler")
    forbid(text, "simulationSession?.Dispose()", "duplicate outer session disposal")
    forbid(text, "worker.Shutdown(WorkerControlTimeout)", "redundant outer worker shutdown")
    forbid(text, "out ReachySimSession simulationSession", "escaping session ownership")
    forbid(text, "physics_timestep_modified = true", "physics timestep degradation")
    forbid(text, "network_fallback_used = true", "cloud fallback")
    forbid(text, "automatic_retry_used = true", "automatic request replay")
    print("RMA-135 Android physical acceptance contracts passed.")


if __name__ == "__main__":
    main()
