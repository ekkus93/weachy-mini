#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyCameraAcquisitionOwnershipTests
    {
        [Test]
        public void OwnedCameraOmissionDoesNotStopActiveSession()
        {
            GameObject gameObject = new GameObject("Rma091OwnedOmission");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new OwnershipDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);
                discovery.RefreshNowForTests();

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new OwnershipAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong sessionId = acquisition.State.Current.SessionId;
                acquisitionPlatform.SnapshotJson = RunningSnapshot(
                    sessionId,
                    "rear-0",
                    1UL,
                    100L);
                acquisition.RefreshNow();

                discoveryPlatform.OmitOwnedCamera = true;
                discovery.RefreshNowForTests();

                Assert.That(acquisition.DesiredActive, Is.True);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));
                Assert.That(
                    acquisition.State.Current.SessionId,
                    Is.EqualTo(sessionId));
                Assert.That(acquisitionPlatform.StopCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BusyAvailabilityDoesNotStopSelfOwnedSession()
        {
            GameObject gameObject = new GameObject("Rma091OwnedBusy");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new OwnershipDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);
                discovery.RefreshNowForTests();

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new OwnershipAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong sessionId = acquisition.State.Current.SessionId;
                acquisitionPlatform.SnapshotJson = RunningSnapshot(
                    sessionId,
                    "rear-0",
                    1UL,
                    100L);
                acquisition.RefreshNow();

                discoveryPlatform.Availability = "busy";
                discovery.RefreshNowForTests();

                Assert.That(acquisition.DesiredActive, Is.True);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));
                Assert.That(acquisitionPlatform.StopCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DisconnectedAvailabilityStopsFailClosed()
        {
            GameObject gameObject = new GameObject("Rma091Disconnected");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new OwnershipDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);
                discovery.RefreshNowForTests();

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new OwnershipAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong sessionId = acquisition.State.Current.SessionId;
                acquisitionPlatform.SnapshotJson = RunningSnapshot(
                    sessionId,
                    "rear-0",
                    1UL,
                    100L);
                acquisition.RefreshNow();

                discoveryPlatform.Availability = "disconnected";
                discovery.RefreshNowForTests();

                Assert.That(acquisition.DesiredActive, Is.False);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Unavailable));
                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static string RunningSnapshot(
            ulong sessionId,
            string cameraId,
            ulong sequence,
            long timestamp)
        {
            return
                "{\"status\":\"ok\",\"state\":\"Running\"," +
                "\"errorCode\":\"\",\"message\":\"running\"," +
                "\"sessionId\":" + sessionId +
                ",\"cameraId\":\"" + cameraId +
                "\",\"facing\":\"rear\"," +
                "\"analysisBackpressure\":\"keep_only_latest\"," +
                "\"previewSink\":\"analysis_yuv_gpu_texture_bridge\"," +
                "\"cpuPixelCopyPerformed\":true,\"latestFrame\":{" +
                "\"sessionId\":" + sessionId +
                ",\"sequence\":" + sequence +
                ",\"timestampNanoseconds\":" + timestamp +
                ",\"cameraId\":\"" + cameraId +
                "\",\"facing\":\"rear\"," +
                "\"sensorOrientationDegrees\":90," +
                "\"rotationDegrees\":90,\"width\":1280,\"height\":720," +
                "\"pixelFormat\":\"YUV_420_888\"," +
                "\"crop\":{\"left\":0,\"top\":0,\"right\":1280,\"bottom\":720}," +
                "\"intrinsics\":{\"available\":true,\"fx\":900.0," +
                "\"fy\":900.0,\"cx\":640.0,\"cy\":360.0," +
                "\"skew\":0.0,\"source\":\"platform_intrinsics\"}," +
                "\"imagePlanesAccessed\":true," +
                "\"cpuPixelCopyPerformed\":true," +
                "\"textureFramePublished\":true," +
                "\"textureFrameStale\":false," +
                "\"mirrored\":false," +
                "\"colorStandard\":\"bt709\"," +
                "\"colorRange\":\"limited\"}}";
        }

        private sealed class OwnershipDiscoveryPlatform :
            IReachyDeviceCameraPlatform
        {
            public bool IsSupported => true;

            public bool OmitOwnedCamera { get; set; }

            public string Availability { get; set; } = "available";

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
                if (OmitOwnedCamera)
                {
                    return
                        "{\"status\":\"ok\",\"permission\":\"granted\"," +
                        "\"errorCode\":\"\"," +
                        "\"message\":\"owned camera omitted by API-26 service\"," +
                        "\"cameras\":[]}";
                }
                return
                    "{\"status\":\"ok\",\"errorCode\":\"\"," +
                    "\"message\":\"rear camera state updated\",\"cameras\":[{" +
                    "\"id\":\"rear-0\",\"facing\":\"rear\"," +
                    "\"sensorOrientationDegrees\":90,\"hardwareLevel\":\"full\"," +
                    "\"availability\":\"" + Availability + "\"," +
                    "\"analysisResolutions\":[{\"width\":1280,\"height\":720}]," +
                    "\"intrinsics\":{\"available\":true,\"fx\":900.0," +
                    "\"fy\":900.0,\"cx\":640.0,\"cy\":360.0,\"skew\":0.0}," +
                    "\"activeArrayWidth\":4032,\"activeArrayHeight\":3024," +
                    "\"calibrationFallback\":\"checkerboard fallback\"}]}";
            }

            public void OpenApplicationSettings()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class OwnershipAcquisitionPlatform :
            IReachyDeviceCameraAcquisitionPlatform
        {
            public bool IsSupported => true;

            public int StopCount { get; private set; }

            public string SnapshotJson { get; set; } = string.Empty;

            public string Start(
                long sessionId,
                string cameraId,
                int width,
                int height)
            {
                return
                    "{\"status\":\"ok\",\"state\":\"Starting\"," +
                    "\"errorCode\":\"\",\"message\":\"binding\"," +
                    "\"sessionId\":" + sessionId +
                    ",\"cameraId\":\"" + cameraId +
                    "\",\"facing\":\"rear\"," +
                    "\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":\"analysis_yuv_gpu_texture_bridge\"," +
                    "\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
            }

            public string Pause()
            {
                return SnapshotJson;
            }

            public string Resume()
            {
                return SnapshotJson;
            }

            public string Stop()
            {
                ++StopCount;
                return
                    "{\"status\":\"ok\",\"state\":\"Stopped\"," +
                    "\"errorCode\":\"\",\"message\":\"stopped\"," +
                    "\"sessionId\":0,\"cameraId\":\"\",\"facing\":\"unknown\"," +
                    "\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":\"analysis_yuv_gpu_texture_bridge\"," +
                    "\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
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
                return SnapshotJson;
            }

            public void Dispose()
            {
            }
        }
    }
}
