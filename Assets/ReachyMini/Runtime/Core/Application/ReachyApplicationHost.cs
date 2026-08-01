#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public sealed class ReachyApplicationHost : IDisposable
    {
        private readonly object sync = new object();
        private readonly ReachyApplicationComposition composition;
        private readonly Dictionary<ReachyServiceKind, IReachyApplicationService>
            services = new Dictionary<ReachyServiceKind, IReachyApplicationService>();
        private readonly List<IReachyApplicationService> constructionOrder =
            new List<IReachyApplicationService>();
        private ReachyApplicationHealthSnapshot snapshot;
        private ReachyApplicationState lifecycleState;
        private string lifecycleMessage;
        private ulong revision;
        private bool startEntered;
        private bool disposeEntered;

        public ReachyApplicationHost(ReachyApplicationComposition composition)
        {
            this.composition = composition ??
                throw new ArgumentNullException(nameof(composition));
            lifecycleState = ReachyApplicationState.Created;
            lifecycleMessage = "Application services have not been constructed.";
            snapshot = CreateSnapshotLocked();
        }

        public event EventHandler<ReachyApplicationHealthChangedEventArgs>?
            HealthChanged;

        public ReachyApplicationHealthSnapshot Health
        {
            get
            {
                lock (sync)
                {
                    return snapshot;
                }
            }
        }

        public void Start()
        {
            lock (sync)
            {
                if (disposeEntered)
                {
                    throw new ObjectDisposedException(nameof(ReachyApplicationHost));
                }
                if (startEntered)
                {
                    throw new InvalidOperationException(
                        "The application host cannot be started more than once.");
                }
                startEntered = true;
            }

            PublishLifecycle(
                ReachyApplicationState.Constructing,
                "Application services are being constructed.");
            try
            {
                ConstructServices();
                PublishLifecycle(
                    ReachyApplicationState.Initializing,
                    "Application services are being initialized.");
                InitializeServices();
                RecomputeOperationalHealth();
            }
            catch (Exception exception)
            {
                string rollback = DisposeConstructedServices();
                string message = $"Application startup failed: {exception.Message}";
                if (!string.IsNullOrEmpty(rollback))
                {
                    message = $"{message} Rollback failures: {rollback}";
                }
                PublishLifecycle(ReachyApplicationState.Faulted, message);
                throw new InvalidOperationException(message, exception);
            }
        }

        public IReachyApplicationService GetRequiredService(ReachyServiceKind kind)
        {
            lock (sync)
            {
                if (!services.TryGetValue(kind, out IReachyApplicationService? service))
                {
                    throw new InvalidOperationException(
                        $"Application service '{kind}' has not been constructed.");
                }
                return service;
            }
        }

        public TService GetRequiredService<TService>(ReachyServiceKind kind)
            where TService : class, IReachyApplicationService
        {
            IReachyApplicationService service = GetRequiredService(kind);
            return service as TService ??
                throw new InvalidOperationException(
                    $"Application service '{kind}' does not implement '{typeof(TService).FullName}'.");
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposeEntered)
                {
                    return;
                }
                disposeEntered = true;
            }

            PublishLifecycle(
                ReachyApplicationState.Disposing,
                "Application services are being disposed.");
            string failures = DisposeConstructedServices();
            PublishLifecycle(
                ReachyApplicationState.Disposed,
                string.IsNullOrEmpty(failures)
                    ? "Application services disposed successfully."
                    : $"Application services were disposed with failures: {failures}");
            GC.SuppressFinalize(this);
        }

        private void ConstructServices()
        {
            IReadOnlyList<ReachyServiceRegistration> registrations =
                composition.OrderedRegistrations;
            for (int index = 0; index < registrations.Count; ++index)
            {
                ReachyServiceRegistration registration = registrations[index];
                var resolver = new ConstructionResolver(
                    services,
                    registration.Dependencies,
                    registration.ServiceId);
                IReachyApplicationService service =
                    registration.Factory(resolver) ??
                    throw new InvalidOperationException(
                        $"Factory for '{registration.ServiceId}' returned null.");
                try
                {
                    ReachyApplicationComposition.ValidateServiceContract(
                        registration,
                        service);
                }
                catch (Exception contractException)
                {
                    string disposalFailure = DisposeRejectedService(service);
                    string message =
                        $"Factory result for '{registration.ServiceId}' was rejected: " +
                        contractException.Message;
                    if (!string.IsNullOrEmpty(disposalFailure))
                    {
                        message = $"{message} Rejected-service disposal failed: {disposalFailure}";
                    }
                    throw new InvalidOperationException(message, contractException);
                }

                service.HealthChanged += OnServiceHealthChanged;
                services.Add(registration.Kind, service);
                constructionOrder.Add(service);
            }
            PublishSnapshot();
        }

        private void InitializeServices()
        {
            for (int index = 0; index < constructionOrder.Count; ++index)
            {
                IReachyApplicationService service = constructionOrder[index];
                service.Initialize();
                ReachyServiceState state = service.Health.State;
                if (state != ReachyServiceState.Ready &&
                    state != ReachyServiceState.Degraded &&
                    state != ReachyServiceState.Unavailable &&
                    state != ReachyServiceState.Faulted)
                {
                    throw new InvalidOperationException(
                        $"Service '{service.ServiceId}' completed initialization in invalid state '{state}'.");
                }
                if (state == ReachyServiceState.Faulted)
                {
                    throw new InvalidOperationException(
                        $"Service '{service.ServiceId}' faulted during initialization: {service.Health.Message}");
                }
                if (state == ReachyServiceState.Unavailable &&
                    service.Criticality == ReachyServiceCriticality.Required)
                {
                    throw new InvalidOperationException(
                        $"Required service '{service.ServiceId}' is unavailable: {service.Health.Message}");
                }
            }
        }

        private void OnServiceHealthChanged(
            object? sender,
            ReachyServiceHealthChangedEventArgs eventArgs)
        {
            if (eventArgs == null)
            {
                throw new ArgumentNullException(nameof(eventArgs));
            }

            ReachyApplicationState current;
            lock (sync)
            {
                current = lifecycleState;
            }
            if (current == ReachyApplicationState.Ready ||
                current == ReachyApplicationState.Degraded ||
                current == ReachyApplicationState.Faulted)
            {
                RecomputeOperationalHealth();
            }
            else
            {
                PublishSnapshot();
            }
        }

        private void RecomputeOperationalHealth()
        {
            ReachyApplicationState state = ReachyApplicationState.Ready;
            string message = "All required application services are ready.";
            lock (sync)
            {
                for (int index = 0; index < constructionOrder.Count; ++index)
                {
                    ReachyServiceHealth health = constructionOrder[index].Health;
                    if ((health.State == ReachyServiceState.Faulted ||
                         health.State == ReachyServiceState.Unavailable ||
                         health.State == ReachyServiceState.Disposed) &&
                        health.Criticality == ReachyServiceCriticality.Required)
                    {
                        state = ReachyApplicationState.Faulted;
                        message =
                            $"Required service '{health.ServiceId}' is {health.State}: {health.Message}";
                        break;
                    }
                    if (health.State == ReachyServiceState.Faulted ||
                        health.State == ReachyServiceState.Unavailable ||
                        health.State == ReachyServiceState.Degraded)
                    {
                        state = ReachyApplicationState.Degraded;
                        message =
                            $"Application capability '{health.ServiceId}' is {health.State}: {health.Message}";
                    }
                    else if (health.State != ReachyServiceState.Ready)
                    {
                        state = ReachyApplicationState.Faulted;
                        message =
                            $"Service '{health.ServiceId}' entered unexpected operational state '{health.State}'.";
                        break;
                    }
                }
                lifecycleState = state;
                lifecycleMessage = message;
            }
            PublishSnapshot();
        }

        private string DisposeConstructedServices()
        {
            var failures = new List<string>();
            for (int index = constructionOrder.Count - 1; index >= 0; --index)
            {
                IReachyApplicationService service = constructionOrder[index];
                try
                {
                    service.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add($"{service.ServiceId}: {exception.Message}");
                }
                finally
                {
                    service.HealthChanged -= OnServiceHealthChanged;
                }
            }
            constructionOrder.Clear();
            services.Clear();
            return string.Join(" | ", failures);
        }

        private static string DisposeRejectedService(
            IReachyApplicationService service)
        {
            try
            {
                service.Dispose();
                return string.Empty;
            }
            catch (Exception exception)
            {
                return $"{service.ServiceId}: {exception.Message}";
            }
        }

        private void PublishLifecycle(
            ReachyApplicationState state,
            string message)
        {
            lock (sync)
            {
                lifecycleState = state;
                lifecycleMessage = message;
            }
            PublishSnapshot();
        }

        private void PublishSnapshot()
        {
            ReachyApplicationHealthSnapshot next;
            EventHandler<ReachyApplicationHealthChangedEventArgs>? handler;
            lock (sync)
            {
                revision = checked(revision + 1UL);
                next = CreateSnapshotLocked();
                snapshot = next;
                handler = HealthChanged;
            }
            handler?.Invoke(
                this,
                new ReachyApplicationHealthChangedEventArgs(next));
        }

        private ReachyApplicationHealthSnapshot CreateSnapshotLocked()
        {
            var health = new List<ReachyServiceHealth>(constructionOrder.Count);
            for (int index = 0; index < constructionOrder.Count; ++index)
            {
                health.Add(constructionOrder[index].Health);
            }
            health.Sort((left, right) => left.Kind.CompareTo(right.Kind));
            return new ReachyApplicationHealthSnapshot(
                lifecycleState,
                lifecycleMessage,
                revision,
                health);
        }

        private sealed class ConstructionResolver : IReachyServiceResolver
        {
            private readonly Dictionary<
                ReachyServiceKind,
                IReachyApplicationService> services;
            private readonly HashSet<ReachyServiceKind> allowed;
            private readonly string ownerServiceId;

            public ConstructionResolver(
                Dictionary<ReachyServiceKind, IReachyApplicationService> services,
                IReadOnlyList<ReachyServiceKind> dependencies,
                string ownerServiceId)
            {
                this.services = services ??
                    throw new ArgumentNullException(nameof(services));
                if (dependencies == null)
                {
                    throw new ArgumentNullException(nameof(dependencies));
                }
                this.ownerServiceId = ownerServiceId ??
                    throw new ArgumentNullException(nameof(ownerServiceId));
                allowed = new HashSet<ReachyServiceKind>();
                for (int index = 0; index < dependencies.Count; ++index)
                {
                    allowed.Add(dependencies[index]);
                }
            }

            public IReachyApplicationService GetRequired(ReachyServiceKind kind)
            {
                if (!allowed.Contains(kind))
                {
                    throw new InvalidOperationException(
                        $"Factory for '{ownerServiceId}' requested undeclared dependency '{kind}'.");
                }
                if (!services.TryGetValue(
                        kind,
                        out IReachyApplicationService? service))
                {
                    throw new InvalidOperationException(
                        $"Declared dependency '{kind}' for '{ownerServiceId}' has not been constructed.");
                }
                return service;
            }

            public TService GetRequired<TService>(ReachyServiceKind kind)
                where TService : class, IReachyApplicationService
            {
                IReachyApplicationService service = GetRequired(kind);
                return service as TService ??
                    throw new InvalidOperationException(
                        $"Dependency '{kind}' for '{ownerServiceId}' does not implement '{typeof(TService).FullName}'.");
            }
        }
    }
}
