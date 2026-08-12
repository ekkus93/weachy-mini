#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Rendering;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyRma154VisualServoAcceptance
    {
        private sealed class SyntheticOpticalTargetFeedbackSource :
            IReachyVisualServoFeedbackSource
        {
            private readonly ReachyProductionAuthoritativeRuntime runtime;
            private readonly BoundedWorldModel worldModel;
            private readonly ReachyProductionVisualServoFeedbackSource
                productionFeedback;
            private ReachyCameraCalibrationProfile? calibration;
            private readonly ReachySimAuthoritativeStateFrame transformState;
            private ReachyVisualServoFeedbackSample? initialSample;
            private ReachyVector3D fixedPhonePixel;
            private bool hasFixedPhonePixel;
            private ulong sourceSequence;
            private ulong lastTransformAuthoritativeSequence;
            private double[]? lastMotionPositions;
            private ReachyCameraCoverageState previousCoverageState =
                ReachyCameraCoverageState.Unavailable;
            private long latestTimestampNanoseconds = 1_540_000_000L;

            internal SyntheticOpticalTargetFeedbackSource(
                ReachyProductionAuthoritativeRuntime runtime,
                BoundedWorldModel worldModel)
            {
                this.runtime = runtime ??
                    throw new ArgumentNullException(nameof(runtime));
                this.worldModel = worldModel ??
                    throw new ArgumentNullException(nameof(worldModel));
                if (!runtime.TryCreateAuthoritativeStateFrame(
                        out ReachySimAuthoritativeStateFrame createdTransformState))
                {
                    throw new InvalidOperationException(
                        "RMA-154 could not allocate its homography state frame.");
                }
                transformState = createdTransformState;
                productionFeedback =
                    new ReachyProductionVisualServoFeedbackSource(
                        runtime,
                        worldModel,
                        () => latestTimestampNanoseconds,
                        () => true);
            }

            internal int ProducedFrameCount { get; private set; }

            internal bool ObservedPhysicalMotion { get; private set; }

            internal bool ObservedPostMotionTransformedFrame { get; private set; }

            internal async Task<ReachyVisualServoFeedbackSample> InitializeAsync(
                CancellationToken cancellationToken)
            {
                if (initialSample != null)
                {
                    throw new InvalidOperationException(
                        "RMA-154 synthetic feedback was initialized twice.");
                }
                ReachyVisualServoFeedbackSample created =
                    await ProduceAsync(cancellationToken).ConfigureAwait(false);
                initialSample = created;
                return created;
            }

            public async ValueTask<ReachyVisualServoFeedbackSample> CaptureAsync(
                CancellationToken cancellationToken)
            {
                ReachyVisualServoFeedbackSample? cached = initialSample;
                if (cached != null)
                {
                    initialSample = null;
                    return cached;
                }
                return await ProduceAsync(cancellationToken).ConfigureAwait(false);
            }

            private async Task<ReachyVisualServoFeedbackSample> ProduceAsync(
                CancellationToken cancellationToken)
            {
                await WaitForNewTransformStateAsync(cancellationToken)
                    .ConfigureAwait(false);
                ReachyBehaviorMotionSnapshot transformMotion =
                    ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                        transformState);
                bool movedSincePrevious = UpdateMotionObservation(
                    transformMotion);

                if (calibration == null)
                {
                    calibration = CreateBaselineCalibration(transformState);
                }

                sourceSequence = checked(sourceSequence + 1UL);
                latestTimestampNanoseconds = checked(
                    latestTimestampNanoseconds + 20_000_000L);
                ReachyCameraHomographyPlan plan = BuildHomographyPlan(
                    sourceSequence,
                    latestTimestampNanoseconds);
                if (!hasFixedPhonePixel)
                {
                    double desiredOutputX =
                        InitialTargetCenterXNormalized * (ImageWidth - 1.0);
                    double desiredOutputY =
                        InitialTargetCenterYNormalized * (ImageHeight - 1.0);
                    if (!plan.TryMapReachyPixelToPhonePixel(
                            desiredOutputX,
                            desiredOutputY,
                            out fixedPhonePixel))
                    {
                        throw new InvalidOperationException(
                            "RMA-154 could not seed a valid edge target in the initial transformed frame.");
                    }
                    hasFixedPhonePixel = true;
                }

                ReachyVector3D transformed =
                    plan.PhoneToReachyPixels.Transform(fixedPhonePixel);
                if (transformed.Z <= 1.0e-9)
                {
                    throw new InvalidOperationException(
                        "RMA-154 synthetic optical target moved behind the transformed camera.");
                }
                double centerX = transformed.X / transformed.Z /
                    (ImageWidth - 1.0);
                double centerY = transformed.Y / transformed.Z /
                    (ImageHeight - 1.0);
                if (!IsFinite(centerX) || !IsFinite(centerY) ||
                    centerX <= TargetWidthNormalized * 0.5 ||
                    centerX >= 1.0 - TargetWidthNormalized * 0.5 ||
                    centerY <= TargetHeightNormalized * 0.5 ||
                    centerY >= 1.0 - TargetHeightNormalized * 0.5)
                {
                    throw new InvalidOperationException(
                        "RMA-154 synthetic target left the usable transformed image: " +
                        "x=" + centerX + ", y=" + centerY + ".");
                }

                ReachyCameraCoverageMeasurement cameraCoverage =
                    ReachyCameraValidCoverageCalculator.Calculate(plan);
                ReachyVisionCoverage visionCoverage =
                    CreateVisionCoverage(cameraCoverage);
                var frameIdentity = new ReachyVisionFrameIdentity(
                    CameraId,
                    SourceSessionId,
                    sourceSequence,
                    latestTimestampNanoseconds,
                    plan.AuthoritativeSequence,
                    plan.ContinuityId);
                var bounds = new NormalizedVisionBounds(
                    centerX - TargetWidthNormalized * 0.5,
                    centerY - TargetHeightNormalized * 0.5,
                    TargetWidthNormalized,
                    TargetHeightNormalized);
                var tracked = new TrackedObject(
                    LocalTrackId,
                    "face",
                    confidence: 0.99,
                    bounds: bounds);
                WorldModelUpdateResult update = worldModel.ApplyTracking(
                    new WorldModelTrackingBatch(
                        ProviderId,
                        frameIdentity,
                        visionCoverage,
                        new[] { tracked }));
                if (!update.Accepted)
                {
                    throw new InvalidOperationException(
                        "RMA-154 world-model update failed: " +
                        update.Status + " / " + update.Diagnostic);
                }

                ++ProducedFrameCount;
                if (movedSincePrevious)
                {
                    ObservedPhysicalMotion = true;
                    ObservedPostMotionTransformedFrame = true;
                }

                return await productionFeedback.CaptureAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            private async Task WaitForNewTransformStateAsync(
                CancellationToken cancellationToken)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (runtime.TryCaptureLatestAuthoritativeState(
                            transformState) &&
                        transformState.Sequence >
                            lastTransformAuthoritativeSequence)
                    {
                        lastTransformAuthoritativeSequence =
                            transformState.Sequence;
                        return;
                    }
                    await Task.Delay(
                            StateAdvanceDelayMilliseconds,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            private bool UpdateMotionObservation(
                ReachyBehaviorMotionSnapshot current)
            {
                bool moved = false;
                if (lastMotionPositions != null)
                {
                    for (int index = ReachyBehaviorPlannerActuators.BodyYaw;
                        index <= ReachyBehaviorPlannerActuators.Stewart6;
                        ++index)
                    {
                        if (Math.Abs(
                                current.PositionsRadians[index] -
                                lastMotionPositions[index]) >=
                            MotionThresholdRadians)
                        {
                            moved = true;
                            break;
                        }
                    }
                }

                lastMotionPositions = new double[
                    ReachyBehaviorPlannerActuators.Count];
                for (int index = 0;
                    index < ReachyBehaviorPlannerActuators.Count;
                    ++index)
                {
                    lastMotionPositions[index] =
                        current.PositionsRadians[index];
                }
                return moved;
            }

            private ReachyCameraHomographyPlan BuildHomographyPlan(
                ulong nextSourceSequence,
                long timestampNanoseconds)
            {
                ReachyCameraCalibrationProfile activeCalibration = calibration ??
                    throw new InvalidOperationException(
                        "RMA-154 synthetic camera calibration is unavailable.");
                ReachySimBodyPoseSnapshot cameraPose =
                    FindCameraPose(transformState);
                var cameraQuaternion = new ReachyQuaternionD(
                    cameraPose.QuaternionX,
                    cameraPose.QuaternionY,
                    cameraPose.QuaternionZ,
                    cameraPose.QuaternionW);
                ReachyPhoneOpticalOrientationSample phoneOrientation =
                    ReachyPhoneOpticalOrientationSample.Identity(
                        timestampNanoseconds);
                ReachyCameraRelativeRotationSample rotation =
                    ReachyCameraRelativeRotationCalculator.Calculate(
                        ReachyCameraMujocoOpticalBinding.PinnedReachyMini,
                        activeCalibration,
                        transformState.Layout.ModelHash,
                        transformState.Sequence,
                        transformState.SimulationTime,
                        transformState.ContinuityId,
                        cameraQuaternion,
                        phoneOrientation);
                ReachyCameraHomographyBuildResult build =
                    ReachyCameraHomographyCalculator.Build(
                        activeCalibration,
                        rotation,
                        SourceSessionId,
                        nextSourceSequence,
                        timestampNanoseconds,
                        CameraId,
                        ReachyDeviceCameraFacing.External,
                        ImageWidth,
                        ImageHeight);
                if (!build.Succeeded || build.Plan == null)
                {
                    throw new InvalidOperationException(
                        "RMA-154 homography build failed: " +
                        build.Status + " / " + build.Message);
                }
                return build.Plan;
            }

            private ReachyVisionCoverage CreateVisionCoverage(
                ReachyCameraCoverageMeasurement measurement)
            {
                ReachyCameraCoveragePolicy coveragePolicy =
                    ReachyCameraCoveragePolicy.EngineeringBaseline;
                ReachyCameraCoverageState state = coveragePolicy.Classify(
                    measurement.CoverageFraction,
                    previousCoverageState);
                previousCoverageState = state;
                bool shouldStop =
                    coveragePolicy.ShouldStopVisionDrivenTurning(
                        measurement.CoverageFraction,
                        state);
                return new ReachyVisionCoverage(
                    ToVisionCoverageState(state),
                    measurement.ValidPixelCount,
                    measurement.TotalPixelCount,
                    hasValidityMask: true,
                    shouldStopVisionDrivenTurning: shouldStop,
                    diagnostic: "rma154-physical-homography-coverage");
            }

            private static ReachySimBodyPoseSnapshot FindCameraPose(
                ReachySimAuthoritativeStateFrame state)
            {
                int match = -1;
                for (int index = 0; index < state.BodyPoseCount; ++index)
                {
                    if (state.GetBodyPose(index).BodyId !=
                        ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId)
                    {
                        continue;
                    }
                    if (match >= 0)
                    {
                        throw new InvalidOperationException(
                            "RMA-154 authoritative camera body is duplicated.");
                    }
                    match = index;
                }
                if (match < 0)
                {
                    throw new InvalidOperationException(
                        "RMA-154 authoritative camera body is missing.");
                }
                return state.GetBodyPose(match);
            }

            private static ReachyCameraCalibrationProfile
                CreateBaselineCalibration(
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
                ReachyQuaternionD baselineReachyFromPhoneRotation =
                    QuaternionFromRotationMatrix(baselineReachyFromPhone);

                var normalization = new ReachyCameraImageNormalization(
                    ImageWidth,
                    ImageHeight,
                    cropLeft: 0,
                    cropTop: 0,
                    cropWidth: ImageWidth,
                    cropHeight: ImageHeight,
                    clockwiseRotationDegrees: 0,
                    mirrorHorizontally: false);
                ReachyCameraIntrinsicMatrix intrinsics =
                    ReachyCameraIntrinsicMatrix.CreatePinhole(
                        ImageWidth,
                        ImageHeight,
                        FocalLengthPixels,
                        FocalLengthPixels,
                        principalPointX: (ImageWidth - 1.0) * 0.5,
                        principalPointY: (ImageHeight - 1.0) * 0.5);
                return new ReachyCameraCalibrationProfile(
                    ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                    profileId: "rma154-physical-acceptance",
                    cameraId: CameraId,
                    facing: ReachyDeviceCameraFacing.External,
                    provenance:
                        ReachyCameraCalibrationProvenance.UncalibratedEstimate,
                    provenanceDetail:
                        "Deterministic RMA-154 physical acceptance optics.",
                    sourceReference:
                        "docs/RMA_154_VISUAL_SERVO_GAZE_LOOP_SPEC_2026-08-12.md",
                    modelCompatibility:
                        ReachyCameraMujocoOpticalBinding.OfficialModelCompatibility,
                    createdUtc: DateTimeOffset.UnixEpoch,
                    imageNormalization: normalization,
                    phoneIntrinsics: intrinsics,
                    reachyIntrinsics: intrinsics,
                    neutralReachyFromPhoneRotation:
                        baselineReachyFromPhoneRotation);
            }

            private static ReachyQuaternionD QuaternionFromRotationMatrix(
                ReachyMatrix3x3 rotation)
            {
                if (!rotation.IsProperRotation())
                {
                    throw new InvalidOperationException(
                        "RMA-154 baseline camera alignment is not a proper rotation.");
                }

                double trace = rotation.M00 + rotation.M11 + rotation.M22;
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
                        1.0 + rotation.M00 - rotation.M11 - rotation.M22);
                    w = (rotation.M21 - rotation.M12) / scale;
                    x = 0.25 * scale;
                    y = (rotation.M01 + rotation.M10) / scale;
                    z = (rotation.M02 + rotation.M20) / scale;
                }
                else if (rotation.M11 > rotation.M22)
                {
                    double scale = 2.0 * Math.Sqrt(
                        1.0 + rotation.M11 - rotation.M00 - rotation.M22);
                    w = (rotation.M02 - rotation.M20) / scale;
                    x = (rotation.M01 + rotation.M10) / scale;
                    y = 0.25 * scale;
                    z = (rotation.M12 + rotation.M21) / scale;
                }
                else
                {
                    double scale = 2.0 * Math.Sqrt(
                        1.0 + rotation.M22 - rotation.M00 - rotation.M11);
                    w = (rotation.M10 - rotation.M01) / scale;
                    x = (rotation.M02 + rotation.M20) / scale;
                    y = (rotation.M12 + rotation.M21) / scale;
                    z = 0.25 * scale;
                }
                return new ReachyQuaternionD(x, y, z, w);
            }

            private static VisionCoverageState ToVisionCoverageState(
                ReachyCameraCoverageState state)
            {
                return state switch
                {
                    ReachyCameraCoverageState.Normal =>
                        VisionCoverageState.Normal,
                    ReachyCameraCoverageState.Degraded =>
                        VisionCoverageState.Degraded,
                    ReachyCameraCoverageState.Unusable =>
                        VisionCoverageState.Unusable,
                    ReachyCameraCoverageState.Unavailable =>
                        throw new InvalidOperationException(
                            "RMA-154 cannot publish unavailable transformed " +
                            "coverage with measured pixels."),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(state),
                        state,
                        "Unknown RMA-154 camera coverage state."),
                };
            }

            private static bool IsFinite(double value)
            {
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
        }

    }
}
