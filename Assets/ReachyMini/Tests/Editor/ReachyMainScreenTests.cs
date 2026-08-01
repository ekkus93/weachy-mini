#nullable enable

using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyMainScreenTests
    {
        [Test]
        public void ProductionShellInstallsOnceAndBindsAllRequiredBoundaries()
        {
            GameObject rootObject = new GameObject("Rma081Root");
            GameObject cameraObject = new GameObject("Rma081Camera");
            try
            {
                ReachyPresentationRoot root =
                    rootObject.AddComponent<ReachyPresentationRoot>();
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = true;
                cameraObject.tag = "MainCamera";
                ReachyPresentationCamera metadata =
                    cameraObject.AddComponent<ReachyPresentationCamera>();
                metadata.ConfigureFixedPresentationCamera();

                Assert.That(
                    ReachyMainScreenBootstrap.TryInstall(root, camera, out string fault),
                    Is.True,
                    fault);
                Assert.That(fault, Is.Empty);
                Assert.That(
                    ReachyMainScreenBootstrap.TryInstall(root, camera, out fault),
                    Is.True,
                    fault);

                ReachyMainScreen[] screens =
                    rootObject.GetComponentsInChildren<ReachyMainScreen>(true);
                Assert.That(screens, Has.Length.EqualTo(1));
                ReachyApplicationHostBehaviour host = rootObject
                    .GetComponentInChildren<ReachyApplicationHostBehaviour>(true);
                ReachyProductionApplicationCompositionProvider provider = rootObject
                    .GetComponentInChildren<
                        ReachyProductionApplicationCompositionProvider>(true);
                Assert.That(host, Is.Not.Null);
                Assert.That(provider, Is.Not.Null);

                host.StartApplication();

                Assert.That(host.Host, Is.Not.Null);
                Assert.That(host.Health, Is.Not.Null);
                Assert.That(
                    host.Health!.State,
                    Is.EqualTo(ReachyApplicationState.Degraded));
                Assert.That(host.Health.Services, Has.Count.EqualTo(8));
                Assert.That(screens[0].Snapshot, Is.Not.Null);
                Assert.That(
                    screens[0].Snapshot!.InteractionState,
                    Is.EqualTo(ReachyInteractionState.Idle));
                Assert.That(
                    screens[0].Snapshot.ActiveCamera,
                    Is.EqualTo("Fixed front / three-quarter"));
                Assert.That(
                    screens[0].Snapshot.ProviderLocation,
                    Is.EqualTo(ReachyProviderLocation.Unavailable));
                Assert.That(screens[0].Snapshot.MicrophoneAvailable, Is.False);
                Assert.That(
                    camera.GetComponent<ReachyPresentationCamera>()
                        .AcceptsUserNavigation,
                    Is.False);

                host.ShutdownApplication();
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ControlsSurfaceUnavailableActionsWithoutMovingCamera()
        {
            GameObject rootObject = new GameObject("Rma081ControlRoot");
            GameObject cameraObject = new GameObject("Rma081ControlCamera");
            try
            {
                ReachyPresentationRoot root =
                    rootObject.AddComponent<ReachyPresentationRoot>();
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = true;
                cameraObject.tag = "MainCamera";
                camera.transform.position = new Vector3(0.62f, 0.36f, -0.62f);
                camera.transform.rotation = Quaternion.Euler(10f, 315f, 0f);
                ReachyPresentationCamera metadata =
                    cameraObject.AddComponent<ReachyPresentationCamera>();
                metadata.ConfigureFixedPresentationCamera();
                Vector3 initialPosition = camera.transform.position;
                Quaternion initialRotation = camera.transform.rotation;

                Assert.That(
                    ReachyMainScreenBootstrap.TryInstall(root, camera, out string fault),
                    Is.True,
                    fault);
                ReachyApplicationHostBehaviour host = rootObject
                    .GetComponentInChildren<ReachyApplicationHostBehaviour>(true);
                ReachyMainScreen screen = rootObject
                    .GetComponentInChildren<ReachyMainScreen>(true);
                host.StartApplication();

                screen.RequestMicrophone();
                Assert.That(
                    screen.Snapshot!.InteractionState,
                    Is.EqualTo(ReachyInteractionState.Unavailable));
                StringAssert.Contains("Microphone unavailable", screen.Snapshot.Detail);

                screen.RequestCameraSelection();
                StringAssert.Contains(
                    "Camera selection unavailable",
                    screen.Snapshot.Detail);
                Assert.That(screen.Snapshot.ActiveCamera, Does.StartWith("Fixed"));

                screen.ToggleSettings();
                Assert.That(screen.Snapshot.SettingsVisible, Is.True);
                Assert.That(screen.Snapshot.DiagnosticsVisible, Is.False);
                screen.ToggleDiagnostics();
                Assert.That(screen.Snapshot.SettingsVisible, Is.False);
                Assert.That(screen.Snapshot.DiagnosticsVisible, Is.True);
                screen.ToggleDiagnostics();
                Assert.That(screen.Snapshot.DiagnosticsVisible, Is.False);

                Assert.That(camera.transform.position, Is.EqualTo(initialPosition));
                Assert.That(camera.transform.rotation, Is.EqualTo(initialRotation));
                Assert.That(metadata.AcceptsUserNavigation, Is.False);

                host.ShutdownApplication();
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void BootstrapRejectsNonPresentationCameraWithoutFallback()
        {
            GameObject rootObject = new GameObject("Rma081InvalidRoot");
            GameObject cameraObject = new GameObject("Rma081InvalidCamera");
            try
            {
                ReachyPresentationRoot root =
                    rootObject.AddComponent<ReachyPresentationRoot>();
                Camera camera = cameraObject.AddComponent<Camera>();

                Assert.That(
                    ReachyMainScreenBootstrap.TryInstall(root, camera, out string fault),
                    Is.False);
                StringAssert.Contains("fixed non-navigable", fault);
                Assert.That(
                    rootObject.GetComponentInChildren<ReachyMainScreen>(true),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
