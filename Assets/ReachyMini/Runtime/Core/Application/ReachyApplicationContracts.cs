#nullable enable

using System;
using System.Collections.Generic;

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

    public interface IReachyBehaviorService : IReachyApplicationService
    {
    }

    public interface IReachyPersistenceService : IReachyApplicationService
    {
    }

    public interface IReachyUserInterfaceService : IReachyApplicationService
    {
    }
}
