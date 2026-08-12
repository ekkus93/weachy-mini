#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraTextureBridge
    {
        public bool PumpOnceForTests()
        {
            return PumpOnce();
        }

        private void Update()
        {
            PumpOnce();
        }

        private bool PumpOnce()
        {
            if (disposed || acquisition == null || conversionMaterial == null)
            {
                return false;
            }

            ReachyCameraAcquisitionSnapshot acquisitionSnapshot =
                acquisition.State.Current;
            if (acquisitionSnapshot.State != ReachyCameraAcquisitionState.Running ||
                acquisitionSnapshot.SessionId == 0UL)
            {
                if (current.State == ReachyCameraTextureBridgeState.Ready)
                {
                    InvalidateOutput(
                        $"Camera acquisition is {acquisitionSnapshot.State}; no texture is sampleable.");
                }
                return false;
            }

            ulong afterSequence =
                lastUploadedSessionId == acquisitionSnapshot.SessionId
                    ? lastUploadedSequence
                    : 0UL;
            IReachyCameraTextureFrameLease? lease =
                acquisition.AcquireLatestTextureFrame(afterSequence);
            if (lease == null)
            {
                return false;
            }

            using (lease)
            {
                ReachyCameraTextureFrameDescriptor descriptor =
                    lease.Descriptor;
                if (!FrameMatchesActiveSession(
                        descriptor,
                        acquisitionSnapshot,
                        afterSequence))
                {
                    Publish(
                        current.State == ReachyCameraTextureBridgeState.Ready
                            ? ReachyCameraTextureBridgeState.Ready
                            : ReachyCameraTextureBridgeState.Waiting,
                        "Rejected a stale or mismatched detached camera texture frame.",
                        current.State == ReachyCameraTextureBridgeState.Ready
                            ? current.Frame
                            : null,
                        current.UploadedFrameCount,
                        checked(current.StaleFrameCount + 1UL));
                    return false;
                }

                try
                {
                    ValidateLease(lease);
                    EnsurePlaneTextures(descriptor);
                    UploadPlane(
                        RequireTexture(yTexture, "Y"),
                        lease.YBuffer,
                        lease.YLength,
                        "Y");
                    UploadPlane(
                        RequireTexture(uTexture, "U"),
                        lease.UBuffer,
                        lease.ULength,
                        "U");
                    UploadPlane(
                        RequireTexture(vTexture, "V"),
                        lease.VBuffer,
                        lease.VLength,
                        "V");
                    EnsureOutputTexture(descriptor);
                    ConfigureMaterial(descriptor);
                    Graphics.Blit(
                        RequireTexture(yTexture, "Y"),
                        RequireOutputTexture(),
                        conversionMaterial!);

                    lastUploadedSessionId = descriptor.SessionId;
                    lastUploadedSequence = descriptor.Sequence;
                    Publish(
                        ReachyCameraTextureBridgeState.Ready,
                        $"Uploaded camera texture frame {descriptor.Sequence} with timestamp {descriptor.TimestampNanoseconds} ns.",
                        descriptor,
                        checked(current.UploadedFrameCount + 1UL),
                        current.StaleFrameCount);
                    return true;
                }
                catch (Exception exception)
                {
                    InvalidateOutput(
                        $"camera_texture_upload_failed: {exception.Message}");
                    Publish(
                        ReachyCameraTextureBridgeState.Faulted,
                        $"camera_texture_upload_failed: {exception.Message}",
                        null,
                        current.UploadedFrameCount,
                        current.StaleFrameCount);
                    return false;
                }
            }
        }
    }
}
