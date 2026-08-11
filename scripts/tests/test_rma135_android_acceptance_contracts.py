from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ACCEPTANCE = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.cs"
)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        raise AssertionError(f"forbidden {label}: {needle}")


def main() -> None:
    text = ACCEPTANCE.read_text(encoding="utf-8")
    require(text, "ReachySimulationWorker", "production simulation worker")
    require(text, "ReachySimAuthoritativeStateReader", "production authoritative state reader")
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
        "disposed by the worker owner",
        "single simulation worker ownership checkpoint",
    )
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
