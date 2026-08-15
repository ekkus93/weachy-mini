from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmResourceGovernor.cs"
ANDROID = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyAndroidLocalLlmResourceSignalSource.cs"
)
PHYSICS = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachyLocalLlmPhysicsBudgetTracker.cs"
COORDINATOR = (
    ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmGovernedGenerationCoordinator.cs"
)
SIM_SOURCE = (
    ROOT
    / "Assets/ReachyMini/Runtime/Core/Application/ReachySimulationLocalLlmPhysicsBudgetSource.cs"
)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        raise AssertionError(f"forbidden {label}: {needle}")


def main() -> None:
    core = CORE.read_text(encoding="utf-8")
    android = ANDROID.read_text(encoding="utf-8")
    physics = PHYSICS.read_text(encoding="utf-8")
    coordinator = COORDINATOR.read_text(encoding="utf-8")
    sim_source = SIM_SOURCE.read_text(encoding="utf-8")

    require(core, "LocalLlmGovernorMode.Suspended", "explicit suspension state")
    require(core, "PhysicsBudgetExceeded", "physics-priority reason")
    require(core, "RecentOutOfMemory", "OOM signal")
    require(core, "RecordOutOfMemory", "explicit OOM latch")
    require(core, "OutOfMemoryLatched", "OOM latch diagnostics")
    require(core, "RecoverySamplesRequired = 3", "recovery hysteresis")
    require(core, "case LocalLlmPhysicsBudgetState.Unavailable", "missing-physics branch")
    require(core, "DeviceProfileLimit", "device-profile diagnostics")
    require(core, "ThermalSignalUnavailable", "thermal observability gap")
    require(core, "MemorySignalUnavailable", "memory observability gap")
    require(core, "PhysicsSignalUnavailable", "physics observability gap")
    require(core, "ProfileIncompatible", "fail-closed profile incompatibility")

    require(android, '"android.app.ActivityManager$MemoryInfo"', "ActivityManager memory signal")
    require(android, 'GetStatic<int>("SDK_INT")', "Android API detection")
    require(android, 'Call<int>("getCurrentThermalStatus")', "PowerManager thermal signal")
    require(android, "if (apiLevel < 29)", "API-29 thermal boundary")
    require(android, "LocalLlmThermalStatus.Unavailable", "explicit old-API thermal state")
    require(android, "ownerManagedThreadId", "Android JNI owner-thread identity")
    require(
        android,
        "Environment.CurrentManagedThreadId != ownerManagedThreadId",
        "custom-thread JNI detection",
    )
    require(android, "AndroidJNI.AttachCurrentThread()", "custom-thread JVM attachment")
    require(android, "AndroidJNI.DetachCurrentThread()", "custom-thread JVM detachment")
    require(android, "throw new InvalidOperationException", "fail-visible Android bridge errors")

    require(physics, "DeadlineMissCount", "authoritative deadline-miss input")
    require(physics, "AccumulatedLagSeconds", "authoritative lag input")
    require(physics, "LastStepDurationSeconds", "authoritative step-duration input")
    require(physics, "LocalLlmPhysicsBudgetState.Exceeded", "physics suspension trigger")
    require(physics, "newSteps == 0UL", "stalled-physics visibility")
    forbid(
        physics,
        "currentSnapshot.AccumulatedLagSeconds < previousSnapshot.AccumulatedLagSeconds",
        "scheduler-lag gauge treated as monotonic counter",
    )
    forbid(physics, "Thread.Sleep", "physics-governor sleep fallback")

    require(coordinator, "ResourceSuspendedBeforeStart", "preflight suspension")
    require(coordinator, "ResourceCancelledDuringGeneration", "active resource cancellation")
    require(coordinator, "ResourceExhausted", "typed OOM propagation")
    require(coordinator, "SignalFailure", "signal failure visibility")
    require(coordinator, "CompareExchange", "no hidden coordinator queue")
    require(coordinator, "explicit provider recreation", "explicit profile-change boundary")
    require(coordinator, "ProfileFitsWithin", "loaded-profile safety comparison")
    forbid(coordinator, "ResetConversation()", "resource cancellation via conversation reset")

    require(sim_source, "IReachySimulationTimingSource", "read-only simulation timing contract")
    require(sim_source, "ReachySimulationRunState.Running", "running-simulation requirement")
    require(
        sim_source,
        "TryGetLatestTimingSnapshot",
        "authoritative simulation timing snapshot source",
    )
    require(sim_source, "tracker.Reset()", "stale physics reset")

    print("RMA-135 static resource-governor contracts passed.")


if __name__ == "__main__":
    main()
