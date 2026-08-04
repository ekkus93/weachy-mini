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
        public void OwnedOmissionAndBusySignalKeepSessionButDisconnectStopsFailClosed()
        {
            GameObject gameObject =
                new GameObject("Rma091CameraOwnership");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new OwnershipDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new OwnershipAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);

                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong sessionId = acquisition.State.Current.SessionId;
                acquisitionPlatform.SnapshotJson = RunningSnapshot(
                    checked((long)sessionId));
                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));

                discoveryPlatform.OmitCamera = true;
                discovery.RefreshPermissionAndCapabilities();

                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(0));
                Assert.That(acquisition.DesiredActive, Is.True);
                Assert.That(
                    acquisition.State.Current.SessionId,
                    Is.EqualTo(sessionId));
                Assert.That(
                    acquisition.State.Current.CameraId,
                    Is.EqualTo("rear-0"));

                discoveryPlatform.OmitCamera = false;
                discoveryPlatform.Availability =
                    "in_use_or_unavailable";
                discovery.RefreshPermissionAndCapabilities();

                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(0));
                Assert.That(acquisition.DesiredActive, Is.True);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));
                Assert.That(
                    acquisition.State.Current.SessionId,
                    Is.EqualTo(sessionId));

                discoveryPlatform.Availability = "disconnected";
                discovery.RefreshPermissionAndCapabilities();

                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(1));
                Assert.That(acquisition.DesiredActive, Is.False);
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Unavailable));
                StringAssert.Contains(
                    "Disconnected",
                    acquisition.State.Current.Message);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static string RunningSnapshot(long sessionId)
        {
            return
                "{\"status\":\"ok\",\"state\":\"Running\",\"errorCode\":\"\"," +
                "\"message\":\"frame texture ready\",\"sessionId\":" + sessionId +
                ",\"cameraId\":\"rear-0\",\"facing\":\"rear\"," +
                "\"analysisBackpressure\":\"keep_only_latest\"," +
                "\"previewSink\":\"analysis_yuv_gpu_texture_bridge\"," +
                "\"cpuPixelCopyPerformed\":true,\"latestFrame\":{" +
                "\"sessionId\":" + sessionId +
                ",\"sequence\":1,\"timestampNanoseconds\":1000000," +
                "\"cameraId\":\"rear-0\",\"facing\":\"rear\"," +
                "\"sensorOrientationDegrees\":90,\"rotationDegrees\":0," +
                "\"width\":1280,\"height\":720," +
                "\"crop\":{\"left\":0,\"top\":0,\"right\":1280,\"bottom\":720}," +
                "\"pixelFormat\":\"YUV_420_888\"," +
                "\"intrinsics\":{\"source\":\"android_calibration\"," +
                "\"fx\":900.0,\"fy\":900.0,\"cx\":640.0,\"cy\":360.0," +
                "\"skew\":0.0,\"coordinateSpace\":\"active_sensor_array\"," +
                "\"activeArrayLeft\":0,\"activeArrayTop\":0," +
                "\"activeArrayRight\":4032,\"activeArrayBottom\":3024," +
                "\"provenance\":\"Camera2 calibration\"}," +
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

            public bool OmitCamera { get; set; }

            public string Availability { get; set; } = "available";

            public bool HasCameraPermission()
            {
                return true;
            }

            public bool ShouldShowCameraPermissionRationale()
            {
                return false;
            }

            public void RequestCameraPermission(Action granted, Action denied)
            {
                _ = denied;
                granted();
            }

            public string DiscoverCameraCapabilitiesJson()
            {
                if (OmitCamera)
                {
                    return
                        "{\"status\":\"ok\",\"errorCode\":\"\"," +
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
