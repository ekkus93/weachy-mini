#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Perception;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma190CameraEndToEndAcceptance
    {
        private static async Task<(ReachyAndroidCameraAcquisition acquisition,
            ReachyAndroidCameraTextureBridge bridge,
            ReachyCameraTextureFrameDescriptor descriptor)> StartFrontCameraAsync(
            CancellationToken cancellationToken)
        {
            ReachyAndroidCameraAcquisition acquisition =
                UnityEngine.Object.FindAnyObjectByType<ReachyAndroidCameraAcquisition>() ??
                throw new InvalidOperationException(
                    "The shipping CameraX acquisition component is unavailable.");
            ReachyAndroidCameraTextureBridge bridge =
                acquisition.GetComponent<ReachyAndroidCameraTextureBridge>() ??
                throw new InvalidOperationException(
                    "The shipping camera texture bridge is unavailable.");

            acquisition.StartPreferred(ReachyCameraFacing.Front);
            int waited = 0;
            while (waited < CameraWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyCameraAcquisitionSnapshot acquisitionSnapshot =
                    acquisition.State.Current;
                ReachyCameraTextureBridgeSnapshot textureSnapshot =
                    bridge.Current;
                ReachyCameraTextureFrameDescriptor? descriptor =
                    textureSnapshot.Frame;
                if (acquisitionSnapshot.State ==
                        ReachyCameraAcquisitionState.Running &&
                    acquisitionSnapshot.RequestedFacing ==
                        ReachyDeviceCameraFacing.Front &&
                    textureSnapshot.HasTexture &&
                    descriptor != null &&
                    descriptor.LensFacing == ReachyDeviceCameraFacing.Front)
                {
                    return (acquisition, bridge, descriptor);
                }
                if (acquisitionSnapshot.State ==
                        ReachyCameraAcquisitionState.Faulted ||
                    acquisitionSnapshot.State ==
                        ReachyCameraAcquisitionState.Unavailable ||
                    acquisitionSnapshot.State ==
                        ReachyCameraAcquisitionState.PermissionRevoked)
                {
                    throw new InvalidOperationException(
                        "Front camera acquisition failed: " +
                        acquisitionSnapshot.Message);
                }
                await Task.Delay(50, cancellationToken)
                    .ConfigureAwait(true);
                waited += 50;
            }
            throw new TimeoutException(
                "Timed out waiting for a physical front-camera texture.");
        }

        private static ReachyCameraCalibrationProfile CreateAcceptanceCalibration(
            ReachyCameraTextureFrameDescriptor descriptor,
            ReachySimAuthoritativeStateFrame baselineState)
        {
            ReachySimBodyPoseSnapshot cameraPose =
                FindCameraPose(baselineState);
            var cameraQuaternion = new ReachyQuaternionD(
                cameraPose.QuaternionX,
                cameraPose.QuaternionY,
                cameraPose.QuaternionZ,
                cameraPose.QuaternionW);
            ReachyCameraMujocoOpticalBinding binding =
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            ReachyMatrix3x3 currentWorldFromOptical =
                cameraQuaternion.ToRotationMatrix() *
                binding.CameraBodyFromOptical;
            ReachyMatrix3x3 baselineReachyFromPhone =
                binding.NeutralMujocoWorldFromOptical.Transposed() *
                currentWorldFromOptical;

            var normalization = new ReachyCameraImageNormalization(
                descriptor.Width,
                descriptor.Height,
                descriptor.Crop.Left,
                descriptor.Crop.Top,
                descriptor.Crop.Width,
                descriptor.Crop.Height,
                descriptor.RotationDegrees,
                descriptor.Mirrored);
            double focal =
                Math.Max(descriptor.OutputWidth, descriptor.OutputHeight) *
                0.5;
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    descriptor.OutputWidth,
                    descriptor.OutputHeight,
                    focal,
                    focal,
                    (descriptor.OutputWidth - 1.0) * 0.5,
                    (descriptor.OutputHeight - 1.0) * 0.5);
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                CalibrationProfileId,
                descriptor.CameraId,
                descriptor.LensFacing,
                ReachyCameraCalibrationProvenance.UncalibratedEstimate,
                "Deterministic RMA-190 camera-pipeline acceptance fixture; " +
                    "not a physical calibration measurement.",
                "docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md",
                ReachyCameraMujocoOpticalBinding.OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                intrinsics,
                intrinsics,
                QuaternionFromRotationMatrix(
                    baselineReachyFromPhone));
        }

        private static ReachyQuaternionD QuaternionFromRotationMatrix(
            ReachyMatrix3x3 rotation)
        {
            if (!rotation.IsProperRotation())
            {
                throw new InvalidOperationException(
                    "Acceptance calibration rotation is invalid.");
            }
            double trace =
                rotation.M00 + rotation.M11 + rotation.M22;
            double x;
            double y;
            double z;
            double w;
            if (trace > 0.0)
            {
                double scale = 2.0 * Math.Sqrt(trace + 1.0);
                w = 0.25 * scale;
                x = (rotation.M21 - rotation.M12) / scale;
                y = (rotation.M02 - rotation.M20) / scale;
                z = (rotation.M10 - rotation.M01) / scale;
            }
            else if (rotation.M00 > rotation.M11 &&
                rotation.M00 > rotation.M22)
            {
                double scale = 2.0 * Math.Sqrt(
                    1.0 + rotation.M00 -
                    rotation.M11 - rotation.M22);
                w = (rotation.M21 - rotation.M12) / scale;
                x = 0.25 * scale;
                y = (rotation.M01 + rotation.M10) / scale;
                z = (rotation.M02 + rotation.M20) / scale;
            }
            else if (rotation.M11 > rotation.M22)
            {
                double scale = 2.0 * Math.Sqrt(
                    1.0 + rotation.M11 -
                    rotation.M00 - rotation.M22);
                w = (rotation.M02 - rotation.M20) / scale;
                x = (rotation.M01 + rotation.M10) / scale;
                y = 0.25 * scale;
                z = (rotation.M12 + rotation.M21) / scale;
            }
            else
            {
                double scale = 2.0 * Math.Sqrt(
                    1.0 + rotation.M22 -
                    rotation.M00 - rotation.M11);
                w = (rotation.M10 - rotation.M01) / scale;
                x = (rotation.M02 + rotation.M20) / scale;
                y = (rotation.M12 + rotation.M21) / scale;
                z = 0.25 * scale;
            }
            return new ReachyQuaternionD(x, y, z, w);
        }

        private static async Task<ReachyVisionFrame>
            CaptureTransformedFrameAsync(
                ReachyAndroidPerceptionDriver driver,
                ulong minimumAuthoritativeSequence,
                CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < FrameCaptureWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyVisionFrame? frame =
                    await driver.CaptureFrameForAnalysisAsync(
                            cancellationToken)
                        .ConfigureAwait(true);
                if (frame != null)
                {
                    if (frame.Identity.AuthoritativeSequence >=
                        minimumAuthoritativeSequence)
                    {
                        return frame;
                    }
                    await frame.DisposeAsync().ConfigureAwait(true);
                }
                await Task.Delay(20, cancellationToken)
                    .ConfigureAwait(true);
                waited += 20;
            }
            throw new TimeoutException(
                "Timed out waiting for a transformed Reachy-eye frame.");
        }
    }
}
