#nullable enable

using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyDiagnosticsScreenTests
    {
        [Test]
        public void TypedDiagnosticsRemainVisibleAndPreserveUnavailableReason()
        {
            GameObject screenObject = new GameObject("Rma171Screen");
            GameObject cameraObject = new GameObject("Rma171Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                ReachyMainScreen screen =
                    screenObject.AddComponent<ReachyMainScreen>();
                screen.ConfigurePresentationCamera(camera);
                var state = new ReachyMainScreenStateStore();
                var settings = new ReachySettingsStateStore();
                var cameras = new ReachyCameraCapabilityStateStore();
                ReachyDiagnosticsScreenSnapshot diagnostics = Snapshot();

                screen.Bind(
                    state,
                    settings,
                    () => diagnostics,
                    () => new ReachySettingsResetOutcome(
                        false,
                        "Reset is intentionally unavailable in this test."),
                    cameras,
                    () => { });

                screen.ToggleDiagnostics();
                Assert.That(screen.Snapshot!.DiagnosticsVisible, Is.True);
                ReachyDiagnosticsScreenSnapshot? published =
                    screen.DiagnosticsSnapshot;
                Assert.That(published, Is.SameAs(diagnostics));
                Assert.That(
                    published!.Camera.Availability,
                    Is.EqualTo(ReachyDiagnosticsAvailability.Unavailable));
                StringAssert.Contains(
                    "No reprojection timing source is bound.",
                    published.ToDisplayText());
            }
            finally
            {
                Object.DestroyImmediate(screenObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static ReachyDiagnosticsScreenSnapshot Snapshot()
        {
            ReachyDiagnosticsSection Available(string title) =>
                new ReachyDiagnosticsSection(
                    title,
                    new[] { new ReachyDiagnosticsMetric("state", "ready") });
            return new ReachyDiagnosticsScreenSnapshot(
                Available("Simulation"),
                Available("Rendering"),
                new ReachyDiagnosticsSection(
                    "Camera",
                    new[]
                    {
                        ReachyDiagnosticsMetric.Unavailable(
                            "Reprojection time",
                            "No reprojection timing source is bound."),
                    }),
                Available("Providers"),
                Available("Versions"),
                Available("Device"));
        }
    }
}
