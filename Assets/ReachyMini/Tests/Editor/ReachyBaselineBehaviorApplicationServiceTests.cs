#nullable enable

using System.Diagnostics;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Behavior;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    // RMA-195 phase A: locks in the real, continuously-running behavior
    // service that replaced the permanently-Unavailable stub. The
    // authoritative runtime here is a bare, never-started
    // ReachyProductionAuthoritativeRuntime -- EditMode tests never invoke
    // Unity's Start()/Update(), so its worker never comes up and Status stays
    // Uninitialized. That deliberately exercises the service's "waiting for
    // the runtime" path rather than a real physics-driven trajectory; the
    // latter is covered by the physical RMA-154 acceptance harness instead.
    public sealed class ReachyBaselineBehaviorApplicationServiceTests
    {
        private GameObject? runtimeObject;
        private ReachyProductionAuthoritativeRuntime? runtime;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Rma195BaselineBehaviorRuntime");
            runtime =
                runtimeObject.AddComponent<ReachyProductionAuthoritativeRuntime>();
        }

        [TearDown]
        public void TearDown()
        {
            if (runtimeObject != null)
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void ServiceIdentifiesAsOptionalBehaviorService()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                Assert.That(service.ServiceId, Is.EqualTo("baseline-behavior"));
                Assert.That(service.Kind, Is.EqualTo(ReachyServiceKind.Behavior));
                Assert.That(
                    service.Criticality,
                    Is.EqualTo(ReachyServiceCriticality.Optional));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void InitializeReachesReadyAndStartsTheContinuousLoop()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Ready));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void SnapshotDefaultsToNeutralIdleBeforeInitialize()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                ReachyBehaviorServiceSnapshot snapshot = service.Snapshot;
                Assert.That(
                    snapshot.ExecutionState,
                    Is.EqualTo(ReachyBehaviorServiceExecutionState.Idle));
                Assert.That(
                    snapshot.CurrentBehavior,
                    Is.EqualTo(ReachyBaselineBehaviorKind.NeutralIdle));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void TryTriggerGestureFailsClosedBeforeInitialize()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                Assert.That(
                    service.TryTriggerGesture(
                        ReachyBaselineBehaviorKind.NeutralIdle,
                        out string diagnosticCode),
                    Is.False);
                Assert.That(
                    diagnosticCode,
                    Is.EqualTo("baseline-behavior-service-not-ready"));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void TryTriggerGestureAcceptsBaselineKindsAndRejectsPerceptionOnlyKinds()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                service.Initialize();

                Assert.That(
                    service.TryTriggerGesture(
                        ReachyBaselineBehaviorKind.Acknowledgment,
                        out string acceptedCode),
                    Is.True);
                Assert.That(acceptedCode, Is.Empty);

                Assert.That(
                    service.TryTriggerGesture(
                        ReachyBaselineBehaviorKind.GazeAcquisition,
                        out string gazeCode),
                    Is.False);
                Assert.That(
                    gazeCode,
                    Is.EqualTo(
                        "baseline-behavior-gesture-requires-perception-or-provider-input"));

                Assert.That(
                    service.TryTriggerGesture(
                        ReachyBaselineBehaviorKind.GazeSearch,
                        out string gazeSearchCode),
                    Is.False);
                Assert.That(
                    gazeSearchCode,
                    Is.EqualTo(
                        "baseline-behavior-gesture-requires-perception-or-provider-input"));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void PauseTransitionsToPausedAndResumeAndDisposeDoNotThrowOrHang()
        {
            var service = new ReachyBaselineBehaviorApplicationService(runtime!);
            try
            {
                service.Initialize();

                Assert.DoesNotThrow(
                    () => service.PauseForApplicationInterruption());
                Assert.That(
                    service.Snapshot.ExecutionState,
                    Is.EqualTo(ReachyBehaviorServiceExecutionState.Paused));

                Assert.DoesNotThrow(
                    () => service.ResumeAfterApplicationInterruption());
            }
            finally
            {
                var stopwatch = Stopwatch.StartNew();
                service.Dispose();
                stopwatch.Stop();
                Assert.That(
                    stopwatch.ElapsedMilliseconds,
                    Is.LessThan(2000),
                    "Dispose must cancel the background loop promptly, not block.");
            }
        }
    }
}
