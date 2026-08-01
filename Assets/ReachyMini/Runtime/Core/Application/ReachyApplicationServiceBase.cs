#nullable enable

using System;

namespace ReachyMini.Application
{
    public abstract class ReachyApplicationServiceBase : IReachyApplicationService
    {
        private readonly object sync = new object();
        private ReachyServiceHealth health;
        private bool initializeEntered;
        private bool disposeEntered;

        protected ReachyApplicationServiceBase(
            string serviceId,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                throw new ArgumentException(
                    "A service requires a non-empty identifier.",
                    nameof(serviceId));
            }

            ServiceId = serviceId;
            Kind = kind;
            Criticality = criticality;
            health = new ReachyServiceHealth(
                serviceId,
                kind,
                criticality,
                ReachyServiceState.Created,
                "Service has not been initialized.",
                0UL);
        }

        public string ServiceId { get; }

        public ReachyServiceKind Kind { get; }

        public ReachyServiceCriticality Criticality { get; }

        public ReachyServiceHealth Health
        {
            get
            {
                lock (sync)
                {
                    return health;
                }
            }
        }

        public event EventHandler<ReachyServiceHealthChangedEventArgs>? HealthChanged;

        public void Initialize()
        {
            lock (sync)
            {
                if (disposeEntered)
                {
                    throw new ObjectDisposedException(ServiceId);
                }
                if (initializeEntered)
                {
                    throw new InvalidOperationException(
                        $"Service '{ServiceId}' cannot be initialized more than once.");
                }
                initializeEntered = true;
            }

            Publish(
                ReachyServiceState.Initializing,
                "Service initialization is in progress.");
            try
            {
                OnInitialize();
                if (Health.State == ReachyServiceState.Initializing)
                {
                    SetReady("Service initialized successfully.");
                }
            }
            catch (Exception exception)
            {
                SetFaulted($"Initialization failed: {exception.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            lock (sync)
            {
                if (disposeEntered)
                {
                    return;
                }
                disposeEntered = true;
            }

            Publish(
                ReachyServiceState.Disposing,
                "Service disposal is in progress.");
            try
            {
                OnDispose();
                Publish(
                    ReachyServiceState.Disposed,
                    "Service disposed successfully.");
            }
            catch (Exception exception)
            {
                SetFaulted($"Disposal failed: {exception.Message}");
                throw;
            }
        }

        protected abstract void OnInitialize();

        protected virtual void OnDispose()
        {
        }

        protected void SetReady(string message)
        {
            Publish(ReachyServiceState.Ready, message);
        }

        protected void SetDegraded(string message)
        {
            Publish(ReachyServiceState.Degraded, message);
        }

        protected void SetUnavailable(string message)
        {
            Publish(ReachyServiceState.Unavailable, message);
        }

        protected void SetFaulted(string message)
        {
            Publish(ReachyServiceState.Faulted, message);
        }

        private void Publish(ReachyServiceState state, string message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            ReachyServiceHealth next;
            EventHandler<ReachyServiceHealthChangedEventArgs>? handler;
            lock (sync)
            {
                next = new ReachyServiceHealth(
                    ServiceId,
                    Kind,
                    Criticality,
                    state,
                    message,
                    checked(health.Revision + 1UL));
                health = next;
                handler = HealthChanged;
            }
            handler?.Invoke(this, new ReachyServiceHealthChangedEventArgs(next));
        }
    }
}
