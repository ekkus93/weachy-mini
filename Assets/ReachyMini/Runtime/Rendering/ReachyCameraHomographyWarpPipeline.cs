#nullable enable

using System;
using ReachyMini.AppState;
using UnityEngine;

namespace ReachyMini.Rendering
{
    public enum ReachyCameraHomographyWarpStatus
    {
        Success = 0,
        SourceUnavailable = 1,
        PlanRejected = 2,
        GpuExecutionFailed = 3,
    }

    public sealed class ReachyCameraHomographyWarpResult
    {
        public ReachyCameraHomographyWarpResult(
            ReachyCameraHomographyWarpStatus status,
            ReachyCameraHomographyBuildStatus buildStatus,
            ReachyCameraHomographyGpuFrame? frame,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Homography execution requires diagnostics.",
                    nameof(message));
            }
            bool succeeded =
                status == ReachyCameraHomographyWarpStatus.Success;
            if (succeeded != (frame != null))
            {
                throw new ArgumentException(
                    "Homography execution status and GPU frame disagree.",
                    nameof(frame));
            }

            Status = status;
            BuildStatus = buildStatus;
            Frame = frame;
            Message = message;
        }

        public ReachyCameraHomographyWarpStatus Status { get; }

        public ReachyCameraHomographyBuildStatus BuildStatus { get; }

        public ReachyCameraHomographyGpuFrame? Frame { get; }

        public string Message { get; }

        public bool Succeeded =>
            Status == ReachyCameraHomographyWarpStatus.Success;
    }

    public sealed class ReachyCameraHomographyWarpPipeline : IDisposable
    {
        private readonly ReachyCameraHomographyWarpRenderer renderer;
        private bool disposed;

        public ReachyCameraHomographyWarpPipeline(Shader? shader = null)
        {
            renderer =
                new ReachyCameraHomographyWarpRenderer(shader);
        }

        public RenderTexture? ColorTexture => renderer.ColorTexture;

        public RenderTexture? ValidityTexture =>
            renderer.ValidityTexture;

        public ReachyCameraHomographyWarpResult Execute(
            ReachyAndroidCameraTextureBridge textureBridge,
            ReachyCameraCalibrationProfile calibration,
            ReachyCameraRelativeRotationSample rotation)
        {
            if (textureBridge == null)
            {
                throw new ArgumentNullException(nameof(textureBridge));
            }
            if (calibration == null)
            {
                throw new ArgumentNullException(nameof(calibration));
            }
            if (rotation == null)
            {
                throw new ArgumentNullException(nameof(rotation));
            }
            ThrowIfDisposed();

            ReachyCameraTextureBridgeSnapshot snapshot =
                textureBridge.Current;
            ReachyCameraTextureFrameDescriptor? descriptor =
                snapshot.Frame;
            Texture? source = textureBridge.OutputTexture;
            if (!snapshot.HasTexture ||
                descriptor == null ||
                source == null)
            {
                renderer.ResetOutputs();
                return new ReachyCameraHomographyWarpResult(
                    ReachyCameraHomographyWarpStatus.SourceUnavailable,
                    ReachyCameraHomographyBuildStatus
                        .InvalidFrameIdentity,
                    null,
                    "The RMA-092 normalized RGB texture is not currently sampleable.");
            }

            ReachyCameraHomographyBuildResult build =
                ReachyCameraHomographyCalculator.Build(
                    calibration,
                    rotation,
                    descriptor.SessionId,
                    descriptor.Sequence,
                    descriptor.TimestampNanoseconds,
                    descriptor.CameraId,
                    descriptor.LensFacing,
                    descriptor.OutputWidth,
                    descriptor.OutputHeight);
            if (!build.Succeeded)
            {
                renderer.ResetOutputs();
                return new ReachyCameraHomographyWarpResult(
                    ReachyCameraHomographyWarpStatus.PlanRejected,
                    build.Status,
                    null,
                    build.Message);
            }

            try
            {
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(source, build.Plan!);
                return new ReachyCameraHomographyWarpResult(
                    ReachyCameraHomographyWarpStatus.Success,
                    ReachyCameraHomographyBuildStatus.Success,
                    frame,
                    "GPU homography color and validity textures are ready.");
            }
            catch (Exception exception)
                when (exception is ArgumentException ||
                    exception is InvalidOperationException)
            {
                renderer.ResetOutputs();
                return new ReachyCameraHomographyWarpResult(
                    ReachyCameraHomographyWarpStatus
                        .GpuExecutionFailed,
                    ReachyCameraHomographyBuildStatus.Success,
                    null,
                    $"GPU homography execution failed closed: {exception.Message}");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            renderer.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyCameraHomographyWarpPipeline));
            }
        }
    }
}
