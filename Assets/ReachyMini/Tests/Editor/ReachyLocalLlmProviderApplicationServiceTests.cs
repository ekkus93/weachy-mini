#nullable enable

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    // RMA-195 phase B: locks in the real local-LLM provider wiring that
    // replaced the permanently-Unavailable stub. EditMode tests never invoke
    // Unity's Start()/Update(), and Application.platform in the Editor is
    // never Android, so every path that would touch
    // LocalModelPackageManager/ReachyAndroidLocalLlmResourceSignalSource is
    // unreachable here -- exactly the "requires an Android device" fail-safe
    // this test exercises instead. The real load/admission/generate path is
    // covered by the RMA-134/135 physical acceptance harnesses.
    public sealed class ReachyLocalLlmProviderApplicationServiceTests
    {
        private GameObject? runtimeObject;
        private ReachyProductionAuthoritativeRuntime? runtime;
        private ReachySettingsStateStore? settings;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Rma195LocalLlmProviderRuntime");
            runtime =
                runtimeObject.AddComponent<ReachyProductionAuthoritativeRuntime>();
            settings = new ReachySettingsStateStore();
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
        public void ServiceIdentifiesAsOptionalProviderService()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
            try
            {
                Assert.That(service.ServiceId, Is.EqualTo("provider-selection"));
                Assert.That(service.Kind, Is.EqualTo(ReachyServiceKind.Provider));
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
        public void InitializeReportsUnavailableWithNoProviderConfigured()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Unavailable));
                Assert.That(
                    service.Health.Message,
                    Does.Contain("No ASR, TTS, LLM, or VLM provider is configured."));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void InitializeReportsDegradedWhenLocalLlmIsSelected()
        {
            settings!.CycleProvider(ReachyProviderKind.Llm);
            var service =
                new ReachyLocalLlmProviderApplicationService(settings, runtime!);
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Degraded));
                Assert.That(
                    service.Health.Message,
                    Does.Contain("on-device local LLM preference is wired"));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void HealthTracksSettingsChangesAfterInitialize()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Unavailable));

                settings!.CycleProvider(ReachyProviderKind.Llm);

                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Degraded));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void ProviderSnapshotDefaultsToNotLoadedBeforeGenerate()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
            try
            {
                ReachyProviderServiceSnapshot snapshot = service.ProviderSnapshot;
                Assert.That(
                    snapshot.ExecutionState,
                    Is.EqualTo(ReachyProviderServiceExecutionState.NotLoaded));
                Assert.That(snapshot.ActiveModelId, Is.Empty);
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void GenerateAsyncFailsClosedOffAndroidWithoutThrowing()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
            try
            {
                service.Initialize();
                var request = new LocalLlmGenerationRequest(
                    "req-1",
                    new[]
                    {
                        new LocalLlmChatMessage(LocalLlmChatRole.User, "hello"),
                    });
                var sink = new RecordingStreamSink();

                Task<LocalLlmGovernedGenerationResult> task = service.GenerateAsync(
                    request,
                    sink,
                    CancellationToken.None);
                Assert.That(task.Wait(5000), Is.True, "GenerateAsync must not hang off-Android.");

                LocalLlmGovernedGenerationResult result = task.Result;
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Status,
                    Is.EqualTo(LocalLlmGovernedGenerationStatus.SignalFailure));
                Assert.That(
                    result.Detail,
                    Does.Contain("requires an Android device"));
                Assert.That(
                    service.ProviderSnapshot.ExecutionState,
                    Is.EqualTo(ReachyProviderServiceExecutionState.Faulted));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void PauseAndResumeAndDisposeDoNotThrowOrHangBeforeAnyLoad()
        {
            var service =
                new ReachyLocalLlmProviderApplicationService(settings!, runtime!);
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
                    "Dispose must not block on a load that never started.");
            }
        }

        private sealed class RecordingStreamSink : ILocalLlmStreamSink
        {
            public ValueTask OnEventAsync(
                LocalLlmStreamEvent streamEvent,
                CancellationToken cancellationToken)
            {
                return default;
            }
        }
    }
}
