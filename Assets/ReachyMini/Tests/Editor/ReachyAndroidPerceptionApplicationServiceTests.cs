#nullable enable

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    // RMA-195 phase C: locks in the real perception service that replaced
    // the permanently-Unavailable stub. EditMode tests never invoke Unity's
    // Start()/Update(), and Application.platform in the Editor is never
    // Android, so the real per-frame pipeline
    // (ReachyAndroidPerceptionDriver -- camera texture bridge -> homography
    // -> tracker -> world model) never runs here; this deliberately
    // exercises the off-Android fail-closed path instead, the same way
    // ReachyLocalLlmProviderApplicationServiceTests (phase B) does. The real
    // pipeline is exercised by a physical acceptance harness (see the TODO
    // entry for known coverage gaps in that harness).
    public sealed class ReachyAndroidPerceptionApplicationServiceTests
    {
        private GameObject? runtimeObject;
        private ReachyProductionAuthoritativeRuntime? runtime;
        private ReachyCameraCalibrationPersistenceStore? calibrations;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Rma195PerceptionRuntime");
            runtime =
                runtimeObject.AddComponent<ReachyProductionAuthoritativeRuntime>();
            calibrations = new ReachyCameraCalibrationPersistenceStore(
                System.IO.Path.Combine(
                    Application.temporaryCachePath,
                    "rma195-perception-test-calibration.json"));
        }

        [TearDown]
        public void TearDown()
        {
            if (runtimeObject != null)
            {
                Object.DestroyImmediate(runtimeObject);
            }
            calibrations?.Dispose();
        }

        [Test]
        public void ServiceIdentifiesAsOptionalPerceptionService()
        {
            var service = new ReachyAndroidPerceptionApplicationService(
                runtime!,
                calibrations!);
            try
            {
                Assert.That(service.ServiceId, Is.EqualTo("perception"));
                Assert.That(service.Kind, Is.EqualTo(ReachyServiceKind.Perception));
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
        public void InitializeReportsUnavailableOffAndroid()
        {
            var service = new ReachyAndroidPerceptionApplicationService(
                runtime!,
                calibrations!);
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Unavailable));
                Assert.That(
                    service.Health.Message,
                    Does.Contain("requires an Android device"));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void PerceptionSnapshotDefaultsToNoCameraFrameBeforeInitialize()
        {
            var service = new ReachyAndroidPerceptionApplicationService(
                runtime!,
                calibrations!);
            try
            {
                ReachyPerceptionServiceSnapshot snapshot = service.PerceptionSnapshot;
                Assert.That(
                    snapshot.ExecutionState,
                    Is.EqualTo(ReachyPerceptionServiceExecutionState.NoCameraFrame));
                Assert.That(snapshot.WorldSnapshot, Is.Null);
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void PauseAndResumeAndDisposeDoNotThrowOrHangOffAndroid()
        {
            var service = new ReachyAndroidPerceptionApplicationService(
                runtime!,
                calibrations!);
            try
            {
                service.Initialize();

                Assert.DoesNotThrow(
                    () => service.PauseForApplicationInterruption());
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
                    "Dispose must not hang when no driver was created (off-Android).");
            }
        }

        // RMA-195 phase D (VLM half): mirrors
        // ReachyLocalLlmProviderApplicationServiceTests'
        // GenerateAsyncFailsClosedOffAndroidWithoutThrowing / cloud-generation
        // tests -- off-Android is the only reachable path from an EditMode
        // test, since Application.platform is never Android in the Editor.
        [Test]
        public void AnalyzeSceneAsyncFailsClosedOffAndroidWithoutThrowing()
        {
            var service = new ReachyAndroidPerceptionApplicationService(
                runtime!,
                calibrations!);
            ReachyVisionFrame frame = BuildOffAndroidTestFrame();
            try
            {
                service.Initialize();

                Task<VisionLanguageResult> task = service.AnalyzeSceneAsync(
                        frame,
                        "what is in this scene?",
                        "req-1",
                        CancellationToken.None)
                    .AsTask();
                Assert.That(
                    task.Wait(5000),
                    Is.True,
                    "AnalyzeSceneAsync must not hang off-Android.");

                VisionLanguageResult result = task.Result;
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Status,
                    Is.EqualTo(VisionOperationStatus.Unavailable));
                Assert.That(
                    result.Diagnostic,
                    Does.Contain("requires an Android device"));
            }
            finally
            {
                frame.DisposeAsync().AsTask().Wait(1000);
                service.Dispose();
            }
        }

        private static ReachyVisionFrame BuildOffAndroidTestFrame()
        {
            ReachyVisionFrameIdentity identity = new ReachyVisionFrameIdentity(
                "test-camera",
                sourceSessionId: 1UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: 1L,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            ReachyVisionCoverage coverage = new ReachyVisionCoverage(
                VisionCoverageState.Unavailable,
                validPixelCount: 0L,
                totalPixelCount: 0L,
                hasValidityMask: false,
                shouldStopVisionDrivenTurning: true,
                "off-android test frame carries no real coverage measurement.");
            return new ReachyVisionFrame(
                VisionFrameOrigin.RawPhoneDebug,
                identity,
                coverage,
                new FakeVisionFrameResources());
        }

        private sealed class FakeVisionFrameResources : IReachyVisionFrameResources
        {
            public string OwnerId => "test";

            public ulong Generation => 1UL;

            public int Width => 4;

            public int Height => 4;

            public bool IsDisposed { get; private set; }

            public bool HasResource(VisionResourceKind kind)
            {
                return kind == VisionResourceKind.Color;
            }

            public VisionPixelEncoding GetEncoding(VisionResourceKind kind)
            {
                return VisionPixelEncoding.Rgba8;
            }

            public bool TryGetResource<TResource>(
                VisionResourceKind kind,
                out TResource? resource)
                where TResource : class
            {
                resource = null;
                return false;
            }

            public ValueTask DisposeAsync()
            {
                IsDisposed = true;
                return default;
            }
        }
    }
}
