#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Application
{
    public interface IReachyServiceResolver
    {
        IReachyApplicationService GetRequired(ReachyServiceKind kind);

        TService GetRequired<TService>(ReachyServiceKind kind)
            where TService : class, IReachyApplicationService;
    }

    public sealed class ReachyServiceRegistration
    {
        private readonly ReachyServiceKind[] dependencies;

        public ReachyServiceRegistration(
            string serviceId,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            IReadOnlyList<ReachyServiceKind> dependencies,
            Func<IReachyServiceResolver, IReachyApplicationService> factory)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                throw new ArgumentException(
                    "A service registration requires a non-empty identifier.",
                    nameof(serviceId));
            }
            if (dependencies == null)
            {
                throw new ArgumentNullException(nameof(dependencies));
            }

            ServiceId = serviceId;
            Kind = kind;
            Criticality = criticality;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.dependencies = new ReachyServiceKind[dependencies.Count];
            var seen = new HashSet<ReachyServiceKind>();
            for (int index = 0; index < dependencies.Count; ++index)
            {
                ReachyServiceKind dependency = dependencies[index];
                if (dependency == kind)
                {
                    throw new ArgumentException(
                        $"Service '{serviceId}' cannot depend on itself.",
                        nameof(dependencies));
                }
                if (!seen.Add(dependency))
                {
                    throw new ArgumentException(
                        $"Service '{serviceId}' declares dependency '{dependency}' more than once.",
                        nameof(dependencies));
                }
                this.dependencies[index] = dependency;
            }
        }

        public string ServiceId { get; }

        public ReachyServiceKind Kind { get; }

        public ReachyServiceCriticality Criticality { get; }

        public IReadOnlyList<ReachyServiceKind> Dependencies =>
            Array.AsReadOnly(dependencies);

        internal Func<IReachyServiceResolver, IReachyApplicationService> Factory
        {
            get;
        }
    }

    public sealed class ReachyApplicationComposition
    {
        private static readonly ReachyServiceKind[] RequiredKinds =
        {
            ReachyServiceKind.Simulation,
            ReachyServiceKind.Camera,
            ReachyServiceKind.Audio,
            ReachyServiceKind.Provider,
            ReachyServiceKind.Perception,
            ReachyServiceKind.Behavior,
            ReachyServiceKind.Persistence,
            ReachyServiceKind.UserInterface,
        };

        private readonly ReachyServiceRegistration[] orderedRegistrations;

        private ReachyApplicationComposition(
            ReachyServiceRegistration[] orderedRegistrations)
        {
            this.orderedRegistrations = orderedRegistrations;
        }

        public IReadOnlyList<ReachyServiceRegistration> OrderedRegistrations =>
            Array.AsReadOnly(orderedRegistrations);

        public static ReachyApplicationComposition CreateComplete(
            IReadOnlyList<ReachyServiceRegistration> registrations)
        {
            if (registrations == null)
            {
                throw new ArgumentNullException(nameof(registrations));
            }

            var byKind = new Dictionary<ReachyServiceKind, ReachyServiceRegistration>();
            var serviceIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < registrations.Count; ++index)
            {
                ReachyServiceRegistration registration = registrations[index] ??
                    throw new ArgumentException(
                        "A composition cannot contain a null registration.",
                        nameof(registrations));
                if (!byKind.TryAdd(registration.Kind, registration))
                {
                    throw new ArgumentException(
                        $"Service kind '{registration.Kind}' is registered more than once.",
                        nameof(registrations));
                }
                if (!serviceIds.Add(registration.ServiceId))
                {
                    throw new ArgumentException(
                        $"Service identifier '{registration.ServiceId}' is registered more than once.",
                        nameof(registrations));
                }
            }

            for (int index = 0; index < RequiredKinds.Length; ++index)
            {
                ReachyServiceKind kind = RequiredKinds[index];
                if (!byKind.ContainsKey(kind))
                {
                    throw new ArgumentException(
                        $"Complete application composition is missing '{kind}'.",
                        nameof(registrations));
                }
            }
            if (byKind.Count != RequiredKinds.Length)
            {
                throw new ArgumentException(
                    "Complete application composition contains an unknown service kind.",
                    nameof(registrations));
            }

            foreach (ReachyServiceRegistration registration in byKind.Values)
            {
                for (int index = 0; index < registration.Dependencies.Count; ++index)
                {
                    ReachyServiceKind dependency = registration.Dependencies[index];
                    if (!byKind.ContainsKey(dependency))
                    {
                        throw new ArgumentException(
                            $"Service '{registration.ServiceId}' depends on missing kind '{dependency}'.",
                            nameof(registrations));
                    }
                }
            }

            ReachyServiceRegistration[] ordered = TopologicalOrder(byKind);
            return new ReachyApplicationComposition(ordered);
        }

        internal static void ValidateServiceContract(
            ReachyServiceRegistration registration,
            IReachyApplicationService service)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            if (!string.Equals(
                    registration.ServiceId,
                    service.ServiceId,
                    StringComparison.Ordinal) ||
                registration.Kind != service.Kind ||
                registration.Criticality != service.Criticality)
            {
                throw new InvalidOperationException(
                    $"Factory for '{registration.ServiceId}' returned a service with mismatched identity, kind, or criticality.");
            }

            bool markerMatches = registration.Kind switch
            {
                ReachyServiceKind.Simulation => service is IReachySimulationService,
                ReachyServiceKind.Camera => service is IReachyCameraService,
                ReachyServiceKind.Audio => service is IReachyAudioService,
                ReachyServiceKind.Provider => service is IReachyProviderService,
                ReachyServiceKind.Perception => service is IReachyPerceptionService,
                ReachyServiceKind.Behavior => service is IReachyBehaviorService,
                ReachyServiceKind.Persistence => service is IReachyPersistenceService,
                ReachyServiceKind.UserInterface =>
                    service is IReachyUserInterfaceService,
                _ => false,
            };
            if (!markerMatches)
            {
                throw new InvalidOperationException(
                    $"Service '{registration.ServiceId}' does not implement the '{registration.Kind}' boundary interface.");
            }
        }

        private static ReachyServiceRegistration[] TopologicalOrder(
            Dictionary<ReachyServiceKind, ReachyServiceRegistration> byKind)
        {
            var states = new Dictionary<ReachyServiceKind, int>();
            var ordered = new List<ReachyServiceRegistration>(byKind.Count);
            for (int index = 0; index < RequiredKinds.Length; ++index)
            {
                Visit(RequiredKinds[index], byKind, states, ordered);
            }
            return ordered.ToArray();
        }

        private static void Visit(
            ReachyServiceKind kind,
            Dictionary<ReachyServiceKind, ReachyServiceRegistration> byKind,
            Dictionary<ReachyServiceKind, int> states,
            List<ReachyServiceRegistration> ordered)
        {
            if (states.TryGetValue(kind, out int state))
            {
                if (state == 2)
                {
                    return;
                }
                if (state == 1)
                {
                    throw new ArgumentException(
                        $"Application service dependency cycle includes '{kind}'.",
                        nameof(byKind));
                }
            }

            states[kind] = 1;
            ReachyServiceRegistration registration = byKind[kind];
            for (int index = 0; index < registration.Dependencies.Count; ++index)
            {
                Visit(
                    registration.Dependencies[index],
                    byKind,
                    states,
                    ordered);
            }
            states[kind] = 2;
            ordered.Add(registration);
        }
    }
}
