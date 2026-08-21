#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Behavior;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyApplicationHostTests
    {
        [Test]
        public void MissingCompositionProviderFailsClosed()
        {
            var root = new GameObject("ReachyApplicationMissingProviderTest");
            try
            {
                ReachyApplicationHostBehaviour behaviour =
                    root.AddComponent<ReachyApplicationHostBehaviour>();
                InvokeWithExpectedStructuredError(behaviour.StartApplication);

                Assert.That(behaviour.Host, Is.Null);
                Assert.That(
                    behaviour.Fault,
                    Is.EqualTo(
                        "Reachy application startup requires an explicit composition provider."));
                Assert.Throws<InvalidOperationException>(behaviour.StartApplication);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void InvokeWithExpectedStructuredError(Action action)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                action();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void ExplicitCompositionStartsAndDisposesThroughUnityLifecycle()
        {
            var root = new GameObject("ReachyApplicationHostLifecycleTest");
            var events = new List<string>();
            try
            {
                TestApplicationCompositionProvider provider =
                    root.AddComponent<TestApplicationCompositionProvider>();
                provider.Configure(events);
                ReachyApplicationHostBehaviour behaviour =
                    root.AddComponent<ReachyApplicationHostBehaviour>();
                behaviour.ConfigureCompositionProvider(provider);

                behaviour.StartApplication();

                Assert.That(behaviour.Host, Is.Not.Null);
                Assert.That(behaviour.Health, Is.Not.Null);
                Assert.That(
                    behaviour.Health!.State,
                    Is.EqualTo(ReachyApplicationState.Ready));
                Assert.That(behaviour.Health.Services.Count, Is.EqualTo(8));
                Assert.That(events.FindAll(value =>
                    value.StartsWith("initialize:", StringComparison.Ordinal)).Count,
                    Is.EqualTo(8));

                behaviour.ShutdownApplication();
                behaviour.ShutdownApplication();
                Assert.That(behaviour.Host, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Assert.That(events.FindAll(value =>
                value.StartsWith("dispose:", StringComparison.Ordinal)).Count,
                Is.EqualTo(8));
            Assert.That(events[events.Count - 1], Is.EqualTo("dispose:simulation"));
        }
    }

    public sealed class TestApplicationCompositionProvider :
        MonoBehaviour,
        IReachyApplicationCompositionProvider
    {
        private List<string>? events;

        public void Configure(List<string> lifecycleEvents)
        {
            events = lifecycleEvents ??
                throw new ArgumentNullException(nameof(lifecycleEvents));
        }

        public ReachyApplicationComposition CreateApplicationComposition()
        {
            List<string> lifecycleEvents = events ??
                throw new InvalidOperationException(
                    "Test composition provider was not configured.");
            return ReachyApplicationComposition.CreateComplete(
                new[]
                {
                    Register(
                        "simulation",
                        ReachyServiceKind.Simulation,
                        ReachyServiceCriticality.Required,
                        lifecycleEvents),
                    Register(
                        "camera",
                        ReachyServiceKind.Camera,
                        ReachyServiceCriticality.Optional,
                        lifecycleEvents),
                    Register(
                        "audio",
                        ReachyServiceKind.Audio,
                        ReachyServiceCriticality.Optional,
                        lifecycleEvents),
                    Register(
                        "provider",
                        ReachyServiceKind.Provider,
                        ReachyServiceCriticality.Optional,
                        lifecycleEvents),
                    Register(
                        "perception",
                        ReachyServiceKind.Perception,
                        ReachyServiceCriticality.Optional,
                        lifecycleEvents),
                    Register(
                        "behavior",
                        ReachyServiceKind.Behavior,
                        ReachyServiceCriticality.Optional,
                        lifecycleEvents),
                    Register(
                        "persistence",
                        ReachyServiceKind.Persistence,
                        ReachyServiceCriticality.Required,
                        lifecycleEvents),
                    Register(
                        "ui",
                        ReachyServiceKind.UserInterface,
                        ReachyServiceCriticality.Required,
                        lifecycleEvents),
                });
        }

        private static ReachyServiceRegistration Register(
            string id,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            List<string> events)
        {
            return new ReachyServiceRegistration(
                id,
                kind,
                criticality,
                Array.Empty<ReachyServiceKind>(),
                resolver => new TestApplicationService(
                    id,
                    kind,
                    criticality,
                    events));
        }
    }

    internal sealed class TestApplicationService :
        ReachyApplicationServiceBase,
        IReachySimulationService,
        IReachyCameraService,
        IReachyAudioService,
        IReachyProviderService,
        IReachyPerceptionService,
        IReachyBehaviorService,
        IReachyPersistenceService,
        IReachyUserInterfaceService
    {
        private readonly List<string> events;

        public TestApplicationService(
            string id,
            ReachyServiceKind kind,
            ReachyServiceCriticality criticality,
            List<string> events)
            : base(id, kind, criticality)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        protected override void OnInitialize()
        {
            events.Add($"initialize:{ServiceId}");
        }

        protected override void OnDispose()
        {
            events.Add($"dispose:{ServiceId}");
        }

        public ReachyBehaviorServiceSnapshot Snapshot { get; } =
            new ReachyBehaviorServiceSnapshot(
                ReachyBehaviorServiceExecutionState.Idle,
                ReachyBaselineBehaviorKind.NeutralIdle,
                "test double",
                0UL);

        public event EventHandler<ReachyBehaviorServiceSnapshotChangedEventArgs>?
            SnapshotChanged
        {
            add { }
            remove { }
        }

        public bool TryTriggerGesture(
            ReachyBaselineBehaviorKind gesture,
            out string diagnosticCode)
        {
            diagnosticCode = "test-double-does-not-execute-gestures";
            return false;
        }

        public ReachyProviderServiceSnapshot ProviderSnapshot { get; } =
            new ReachyProviderServiceSnapshot(
                ReachyProviderServiceExecutionState.NotLoaded,
                string.Empty,
                "test double",
                0UL);

        public event EventHandler<ReachyProviderServiceSnapshotChangedEventArgs>?
            ProviderSnapshotChanged
        {
            add { }
            remove { }
        }
    }
}
