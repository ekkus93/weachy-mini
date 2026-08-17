#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Behavior;

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

    public interface IReachyProviderService : IReachyApplicationService
    {
    }

    public interface IReachyPerceptionService : IReachyApplicationService
    {
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
