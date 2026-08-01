#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.AppState;

namespace ReachyMini.Application.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            CompleteCompositionUsesDependencyOrder();
            InvalidGraphsFailBeforeConstruction();
            UndeclaredDependencyFaultsAndRollsBack();
            FactoryContractMismatchDisposesRejectedService();
            InitializationFailureRollsBackAllServices();
            DisposalIsReverseOrderedIdempotentAndExhaustive();
            HealthAggregationPublishesImmutableSnapshots();
            ServiceBaseEnforcesOneShotInitialization();
            Console.WriteLine("RMA-080 application state architecture tests passed.");
            return 0;
        }

        private static void CompleteCompositionUsesDependencyOrder()
        {
            var events = new List<string>();
            using var host = new ReachyApplicationHost(BuildComposition(events));
            host.Start();

            Equal(ReachyApplicationState.Ready, host.Health.State, "ready host");
            Equal(8, host.Health.Services.Count, "service count");
            Before(events, "construct:persistence", "construct:provider");
            Before(events, "construct:provider", "construct:behavior");
            Before(events, "construct:behavior", "construct:ui");
            Before(events, "initialize:persistence", "initialize:provider");
            Before(events, "initialize:behavior", "initialize:ui");
            Equal(
                "simulation",
                host.GetRequiredService<IReachySimulationService>(
                    ReachyServiceKind.Simulation).ServiceId,
                "typed resolution");
        }

        private static void InvalidGraphsFailBeforeConstruction()
        {
            var events = new List<string>();
            var missing = new List<ReachyServiceRegistration>(
                BuildRegistrations(events));
            missing.RemoveAt(missing.Count - 1);
            Throws<ArgumentException>(
                () => ReachyApplicationComposition.CreateComplete(missing),
                "missing boundary");
            Equal(0, events.Count, "missing boundary constructs nothing");

            ReachyServiceRegistration[] duplicate = BuildRegistrations(events);
            duplicate[1] = Register(
                "second-simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Optional,
                Array.Empty<ReachyServiceKind>(),
                resolver => new SimulationService(
                    "second-simulation",
                    ReachyServiceCriticality.Optional,
                    events));
            Throws<ArgumentException>(
                () => ReachyApplicationComposition.CreateComplete(duplicate),
                "duplicate kind");

            ReachyServiceRegistration[] cycle = BuildRegistrations(events);
            cycle[0] = Register(
                "simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required,
                new[] { ReachyServiceKind.UserInterface },
                resolver => new SimulationService(
                    "simulation",
                    ReachyServiceCriticality.Required,
                    events));
            Throws<ArgumentException>(
                () => ReachyApplicationComposition.CreateComplete(cycle),
                "dependency cycle");
        }

        private static void UndeclaredDependencyFaultsAndRollsBack()
        {
            var events = new List<string>();
            ReachyServiceRegistration[] registrations = BuildRegistrations(events);
            registrations[1] = Register(
                "camera",
                ReachyServiceKind.Camera,
                ReachyServiceCriticality.Optional,
                Array.Empty<ReachyServiceKind>(),
                resolver =>
                {
                    resolver.GetRequired(ReachyServiceKind.Simulation);
                    return new CameraService("camera", events);
                });

            using var host = new ReachyApplicationHost(
                ReachyApplicationComposition.CreateComplete(registrations));
            Throws<InvalidOperationException>(host.Start, "undeclared dependency");
            Equal(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "undeclared dependency health");
            Contains(events, "dispose:simulation", "construction rollback");
        }

        private static void FactoryContractMismatchDisposesRejectedService()
        {
            var events = new List<string>();
            ReachyServiceRegistration[] registrations = BuildRegistrations(events);
            registrations[0] = Register(
                "simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required,
                Array.Empty<ReachyServiceKind>(),
                resolver => new CameraService(
                    "simulation",
                    events,
                    ReachyServiceCriticality.Required));

            using var host = new ReachyApplicationHost(
                ReachyApplicationComposition.CreateComplete(registrations));
            Throws<InvalidOperationException>(host.Start, "factory marker mismatch");
            Contains(
                events,
                "dispose:simulation",
                "rejected factory service disposal");
            Equal(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "factory mismatch health");
        }

        private static void InitializationFailureRollsBackAllServices()
        {
            var events = new List<string>();
            ReachyServiceRegistration[] registrations = BuildRegistrations(events);
            registrations[3] = Register(
                "provider",
                ReachyServiceKind.Provider,
                ReachyServiceCriticality.Optional,
                new[] { ReachyServiceKind.Persistence },
                resolver => new ProviderService(
                    "provider",
                    events,
                    failInitialization: true));

            using var host = new ReachyApplicationHost(
                ReachyApplicationComposition.CreateComplete(registrations));
            Throws<InvalidOperationException>(host.Start, "initialization failure");
            Equal(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "initialization failure health");
            Before(events, "dispose:ui", "dispose:simulation");
            Equal(8, Count(events, "dispose:"), "rollback disposal count");
        }

        private static void DisposalIsReverseOrderedIdempotentAndExhaustive()
        {
            var events = new List<string>();
            ReachyServiceRegistration[] registrations = BuildRegistrations(events);
            registrations[4] = Register(
                "perception",
                ReachyServiceKind.Perception,
                ReachyServiceCriticality.Optional,
                new[] { ReachyServiceKind.Camera },
                resolver => new PerceptionService(
                    "perception",
                    events,
                    failDisposal: true));

            var host = new ReachyApplicationHost(
                ReachyApplicationComposition.CreateComplete(registrations));
            host.Start();
            host.Dispose();
            host.Dispose();

            Equal(ReachyApplicationState.Disposed, host.Health.State, "disposed host");
            Before(events, "dispose:ui", "dispose:behavior");
            Before(events, "dispose:behavior", "dispose:perception");
            Before(events, "dispose:perception", "dispose:simulation");
            Equal(8, Count(events, "dispose:"), "idempotent exhaustive disposal");
            Contains(
                host.Health.Message,
                "perception",
                "disposal failure identity retained");
        }

        private static void HealthAggregationPublishesImmutableSnapshots()
        {
            var events = new List<string>();
            using var host = new ReachyApplicationHost(BuildComposition(events));
            host.Start();
            ReachyApplicationHealthSnapshot ready = host.Health;

            CameraService camera = host.GetRequiredService<CameraService>(
                ReachyServiceKind.Camera);
            camera.PublishUnavailable("Camera permission has not been granted.");
            ReachyApplicationHealthSnapshot degraded = host.Health;
            Equal(
                ReachyApplicationState.Degraded,
                degraded.State,
                "optional unavailable degradation");
            True(degraded.Revision > ready.Revision, "health revision advances");
            Equal(
                ReachyApplicationState.Ready,
                ready.State,
                "old application snapshot unchanged");
            Equal(
                ReachyServiceState.Ready,
                Find(ready, ReachyServiceKind.Camera).State,
                "old service snapshot unchanged");

            SimulationService simulation =
                host.GetRequiredService<SimulationService>(
                    ReachyServiceKind.Simulation);
            simulation.PublishFault("Authoritative simulation stopped.");
            Equal(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "required service fault");
            Contains(
                host.Health.Message,
                "Authoritative simulation stopped.",
                "required fault diagnostics");
        }

        private static void ServiceBaseEnforcesOneShotInitialization()
        {
            var events = new List<string>();
            var service = new CameraService("camera", events);
            service.Initialize();
            Throws<InvalidOperationException>(
                service.Initialize,
                "second initialization");
            service.Dispose();
            service.Dispose();
            Equal(1, Count(events, "initialize:"), "one initialization");
            Equal(1, Count(events, "dispose:"), "idempotent service disposal");
            Throws<ObjectDisposedException>(
                service.Initialize,
                "initialization after disposal");
        }

        private static ReachyApplicationComposition BuildComposition(
            List<string> events)
        {
            return ReachyApplicationComposition.CreateComplete(
                BuildRegistrations(events));
        }

        private static ReachyServiceRegistration[] BuildRegistrations(
            List<string> events)
        {
            return new[]
            {
                Register(
                    "simulation",
                    ReachyServiceKind.Simulation,
                    ReachyServiceCriticality.Required,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new SimulationService(
                        "simulation",
                        ReachyServiceCriticality.Required,
                        events)),
                Register(
                    "camera",
                    ReachyServiceKind.Camera,
                    ReachyServiceCriticality.Optional,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new CameraService("camera", events)),
                Register(
                    "audio",
                    ReachyServiceKind.Audio,
                    ReachyServiceCriticality.Optional,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new AudioService("audio", events)),
                Register(
                    "provider",
                    ReachyServiceKind.Provider,
                    ReachyServiceCriticality.Optional,
                    new[] { ReachyServiceKind.Persistence },
                    resolver =>
                    {
                        resolver.GetRequired<IReachyPersistenceService>(
                            ReachyServiceKind.Persistence);
                        return new ProviderService("provider", events);
                    }),
                Register(
                    "perception",
                    ReachyServiceKind.Perception,
                    ReachyServiceCriticality.Optional,
                    new[] { ReachyServiceKind.Camera },
                    resolver =>
                    {
                        resolver.GetRequired<IReachyCameraService>(
                            ReachyServiceKind.Camera);
                        return new PerceptionService("perception", events);
                    }),
                Register(
                    "behavior",
                    ReachyServiceKind.Behavior,
                    ReachyServiceCriticality.Optional,
                    new[]
                    {
                        ReachyServiceKind.Simulation,
                        ReachyServiceKind.Provider,
                        ReachyServiceKind.Perception,
                    },
                    resolver =>
                    {
                        resolver.GetRequired<IReachySimulationService>(
                            ReachyServiceKind.Simulation);
                        resolver.GetRequired<IReachyProviderService>(
                            ReachyServiceKind.Provider);
                        resolver.GetRequired<IReachyPerceptionService>(
                            ReachyServiceKind.Perception);
                        return new BehaviorService("behavior", events);
                    }),
                Register(
                    "persistence",
                    ReachyServiceKind.Persistence,
                    ReachyServiceCriticality.Required,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new PersistenceService("persistence", events)),
                Register(
                    "ui",
                    ReachyServiceKind.UserInterface,
                    ReachyServiceCriticality.Required,
                    new[]
                    {
                        ReachyServiceKind.Simulation,
                        ReachyServiceKind.Camera,
                        ReachyServiceKind.Audio,
                        ReachyServiceKind.Provider,
                        ReachyServiceKind.Perception,
                        ReachyServiceKind.Behavior,
                        ReachyServiceKind.Persistence,
                    },
                    resolver =>
                    {
                        resolver.GetRequired<IReachyBehaviorService>(
                            ReachyServiceKind.Behavior);
                        return new UserInterfaceService("ui", events);
                    }),
            };
        }

        private static ReachyServiceRegistration Register(
            string serviceId,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            ReachyServiceKind[] dependencies,
            Func<IReachyServiceResolver, IReachyApplicationService> factory)
        {
            return new ReachyServiceRegistration(
                serviceId,
                kind,
                criticality,
                dependencies,
                resolver =>
                {
                    IReachyApplicationService service = factory(resolver);
                    if (service is TestServiceBase testService)
                    {
                        testService.RecordConstruction();
                    }
                    return service;
                });
        }

        private static ReachyServiceHealth Find(
            ReachyApplicationHealthSnapshot snapshot,
            ReachyServiceKind kind)
        {
            for (int index = 0; index < snapshot.Services.Count; ++index)
            {
                if (snapshot.Services[index].Kind == kind)
                {
                    return snapshot.Services[index];
                }
            }
            throw new InvalidOperationException($"Missing health for '{kind}'.");
        }

        private static int Count(List<string> values, string prefix)
        {
            int count = 0;
            for (int index = 0; index < values.Count; ++index)
            {
                if (values[index].StartsWith(prefix, StringComparison.Ordinal))
                {
                    ++count;
                }
            }
            return count;
        }

        private static void Before(
            List<string> values,
            string earlier,
            string later)
        {
            int earlierIndex = values.IndexOf(earlier);
            int laterIndex = values.IndexOf(later);
            if (earlierIndex < 0 || laterIndex < 0 || earlierIndex >= laterIndex)
            {
                throw new InvalidOperationException(
                    $"Expected '{earlier}' before '{later}', found [{string.Join(", ", values)}].");
            }
        }

        private static void Contains(
            List<string> values,
            string expected,
            string label)
        {
            if (!values.Contains(expected))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: missing '{expected}'.");
            }
        }

        private static void Contains(string value, string expected, string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' lacks '{expected}'.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void Throws<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"Managed test failed for {label}: expected {typeof(TException).Name}.");
        }

        private abstract class TestServiceBase : ReachyApplicationServiceBase
        {
            private readonly List<string> events;
            private readonly bool failInitialization;
            private readonly bool failDisposal;

            protected TestServiceBase(
                string serviceId,
                ReachyServiceKind kind,
                ReachyServiceCriticality criticality,
                List<string> events,
                bool failInitialization = false,
                bool failDisposal = false)
                : base(serviceId, kind, criticality)
            {
                this.events = events;
                this.failInitialization = failInitialization;
                this.failDisposal = failDisposal;
            }

            public void RecordConstruction()
            {
                events.Add($"construct:{ServiceId}");
            }

            public void PublishUnavailable(string message)
            {
                SetUnavailable(message);
            }

            public void PublishFault(string message)
            {
                SetFaulted(message);
            }

            protected override void OnInitialize()
            {
                events.Add($"initialize:{ServiceId}");
                if (failInitialization)
                {
                    throw new InvalidOperationException(
                        $"Synthetic initialization failure for {ServiceId}.");
                }
            }

            protected override void OnDispose()
            {
                events.Add($"dispose:{ServiceId}");
                if (failDisposal)
                {
                    throw new InvalidOperationException(
                        $"Synthetic disposal failure for {ServiceId}.");
                }
            }
        }

        private sealed class SimulationService :
            TestServiceBase,
            IReachySimulationService
        {
            public SimulationService(
                string id,
                ReachyServiceCriticality criticality,
                List<string> events)
                : base(id, ReachyServiceKind.Simulation, criticality, events)
            {
            }
        }

        private sealed class CameraService : TestServiceBase, IReachyCameraService
        {
            public CameraService(
                string id,
                List<string> events,
                ReachyServiceCriticality criticality =
                    ReachyServiceCriticality.Optional)
                : base(id, ReachyServiceKind.Camera, criticality, events)
            {
            }
        }

        private sealed class AudioService : TestServiceBase, IReachyAudioService
        {
            public AudioService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Audio,
                    ReachyServiceCriticality.Optional,
                    events)
            {
            }
        }

        private sealed class ProviderService :
            TestServiceBase,
            IReachyProviderService
        {
            public ProviderService(
                string id,
                List<string> events,
                bool failInitialization = false)
                : base(
                    id,
                    ReachyServiceKind.Provider,
                    ReachyServiceCriticality.Optional,
                    events,
                    failInitialization)
            {
            }
        }

        private sealed class PerceptionService :
            TestServiceBase,
            IReachyPerceptionService
        {
            public PerceptionService(
                string id,
                List<string> events,
                bool failDisposal = false)
                : base(
                    id,
                    ReachyServiceKind.Perception,
                    ReachyServiceCriticality.Optional,
                    events,
                    failDisposal: failDisposal)
            {
            }
        }

        private sealed class BehaviorService :
            TestServiceBase,
            IReachyBehaviorService
        {
            public BehaviorService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Behavior,
                    ReachyServiceCriticality.Optional,
                    events)
            {
            }
        }

        private sealed class PersistenceService :
            TestServiceBase,
            IReachyPersistenceService
        {
            public PersistenceService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Persistence,
                    ReachyServiceCriticality.Required,
                    events)
            {
            }
        }

        private sealed class UserInterfaceService :
            TestServiceBase,
            IReachyUserInterfaceService
        {
            public UserInterfaceService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.UserInterface,
                    ReachyServiceCriticality.Required,
                    events)
            {
            }
        }
    }
}
