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
        CoverageRejected = 4,
    }

    public sealed class ReachyCameraHomographyWarpResult
    {
        public ReachyCameraHomographyWarpResult(
            ReachyCameraHomographyWarpStatus status,
            ReachyCameraHomographyBuildStatus buildStatus,
            ReachyCameraHomographyGpuFrame? frame,
            ReachyCameraCoverageSnapshot coverage,
            string message)
        {
            if (coverage == null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }
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
            if (succeeded != coverage.HasCoverage)
            {
                throw new ArgumentException(
                    "Homography execution status and coverage disagree.",
                    nameof(coverage));
            }

            Status = status;
            BuildStatus = buildStatus;
            Frame = frame;
            Coverage = coverage;
            Message = message;
        }

        public ReachyCameraHomographyWarpStatus Status { get; }

        public ReachyCameraHomographyBuildStatus BuildStatus { get; }

        public ReachyCameraHomographyGpuFrame? Frame { get; }

        public ReachyCameraCoverageSnapshot Coverage { get; }

        public string Message { get; }

        public bool Succeeded =>
            Status == ReachyCameraHomographyWarpStatus.Success;
    }

    public sealed class ReachyCameraHomographyWarpPipeline : IDisposable
    {
        private readonly ReachyCameraHomographyWarpRenderer renderer;
        private readonly ReachyCameraCoverageStateMachine coverageState;
        private bool disposed;

        public ReachyCameraHomographyWarpPipeline(
            Shader? shader = null,
            ReachyCameraCoveragePolicy? coveragePolicy = null)
        {
            renderer =
                new ReachyCameraHomographyWarpRenderer(shader);
            coverageState =
                new ReachyCameraCoverageStateMachine(coveragePolicy);
        }

        public RenderTexture? ColorTexture => renderer.ColorTexture;

        public RenderTexture? ValidityTexture =>
            renderer.ValidityTexture;

        public ReachyCameraCoverageSnapshot CurrentCoverage =>
            coverageState.Current;

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
                return Failure(
                    ReachyCameraHomographyWarpStatus.SourceUnavailable,
                    ReachyCameraHomographyBuildStatus
                        .InvalidFrameIdentity,
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
                return Failure(
                    ReachyCameraHomographyWarpStatus.PlanRejected,
                    build.Status,
                    build.Message);
            }

            ReachyCameraCoverageMeasurement measurement;
            try
            {
                measurement =
                    ReachyCameraValidCoverageCalculator.Calculate(
                        build.Plan!);
            }
            catch (Exception exception)
                when (exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is OverflowException)
            {
                return Failure(
                    ReachyCameraHomographyWarpStatus
                        .CoverageRejected,
                    ReachyCameraHomographyBuildStatus.Success,
                    $"Coverage calculation failed closed: {exception.Message}");
            }

            ReachyCameraCoveragePublishResult publication =
                coverageState.Publish(measurement);
            if (!publication.Succeeded)
            {
                return Failure(
                    ReachyCameraHomographyWarpStatus.CoverageRejected,
                    ReachyCameraHomographyBuildStatus.Success,
                    $"Coverage publication failed closed: {publication.Message}");
            }

            try
            {
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(
                        source,
                        build.Plan!,
                        publication.Snapshot);
                return new ReachyCameraHomographyWarpResult(
                    ReachyCameraHomographyWarpStatus.Success,
                    ReachyCameraHomographyBuildStatus.Success,
                    frame,
                    publication.Snapshot,
                    "GPU homography color, validity, and coverage metadata are ready.");
            }
            catch (Exception exception)
                when (exception is ArgumentException ||
                    exception is InvalidOperationException)
            {
                return Failure(
                    ReachyCameraHomographyWarpStatus
                        .GpuExecutionFailed,
                    ReachyCameraHomographyBuildStatus.Success,
                    $"GPU homography execution failed closed: {exception.Message}");
            }
        }

        public void Reset(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Coverage reset requires diagnostics.",
                    nameof(message));
            }
            ThrowIfDisposed();
            renderer.ResetOutputs();
            coverageState.Reset(message);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            renderer.Dispose();
            coverageState.Reset(
                "The homography pipeline has been disposed.");
        }

        private ReachyCameraHomographyWarpResult Failure(
            ReachyCameraHomographyWarpStatus status,
            ReachyCameraHomographyBuildStatus buildStatus,
            string message)
        {
            renderer.ResetOutputs();
            ReachyCameraCoverageSnapshot coverage =
                coverageState.Reset(message);
            return new ReachyCameraHomographyWarpResult(
                status,
                buildStatus,
                null,
                coverage,
                message);
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
