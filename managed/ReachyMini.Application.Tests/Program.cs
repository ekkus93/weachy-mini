#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Application;

namespace ReachyMini.Application.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            TestCompleteCompositionAndDependencyOrder();
            TestMissingBoundaryRejected();
            TestCycleRejected();
            TestUndeclaredDependencyRejected();
            TestFactoryContractMismatchRejected();
            TestInitializationFailureRollsBack();
            TestReverseDisposalAndIdempotence();
            TestHealthAggregationAndSnapshotIsolation();
            Console.WriteLine("RMA-080 application state architecture tests passed.");
            return 0;
        }

        private static void TestCompleteCompositionAndDependencyOrder()
        {
            var events = new List<string>();
            ReachyApplicationComposition composition = BuildComposition(events);
            using var host = new ReachyApplicationHost(composition);
            host.Start();

            AssertEqual(
                ReachyApplicationState.Ready,
                host.Health.State,
                "complete composition health");
            AssertEqual(8, host.Health.Services.Count, "complete service count");
            AssertBefore(events, "construct:simulation", "construct:behavior");
            AssertBefore(events, "initialize:simulation", "initialize:behavior");
            AssertBefore(events, "initialize:behavior", "initialize:ui");
            AssertEqual(
                "simulation",
                host.GetRequiredService<IReachySimulationService>(
                    ReachyServiceKind.Simulation).ServiceId,
                "typed simulation resolution");
        }

        private static void TestMissingBoundaryRejected()
        {
            var registrations = new List<ReachyServiceRegistration>(
                BuildRegistrations(new List<string>()));
            registrations.RemoveAt(registrations.Count - 1);
            AssertThrows<ArgumentException>(
                () => ReachyApplicationComposition.CreateComplete(registrations),
                "missing UI boundary");
        }

        private static void TestCycleRejected()
        {
            var events = new List<string>();
            var registrations = new List<ReachyServiceRegistration>(
                BuildRegistrations(events));
            registrations[0] = Registration(
                "simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required,
                new[] { ReachyServiceKind.UserInterface },
                resolver => new SimulationService("simulation", events));
            AssertThrows<ArgumentException>(
                () => ReachyApplicationComposition.CreateComplete(registrations),
                "dependency cycle");
        }

        private static void TestUndeclaredDependencyRejected()
        {
            var events = new List<string>();
            var registrations = new List<ReachyServiceRegistration>(
                BuildRegistrations(events));
            registrations[1] = Registration(
                "camera",
                ReachyServiceKind.Camera,
                ReachyServiceCriticality.Optional,
                Array.Empty<ReachyServiceKind>(),
                resolver =>
                {
                    resolver.GetRequired(ReachyServiceKind.Simulation);
                    return new CameraService("camera", events);
                });
            ReachyApplicationComposition composition =
                ReachyApplicationComposition.CreateComplete(registrations);
            using var host = new ReachyApplicationHost(composition);
            AssertThrows<InvalidOperationException>(
                host.Start,
                "undeclared dependency");
            AssertEqual(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "undeclared dependency health");
            AssertContains(events, "dispose:simulation", "construction rollback");
        }

        private static void TestFactoryContractMismatchRejected()
        {
            var events = new List<string>();
            var registrations = new List<ReachyServiceRegistration>(
                BuildRegistrations(events));
            registrations[0] = Registration(
                "simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required,
                Array.Empty<ReachyServiceKind>(),
                resolver => new CameraService("simulation", events));
            ReachyApplicationComposition composition =
                ReachyApplicationComposition.CreateComplete(registrations);
            using var host = new ReachyApplicationHost(composition);
            AssertThrows<InvalidOperationException>(
                host.Start,
                "marker interface mismatch");
            AssertEqual(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "factory mismatch health");
        }

        private static void TestInitializationFailureRollsBack()
        {
            var events = new List<string>();
            var registrations = new List<ReachyServiceRegistration>(
                BuildRegistrations(events));
            registrations[3] = Registration(
                "provider",
                ReachyServiceKind.Provider,
                ReachyServiceCriticality.Optional,
                new[] { ReachyServiceKind.Persistence },
                resolver => new ProviderService(
                    "provider",
                    events,
                    failInitialization: true));
            ReachyApplicationComposition composition =
                ReachyApplicationComposition.CreateComplete(registrations);
            using var host = new ReachyApplicationHost(composition);
            AssertThrows<InvalidOperationException>(
                host.Start,
                "initialization failure");
            AssertEqual(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "initialization failure health");
            AssertBefore(events, "dispose:ui", "dispose:simulation");
            AssertEqual(8, CountPrefix(events, "dispose:"), "rollback dispose count");
        }

        private static void TestReverseDisposalAndIdempotence()
        {
            var events = new List<string>();
            ReachyApplicationComposition composition = BuildComposition(events);
            var host = new ReachyApplicationHost(composition);
            host.Start();
            host.Dispose();
            host.Dispose();

            AssertEqual(
                ReachyApplicationState.Disposed,
                host.Health.State,
                "disposed host state");
            AssertBefore(events, "dispose:ui", "dispose:behavior");
            AssertBefore(events, "dispose:behavior", "dispose:simulation");
            AssertEqual(8, CountPrefix(events, "dispose:"), "idempotent dispose count");
        }

        private static void TestHealthAggregationAndSnapshotIsolation()
        {
            var events = new List<string>();
            ReachyApplicationComposition composition = BuildComposition(events);
            using var host = new ReachyApplicationHost(composition);
            host.Start();
            ReachyApplicationHealthSnapshot ready = host.Health;
            ulong readyRevision = ready.Revision;

            var camera = host.GetRequiredService<CameraService>(
                ReachyServiceKind.Camera);
            camera.PublishUnavailable("Camera permission has not been granted.");
            ReachyApplicationHealthSnapshot degraded = host.Health;
            AssertEqual(
                ReachyApplicationState.Degraded,
                degraded.State,
                "optional unavailable degradation");
            AssertTrue(
                degraded.Revision > readyRevision,
                "application health revision advances");
            AssertEqual(
                ReachyApplicationState.Ready,
                ready.State,
                "old top-level snapshot remains unchanged");
            AssertEqual(
                ReachyServiceState.Ready,
                Find(ready, ReachyServiceKind.Camera).State,
                "old service snapshot remains unchanged");

            var simulation = host.GetRequiredService<SimulationService>(
                ReachyServiceKind.Simulation);
            simulation.PublishFault("Authoritative simulation stopped.");
            AssertEqual(
                ReachyApplicationState.Faulted,
                host.Health.State,
                "required fault aggregation");
            AssertContains(
                host.Health.Message,
                "Authoritative simulation stopped.",
                "required fault diagnostics");
        }

        private static ReachyApplicationComposition BuildComposition(
            List<string> events)
        {
            return ReachyApplicationComposition.CreateComplete(
                BuildRegistrations(events));
        }

        private static IReadOnlyList<ReachyServiceRegistration> BuildRegistrations(
            List<string> events)
        {
            return new[]
            {
                Registration(
                    "simulation",
                    ReachyServiceKind.Simulation,
                    ReachyServiceCriticality.Required,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new SimulationService("simulation", events)),
                Registration(
                    "camera",
                    ReachyServiceKind.Camera,
                    ReachyServiceCriticality.Optional,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new CameraService("camera", events)),
                Registration(
                    "audio",
                    ReachyServiceKind.Audio,
                    ReachyServiceCriticality.Optional,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new AudioService("audio", events)),
                Registration(
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
                Registration(
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
                Registration(
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
                Registration(
                    "persistence",
                    ReachyServiceKind.Persistence,
                    ReachyServiceCriticality.Required,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new PersistenceService("persistence", events)),
                Registration(
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

        private static ReachyServiceRegistration Registration(
            string serviceId,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            IReadOnlyList<ReachyServiceKind> dependencies,
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
                    TestServiceBase testService = service as TestServiceBase ??
                        throw new InvalidOperationException(
                            "Test factory returned an unexpected service implementation.");
                    testService.RecordConstruction();
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
            throw new InvalidOperationException($"Missing health record for '{kind}'.");
        }

        private static int CountPrefix(IReadOnlyList<string> values, string prefix)
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

        private static void AssertBefore(
            IReadOnlyList<string> values,
            string earlier,
            string later)
        {
            int earlierIndex = IndexOf(values, earlier);
            int laterIndex = IndexOf(values, later);
            if (earlierIndex < 0 || laterIndex < 0 || earlierIndex >= laterIndex)
            {
                throw new InvalidOperationException(
                    $"Expected '{earlier}' before '{later}', found [{string.Join(", ", values)}].");
            }
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            for (int index = 0; index < values.Count; ++index)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private static void AssertContains(
            IReadOnlyList<string> values,
            string expected,
            string label)
        {
            if (IndexOf(values, expected) < 0)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: missing '{expected}'.");
            }
        }

        private static void AssertContains(
            string value,
            string expected,
            string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' does not contain '{expected}'.");
            }
        }

        private static void AssertTrue(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void AssertThrows<TException>(Action action, string label)
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

            protected TestServiceBase(
                string serviceId,
                ReachyServiceKind kind,
                ReachyServiceCriticality criticality,
                List<string> events,
                bool failInitialization = false)
                : base(serviceId, kind, criticality)
            {
                this.events = events;
                this.failInitialization = failInitialization;
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
            }
        }

        private sealed class SimulationService :
            TestServiceBase,
            IReachySimulationService
        {
            public SimulationService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Simulation,
                    ReachyServiceCriticality.Required,
                    events)
            {
            }
        }

        private sealed class CameraService : TestServiceBase, IReachyCameraService
        {
            public CameraService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Camera,
                    ReachyServiceCriticality.Optional,
                    events)
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
            public PerceptionService(string id, List<string> events)
                : base(
                    id,
                    ReachyServiceKind.Perception,
                    ReachyServiceCriticality.Optional,
                    events)
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
