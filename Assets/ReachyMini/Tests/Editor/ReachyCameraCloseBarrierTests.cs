#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyCameraCloseBarrierTests
    {
        [Test]
        public void ExplicitStopRemainsStoppingUntilClosedSnapshot()
        {
            GameObject gameObject = new GameObject(
                "Rma104ExplicitCameraCloseBarrier");
            try
            {
                CreateServices(
                    gameObject,
                    out ReachyAndroidCameraAcquisition acquisition,
                    out DeferredStopPlatform platform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong session = acquisition.State.Current.SessionId;
                platform.PublishRunning(session, "rear-0", "rear");
                acquisition.RefreshNow();

                acquisition.StopAcquisition();

                Assert.That(platform.StopCount, Is.EqualTo(1));
                Assert.That(acquisition.DesiredActive, Is.False);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopping));
                Assert.That(
                    acquisition.State.Current.SessionId,
                    Is.EqualTo(session));
                StringAssert.Contains(
                    "closing",
                    acquisition.State.Current.Message);

                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopping));

                platform.CompleteStop();
                acquisition.RefreshNow();

                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopped));
                Assert.That(acquisition.State.Current.SessionId, Is.Zero);
                Assert.That(acquisition.State.Current.CameraId, Is.Empty);
                StringAssert.Contains(
                    "CLOSED",
                    acquisition.State.Current.Message);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CameraSwitchStartsOnlyAfterClosedSnapshot()
        {
            GameObject gameObject = new GameObject(
                "Rma104CameraSwitchCloseBarrier");
            try
            {
                CreateServices(
                    gameObject,
                    out ReachyAndroidCameraAcquisition acquisition,
                    out DeferredStopPlatform platform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong rearSession = acquisition.State.Current.SessionId;
                platform.PublishRunning(rearSession, "rear-0", "rear");
                acquisition.RefreshNow();

                acquisition.StartPreferred(ReachyCameraFacing.Front);

                Assert.That(platform.StopCount, Is.EqualTo(1));
                Assert.That(platform.StartCount, Is.EqualTo(1));
                Assert.That(acquisition.DesiredActive, Is.True);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopping));

                acquisition.RefreshNow();
                Assert.That(platform.StartCount, Is.EqualTo(1));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopping));

                platform.CompleteStop();
                acquisition.RefreshNow();

                Assert.That(platform.StartCount, Is.EqualTo(2));
                Assert.That(platform.CameraId, Is.EqualTo("front-1"));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Starting));
                Assert.That(
                    acquisition.State.Current.SessionId,
                    Is.GreaterThan(rearSession));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void CreateServices(
            GameObject gameObject,
            out ReachyAndroidCameraAcquisition acquisition,
            out DeferredStopPlatform platform)
        {
            ReachyAndroidCameraDiscovery discovery =
                gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
            discovery.ConfigurePlatformForTests(
                new GrantedDiscoveryPlatform());
            acquisition =
                gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
            platform = new DeferredStopPlatform();
            acquisition.ConfigurePlatformForTests(discovery, platform);
        }

        private sealed class GrantedDiscoveryPlatform :
            IReachyDeviceCameraPlatform
        {
            public bool IsSupported => true;

            public bool HasCameraPermission()
            {
                return true;
            }

            public bool ShouldShowCameraPermissionRationale()
            {
                return false;
            }

            public void RequestCameraPermission(
                Action granted,
                Action denied)
            {
                _ = denied;
                granted();
            }

            public string DiscoverCameraCapabilitiesJson()
            {
                return
                    "{\"status\":\"ok\",\"errorCode\":\"\"," +
                    "\"message\":\"front and rear available\",\"cameras\":[" +
                    CameraJson("rear-0", "rear") + "," +
                    CameraJson("front-1", "front") + "]}";
            }

            public void OpenApplicationSettings()
            {
            }

            public void Dispose()
            {
            }

            private static string CameraJson(
                string cameraId,
                string facing)
            {
                return
                    "{\"id\":\"" + cameraId +
                    "\",\"facing\":\"" + facing +
                    "\",\"sensorOrientationDegrees\":90," +
                    "\"hardwareLevel\":\"full\"," +
                    "\"availability\":\"available\"," +
                    "\"analysisResolutions\":[{\"width\":1280," +
                    "\"height\":720}],\"intrinsics\":{" +
                    "\"available\":true,\"fx\":900.0," +
                    "\"fy\":900.0,\"cx\":640.0," +
                    "\"cy\":360.0,\"skew\":0.0}," +
                    "\"activeArrayWidth\":4032," +
                    "\"activeArrayHeight\":3024," +
                    "\"calibrationFallback\":\"none\"}";
            }
        }

        private sealed class DeferredStopPlatform :
            IReachyDeviceCameraAcquisitionPlatform
        {
            public bool IsSupported => true;

            public int StartCount { get; private set; }

            public int StopCount { get; private set; }

            public long SessionId { get; private set; }

            public string CameraId { get; private set; } = string.Empty;

            public string Facing { get; private set; } = "unknown";

            private string nextSnapshot = StoppedSnapshot();

            public string Start(
                long sessionId,
                string cameraId,
                int width,
                int height)
            {
                _ = width;
                _ = height;
                StartCount = checked(StartCount + 1);
                SessionId = sessionId;
                CameraId = cameraId;
                Facing = cameraId.StartsWith(
                    "front",
                    StringComparison.Ordinal)
                    ? "front"
                    : "rear";
                nextSnapshot = ActiveSnapshot(
                    "Starting",
                    "binding");
                return nextSnapshot;
            }

            public string Pause()
            {
                nextSnapshot = ActiveSnapshot("Paused", "paused");
                return nextSnapshot;
            }

            public string Resume()
            {
                nextSnapshot = ActiveSnapshot("Running", "resumed");
                return nextSnapshot;
            }

            public string Stop()
            {
                StopCount = checked(StopCount + 1);
                nextSnapshot = ActiveSnapshot(
                    "Stopping",
                    "CameraX camera device is closing; restart remains blocked.");
                return nextSnapshot;
            }

            public IReachyCameraTextureFrameLease?
                AcquireLatestTextureFrame(
                    long requestedSessionId,
                    long afterSequence)
            {
                _ = requestedSessionId;
                _ = afterSequence;
                return null;
            }

            public string Snapshot()
            {
                return nextSnapshot;
            }

            public void PublishRunning(
                ulong sessionId,
                string cameraId,
                string facing)
            {
                SessionId = checked((long)sessionId);
                CameraId = cameraId;
                Facing = facing;
                nextSnapshot = ActiveSnapshot(
                    "Running",
                    "camera open");
            }

            public void CompleteStop()
            {
                nextSnapshot = StoppedSnapshot();
            }

            public void Dispose()
            {
            }

            private string ActiveSnapshot(
                string state,
                string detail)
            {
                return
                    "{\"status\":\"ok\",\"state\":\"" + state +
                    "\",\"errorCode\":\"\",\"message\":\"" +
                    detail + "\",\"sessionId\":" + SessionId +
                    ",\"cameraId\":\"" + CameraId +
                    "\",\"facing\":\"" + Facing +
                    "\",\"analysisBackpressure\":" +
                    "\"keep_only_latest\",\"previewSink\":" +
                    "\"analysis_yuv_gpu_texture_bridge\"," +
                    "\"cpuPixelCopyPerformed\":false," +
                    "\"latestFrame\":null}";
            }

            private static string StoppedSnapshot()
            {
                return
                    "{\"status\":\"ok\",\"state\":\"Stopped\"," +
                    "\"errorCode\":\"\",\"message\":" +
                    "\"CameraX camera device reached CLOSED; " +
                    "Preview and ImageAnalysis are fully released.\"," +
                    "\"sessionId\":0,\"cameraId\":\"\"," +
                    "\"facing\":\"unknown\"," +
                    "\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":" +
                    "\"analysis_yuv_gpu_texture_bridge\"," +
                    "\"cpuPixelCopyPerformed\":false," +
                    "\"latestFrame\":null}";
            }
        }
    }
}
