#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.AppState
{
    public enum ReachyServiceKind
    {
        Simulation = 0,
        Camera = 1,
        Audio = 2,
        Provider = 3,
        Perception = 4,
        Behavior = 5,
        Persistence = 6,
        UserInterface = 7,
    }

    public enum ReachyServiceCriticality
    {
        Required = 0,
        Optional = 1,
    }

    public enum ReachyServiceState
    {
        Created = 0,
        Initializing = 1,
        Ready = 2,
        Degraded = 3,
        Unavailable = 4,
        Faulted = 5,
        Disposing = 6,
        Disposed = 7,
    }

    public enum ReachyApplicationState
    {
        Created = 0,
        Constructing = 1,
        Initializing = 2,
        Ready = 3,
        Degraded = 4,
        Faulted = 5,
        Disposing = 6,
        Disposed = 7,
    }

    public sealed class ReachyServiceHealth
    {
        public ReachyServiceHealth(
            string serviceId,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            ReachyServiceState state,
            string message,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                throw new ArgumentException(
                    "A service health record requires a non-empty service identifier.",
                    nameof(serviceId));
            }

            ServiceId = serviceId;
            Kind = kind;
            Criticality = criticality;
            State = state;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Revision = revision;
        }

        public string ServiceId { get; }

        public ReachyServiceKind Kind { get; }

        public ReachyServiceCriticality Criticality { get; }

        public ReachyServiceState State { get; }

        public string Message { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyServiceHealthChangedEventArgs : EventArgs
    {
        public ReachyServiceHealthChangedEventArgs(ReachyServiceHealth health)
        {
            Health = health ?? throw new ArgumentNullException(nameof(health));
        }

        public ReachyServiceHealth Health { get; }
    }

    public sealed class ReachyApplicationHealthSnapshot
    {
        private readonly ReachyServiceHealth[] services;

        public ReachyApplicationHealthSnapshot(
            ReachyApplicationState state,
            string message,
            ulong revision,
            IReadOnlyList<ReachyServiceHealth> serviceHealth)
        {
            State = state;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Revision = revision;
            if (serviceHealth == null)
            {
                throw new ArgumentNullException(nameof(serviceHealth));
            }

            services = new ReachyServiceHealth[serviceHealth.Count];
            for (int index = 0; index < serviceHealth.Count; ++index)
            {
                services[index] = serviceHealth[index] ??
                    throw new ArgumentException(
                        "Application health cannot contain a null service record.",
                        nameof(serviceHealth));
            }
        }

        public ReachyApplicationState State { get; }

        public string Message { get; }

        public ulong Revision { get; }

        public IReadOnlyList<ReachyServiceHealth> Services =>
            Array.AsReadOnly(services);
    }

    public sealed class ReachyApplicationHealthChangedEventArgs : EventArgs
    {
        public ReachyApplicationHealthChangedEventArgs(
            ReachyApplicationHealthSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyApplicationHealthSnapshot Snapshot { get; }
    }

    public interface IReachyApplicationService : IDisposable
    {
        string ServiceId { get; }

        ReachyServiceKind Kind { get; }

        ReachyServiceCriticality Criticality { get; }

        ReachyServiceHealth Health { get; }

        event EventHandler<ReachyServiceHealthChangedEventArgs>? HealthChanged;

        void Initialize();
    }

    public interface IReachySimulationService : IReachyApplicationService
    {
    }

    public interface IReachyCameraService : IReachyApplicationService
    {
    }

    public interface IReachyAudioService : IReachyApplicationService
    {
    }

    // RMA-195 phase B: the execution state a provider service is currently
    // in, surfaced to the HUD/diagnostics the same way
    // ReachyBehaviorServiceExecutionState answers "why isn't Reachy
    // moving" for behavior. NotLoaded/Loading/Ready/Generating mirror
    // LocalLlmProviderState (ReachyLocalLlmContracts.cs); Suspended
    // reflects the RMA-135 resource governor holding inference back rather
    // than a fault.
    public enum ReachyProviderServiceExecutionState
    {
        NotLoaded = 0,
        Loading = 1,
        Ready = 2,
        Generating = 3,
        Suspended = 4,
        Faulted = 5,
    }

    public sealed class ReachyProviderServiceSnapshot
    {
        public ReachyProviderServiceSnapshot(
            ReachyProviderServiceExecutionState executionState,
            string activeModelId,
            string statusMessage,
            ulong revision)
        {
            ExecutionState = executionState;
            ActiveModelId = activeModelId ??
                throw new ArgumentNullException(nameof(activeModelId));
            StatusMessage = statusMessage ??
                throw new ArgumentNullException(nameof(statusMessage));
            Revision = revision;
        }

        public ReachyProviderServiceExecutionState ExecutionState { get; }

        // Empty when no model is loaded (NotLoaded/Loading/Faulted).
        public string ActiveModelId { get; }

        public string StatusMessage { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyProviderServiceSnapshotChangedEventArgs : EventArgs
    {
        public ReachyProviderServiceSnapshotChangedEventArgs(
            ReachyProviderServiceSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyProviderServiceSnapshot Snapshot { get; }
    }

    // Deliberately generic across every ReachyProviderKind (ASR/TTS/LLM/VLM),
    // matching the single ReachyServiceKind.Provider boundary they all share
    // -- ProviderSnapshot/ProviderSnapshotChanged answer only "what is this
    // provider service doing right now". Kind-specific capabilities (e.g.
    // local-LLM text generation) are exposed through separate, optional
    // capability interfaces implementers can be `as`-cast to
    // (ILocalLlmProviderCapability, IReachyProviderGovernorDiagnosticsSource
    // in ReachyProviderGovernorDiagnostics.cs), not folded in here.
    //
    // Named "ProviderSnapshot", not the bare "Snapshot" IReachyBehaviorService
    // uses, because several test doubles in this codebase deliberately
    // implement every IReachy*Service marker interface on one class to
    // exercise the full 8-kind composition; a same-named "Snapshot" member
    // on two of those interfaces (different return types) would force
    // explicit interface implementation everywhere such a double exists.
    public interface IReachyProviderService : IReachyApplicationService
    {
        ReachyProviderServiceSnapshot ProviderSnapshot { get; }

        event EventHandler<ReachyProviderServiceSnapshotChangedEventArgs>?
            ProviderSnapshotChanged;
    }

    // RMA-195 phase C: the execution state a perception service is currently
    // in, surfaced to the HUD/diagnostics the same way
    // ReachyBehaviorServiceExecutionState/ReachyProviderServiceExecutionState
    // already answer "why isn't behavior/the provider doing anything" for
    // their own services.
    public enum ReachyPerceptionServiceExecutionState
    {
        NoCameraFrame = 0,
        NoCalibration = 1,
        Tracking = 2,
        Suspended = 3,
        Faulted = 4,
    }

    public sealed class ReachyPerceptionServiceSnapshot
    {
        public ReachyPerceptionServiceSnapshot(
            ReachyPerceptionServiceExecutionState executionState,
            WorldModelSnapshot? worldSnapshot,
            string statusMessage,
            ulong revision)
        {
            ExecutionState = executionState;
            WorldSnapshot = worldSnapshot;
            StatusMessage = statusMessage ??
                throw new ArgumentNullException(nameof(statusMessage));
            Revision = revision;
        }

        public ReachyPerceptionServiceExecutionState ExecutionState { get; }

        // Null unless ExecutionState is Tracking. Handed out verbatim (not a
        // simplified projection) because ReachyDeterministicBehaviorPlanner /
        // ReachyBaselineBehaviorLibrary already accept a raw WorldModelSnapshot?
        // directly as their worldSnapshot parameter -- that consumption
        // contract is already fixed to this exact type.
        public WorldModelSnapshot? WorldSnapshot { get; }

        public string StatusMessage { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyPerceptionServiceSnapshotChangedEventArgs : EventArgs
    {
        public ReachyPerceptionServiceSnapshotChangedEventArgs(
            ReachyPerceptionServiceSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyPerceptionServiceSnapshot Snapshot { get; }
    }

    // Named "PerceptionSnapshot", not the bare "Snapshot" IReachyBehaviorService
    // uses, for the same reason IReachyProviderService's members are named
    // "ProviderSnapshot" (see that interface's comment above): several test
    // doubles in this codebase implement every IReachy*Service marker on one
    // class, and a same-named "Snapshot" member on two of those interfaces
    // (different return types) would force explicit interface implementation
    // everywhere such a double exists.
    public interface IReachyPerceptionService : IReachyApplicationService
    {
        ReachyPerceptionServiceSnapshot PerceptionSnapshot { get; }

        event EventHandler<ReachyPerceptionServiceSnapshotChangedEventArgs>?
            PerceptionSnapshotChanged;
    }

    // RMA-195 phase A: the execution state a behavior service is currently in,
    // surfaced to the HUD/diagnostics so "why isn't Reachy moving" is always
    // answerable rather than silently stuck.
    public enum ReachyBehaviorServiceExecutionState
    {
        Idle = 0,
        ExecutingGesture = 1,
        SafetyBlocked = 2,
        Paused = 3,
    }

    public sealed class ReachyBehaviorServiceSnapshot
    {
        public ReachyBehaviorServiceSnapshot(
            ReachyBehaviorServiceExecutionState executionState,
            ReachyBaselineBehaviorKind currentBehavior,
            string statusMessage,
            ulong revision)
        {
            ExecutionState = executionState;
            CurrentBehavior = currentBehavior;
            StatusMessage = statusMessage ??
                throw new ArgumentNullException(nameof(statusMessage));
            Revision = revision;
        }

        public ReachyBehaviorServiceExecutionState ExecutionState { get; }

        public ReachyBaselineBehaviorKind CurrentBehavior { get; }

        public string StatusMessage { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyBehaviorServiceSnapshotChangedEventArgs : EventArgs
    {
        public ReachyBehaviorServiceSnapshotChangedEventArgs(
            ReachyBehaviorServiceSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyBehaviorServiceSnapshot Snapshot { get; }
    }

    public interface IReachyBehaviorService : IReachyApplicationService
    {
        ReachyBehaviorServiceSnapshot Snapshot { get; }

        event EventHandler<ReachyBehaviorServiceSnapshotChangedEventArgs>? SnapshotChanged;

        // Requests a baseline gesture/pose by kind. Only kinds with a
        // zero-argument request factory that needs neither perception (gaze
        // targets, RMA-195 phase C) nor a provider-supplied drive signal
        // (speech energy, phase D) are accepted; everything else fails closed
        // with a diagnostic code rather than silently no-op'ing.
        bool TryTriggerGesture(
            ReachyBaselineBehaviorKind gesture,
            out string diagnosticCode);
    }

    public interface IReachyPersistenceService : IReachyApplicationService
    {
    }

    public interface IReachyUserInterfaceService : IReachyApplicationService
    {
    }
}
