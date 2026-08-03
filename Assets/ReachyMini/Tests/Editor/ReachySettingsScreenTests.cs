#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachySettingsScreenTests
    {
        [Test]
        public void AllSettingsSectionsAreActionableAndPreserveFixedCamera()
        {
            GameObject screenObject = new GameObject("Rma082Screen");
            GameObject cameraObject = new GameObject("Rma082Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0.62f, 0.36f, -0.62f);
                camera.transform.rotation = Quaternion.Euler(10f, 315f, 0f);
                Vector3 initialPosition = camera.transform.position;
                Quaternion initialRotation = camera.transform.rotation;

                ReachyMainScreen screen =
                    screenObject.AddComponent<ReachyMainScreen>();
                screen.ConfigurePresentationCamera(camera);
                var mainState = new ReachyMainScreenStateStore();
                var settings = new ReachySettingsStateStore();
                int resetCount = 0;
                screen.Bind(
                    mainState,
                    settings,
                    () => "settings-test-diagnostics",
                    () =>
                    {
                        ++resetCount;
                        return new ReachySettingsResetOutcome(
                            true,
                            "synthetic reset completed");
                    });

                screen.ToggleSettings();
                Assert.That(screen.Snapshot!.SettingsVisible, Is.True);
                foreach (ReachySettingsSection section in
                         (ReachySettingsSection[])Enum.GetValues(
                             typeof(ReachySettingsSection)))
                {
                    screen.SelectSettingsSection(section);
                    Assert.That(
                        screen.SettingsSnapshot!.ActiveSection,
                        Is.EqualTo(section));
                }

                screen.CycleProvider(ReachyProviderKind.Asr);
                screen.CycleProvider(ReachyProviderKind.Asr);
                screen.CycleProvider(ReachyProviderKind.Tts);
                screen.CycleProvider(ReachyProviderKind.Llm);
                screen.CycleProvider(ReachyProviderKind.Llm);
                screen.CycleProvider(ReachyProviderKind.Vlm);
                Assert.That(
                    screen.SettingsSnapshot!.GetProvider(
                        ReachyProviderKind.Asr).Connectivity,
                    Is.EqualTo(ReachyConnectivityRequirement.NetworkRequired));
                Assert.That(
                    screen.SettingsSnapshot.GetProvider(
                        ReachyProviderKind.Llm).Execution,
                    Is.EqualTo(ReachyProviderExecution.Cloud));
                StringAssert.Contains(
                    "Network required",
                    screen.SettingsSnapshot.PrivacyCloudSummary);

                screen.CyclePreferredCamera();
                screen.CycleSpeechLanguage();
                screen.CycleSpeechVoice();
                screen.CycleLocalModelMemoryBudget();
                screen.CycleLocalModelContextLength();
                screen.CycleSimulationFidelity();
                screen.ToggleHistory();
                screen.CycleRetentionDays();
                screen.RequestSimulationReset();
                Assert.That(resetCount, Is.EqualTo(1));
                StringAssert.Contains(
                    "Simulation reset completed",
                    screen.SettingsSnapshot.StatusMessage);

                screen.RequestCameraPreview();
                StringAssert.Contains(
                    "Camera preview unavailable",
                    screen.SettingsSnapshot.StatusMessage);
                screen.RequestCameraCalibration();
                screen.RequestReprojectionDiagnostics();
                screen.RequestLocalModelInstall();
                screen.RequestLocalModelImport();
                screen.RequestLocalModelSelect();
                screen.RequestLocalModelDelete();
                StringAssert.Contains(
                    "Local-model delete unavailable",
                    screen.SettingsSnapshot.StatusMessage);

                Assert.That(camera.transform.position, Is.EqualTo(initialPosition));
                Assert.That(camera.transform.rotation, Is.EqualTo(initialRotation));
            }
            finally
            {
                Object.DestroyImmediate(screenObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DurableSettingsRoundTripAndCorruptionIsQuarantined()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma082-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "settings.json");
            Directory.CreateDirectory(directory);
            try
            {
                var writer =
                    new ReachySettingsPersistenceApplicationService(path);
                writer.Initialize();
                writer.Settings.CycleProvider(ReachyProviderKind.Llm);
                writer.Settings.CycleProvider(ReachyProviderKind.Llm);
                writer.Settings.ToggleHistory();
                writer.Settings.CyclePreferredCameraFacing();
                writer.Dispose();
                Assert.That(File.Exists(path), Is.True);

                var reader =
                    new ReachySettingsPersistenceApplicationService(path);
                reader.Initialize();
                Assert.That(
                    reader.Settings.Current.GetProvider(
                        ReachyProviderKind.Llm).Execution,
                    Is.EqualTo(ReachyProviderExecution.Cloud));
                Assert.That(reader.Settings.Current.HistoryEnabled, Is.True);
                Assert.That(
                    reader.Settings.Current.PreferredCameraFacing,
                    Is.EqualTo(ReachyCameraFacing.Front));
                reader.Dispose();

                File.WriteAllText(path, "{not-valid-json");
                var recovery =
                    new ReachySettingsPersistenceApplicationService(path);
                recovery.Initialize();
                Assert.That(
                    recovery.Health.State,
                    Is.EqualTo(ReachyServiceState.Degraded));
                Assert.That(recovery.LastPersistenceFault, Is.Not.Empty);
                Assert.That(
                    recovery.Settings.Current.GetProvider(
                        ReachyProviderKind.Llm).Execution,
                    Is.EqualTo(ReachyProviderExecution.Unconfigured));
                Assert.That(
                    Directory.GetFiles(
                        directory,
                        "settings.json.corrupt-*").Length,
                    Is.EqualTo(1));
                recovery.Dispose();
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public void AndroidServiceSelectionCannotBePresentedAsOffline()
        {
            var settings = new ReachySettingsStateStore();
            settings.CycleProvider(ReachyProviderKind.Tts);
            settings.CycleProvider(ReachyProviderKind.Tts);
            ReachyProviderSelection tts =
                settings.Current.GetProvider(ReachyProviderKind.Tts);

            Assert.That(
                tts.Execution,
                Is.EqualTo(ReachyProviderExecution.AndroidService));
            Assert.That(
                tts.Connectivity,
                Is.EqualTo(ReachyConnectivityRequirement.NetworkRequired));
            StringAssert.Contains(
                "Network required",
                settings.Current.SpeechNetworkStatus);
            StringAssert.DoesNotContain(
                "offline",
                ReachySettingsStateStore.GetConnectivityLabel(tts.Connectivity)
                    .ToLowerInvariant());
        }
    }
}
