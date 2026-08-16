from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# ReachyRma135ResourceGovernorAcceptance.cs is planned to split
# (docs/LARGE_FILE_REFACTOR_TODO_2.md, file #8) into several partial/helper
# files in the same directory. Glob and concatenate every matching file so
# this check still covers the full implementation regardless of which file
# each member lives in.
_APPLICATION_DIR = ROOT / "Assets/ReachyMini/Runtime/Application"
ACCEPTANCE_TEXT = "".join(
    path.read_text(encoding="utf-8")
    for path in sorted(
        {
            *_APPLICATION_DIR.glob("ReachyRma135ResourceGovernorAcceptance*.cs"),
            *_APPLICATION_DIR.glob("Rma135Acceptance*.cs"),
        }
    )
)
RUNNER = ROOT / "scripts/run_rma135_resource_governor_acceptance_android.sh"


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        raise AssertionError(f"forbidden {label}: {needle}")


def main() -> None:
    text = ACCEPTANCE_TEXT
    runner = RUNNER.read_text(encoding="utf-8")
    require(text, "ReachyProductionAuthoritativeRuntime", "live production runtime")
    require(
        text,
        "FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>",
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
    require(
        text,
        "post_load_stabilization_exhausted",
        "post-load stabilization failure diagnostics",
    )
    require(
        text,
        "last_real_physics_state",
        "last real physics sample in failure diagnostics",
    )
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
    require(text, "LastObservedRealState", "last real physics sample retention")
    require(text, "replayVerifiedPassThrough", "verified preflight replay")
    require(text, "injectNextLiveCapture", "monitor-only controlled injection")
    require(
        text,
        "TimeSpan.FromMilliseconds(800.0)",
        "post-load settling sample interval",
    )
    require(text, "postLoadSettleSpacingEnabled = true", "post-load settling enabled")
    require(text, "postLoadSettleSpacingEnabled = false", "monitor cadence restoration")
    require(
        text,
        "Task.Delay(PostLoadSettleSampleInterval).GetAwaiter().GetResult()",
        "acceptance-only post-load settling wait",
    )
    require(
        text,
        "LastObservedRealState != LocalLlmPhysicsBudgetState.Healthy &&",
        "admissible replay guard",
    )
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
