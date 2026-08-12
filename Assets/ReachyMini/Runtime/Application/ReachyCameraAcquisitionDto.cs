#nullable enable

using System;

namespace ReachyMini.AppState
{
    [Serializable]
    internal sealed class ReachyCameraAcquisitionEnvelope
    {
        public string status = string.Empty;
        public string state = string.Empty;
        public string errorCode = string.Empty;
        public string message = string.Empty;
        public long sessionId;
        public string cameraId = string.Empty;
        public string facing = string.Empty;
        public string analysisBackpressure = string.Empty;
        public string previewSink = string.Empty;
        public bool cpuPixelCopyPerformed;
        public ReachyCameraFrameDto? latestFrame;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameDto
    {
        public long sessionId;
        public long sequence;
        public long timestampNanoseconds;
        public string cameraId = string.Empty;
        public string facing = string.Empty;
        public int sensorOrientationDegrees;
        public int rotationDegrees;
        public int width;
        public int height;
        public ReachyCameraFrameCropDto? crop;
        public string pixelFormat = string.Empty;
        public ReachyCameraFrameIntrinsicsDto? intrinsics;
        public bool imagePlanesAccessed;
        public bool cpuPixelCopyPerformed;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameCropDto
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameIntrinsicsDto
    {
        public string source = string.Empty;
        public float fx;
        public float fy;
        public float cx;
        public float cy;
        public float skew;
        public string coordinateSpace = string.Empty;
        public int activeArrayLeft;
        public int activeArrayTop;
        public int activeArrayRight;
        public int activeArrayBottom;
        public string provenance = string.Empty;
    }
}
