#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraTextureBridge
    {
        private void OnAcquisitionChanged(
            object? sender,
            ReachyCameraAcquisitionChangedEventArgs eventArgs)
        {
            ReachyCameraAcquisitionSnapshot snapshot = eventArgs.Snapshot;
            if (snapshot.State != ReachyCameraAcquisitionState.Running ||
                snapshot.SessionId == 0UL ||
                (lastUploadedSessionId != 0UL &&
                    snapshot.SessionId != lastUploadedSessionId))
            {
                InvalidateOutput(
                    $"Camera acquisition changed to {snapshot.State}; detached texture state was cleared.");
            }
        }

        private void OnDestroy()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (acquisition != null)
            {
                acquisition.State.Changed -= OnAcquisitionChanged;
            }
            acquisition = null;
            DestroyAllResources();
        }

        private bool FrameMatchesActiveSession(
            ReachyCameraTextureFrameDescriptor descriptor,
            ReachyCameraAcquisitionSnapshot snapshot,
            ulong afterSequence)
        {
            if (descriptor.SessionId != snapshot.SessionId ||
                !string.Equals(
                    descriptor.CameraId,
                    snapshot.CameraId,
                    StringComparison.Ordinal) ||
                descriptor.Sequence <= afterSequence)
            {
                return false;
            }
            ReachyCameraFrameMetadata? metadata = snapshot.LatestFrame;
            return metadata == null ||
                descriptor.Sequence >= metadata.Sequence;
        }

        private static void ValidateLease(
            IReachyCameraTextureFrameLease lease)
        {
            ReachyCameraTextureFrameDescriptor descriptor =
                lease.Descriptor;
            if (lease.YBuffer == IntPtr.Zero ||
                lease.UBuffer == IntPtr.Zero ||
                lease.VBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "A texture frame lease exposed a null plane pointer.");
            }
            if (lease.YLength != descriptor.YPlaneLength ||
                lease.ULength != descriptor.ChromaPlaneLength ||
                lease.VLength != descriptor.ChromaPlaneLength)
            {
                throw new InvalidOperationException(
                    "A texture frame lease exposed plane lengths that do not match its descriptor.");
            }
        }
    }
}
