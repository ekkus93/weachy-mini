#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma103ValidCoverageContracts
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            Run();
        }

        internal static void Run()
        {
            IdentityCoverageIsExact();
            CoverageMatchesShaderPredicate();
            PolicyUsesHysteresisAndPreemptiveStop();
            StaleAndConflictingCoverageFailClosed();
            ContinuityResetAllowsSequenceRestart();
            ConsumerMetadataIsExplicit();
            Console.WriteLine(
                "RMA-103 valid coverage contracts passed.");
        }

        private static void IdentityCoverageIsExact()
        {
            ReachyCameraHomographyPlan plan = BuildPlan(
                8,
                6,
                8,
                6,
                ReachyMatrix3x3.Identity,
                sourceSessionId: 2UL,
                sourceSequence: 3UL,
                sourceTimestampNanoseconds: 100L,
                authoritativeSequence: 4UL,
                continuityId: 5U);
            ReachyCameraCoverageMeasurement measurement =
                ReachyCameraValidCoverageCalculator.Calculate(plan);

            Equal(48L, measurement.ValidPixelCount, "identity valid pixels");
            Equal(48L, measurement.TotalPixelCount, "identity total pixels");
            Near(1.0, measurement.CoverageFraction, "identity coverage");
        }

        private static void CoverageMatchesShaderPredicate()
        {
            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 10.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan plan = BuildPlan(
                23,
                17,
                19,
                13,
                rotation,
                sourceSessionId: 2UL,
                sourceSequence: 4UL,
                sourceTimestampNanoseconds: 200L,
                authoritativeSequence: 5UL,
                continuityId: 5U);
            ReachyCameraCoverageMeasurement measurement =
                ReachyCameraValidCoverageCalculator.Calculate(plan);

            long expected = 0L;
            for (int y = 0; y < plan.OutputHeight; ++y)
            {
                for (int x = 0; x < plan.OutputWidth; ++x)
                {
                    if (IsShaderValid(plan, x, y))
                    {
                        ++expected;
                    }
                }
            }

            Equal(
                expected,
                measurement.ValidPixelCount,
                "exact shader-valid pixel count");
            Equal(
                247L,
                measurement.TotalPixelCount,
                "independent output pixel count");
        }

        private static void PolicyUsesHysteresisAndPreemptiveStop()
        {
            ReachyCameraCoveragePolicy policy =
                ReachyCameraCoveragePolicy.EngineeringBaseline;

            Equal(
                ReachyCameraCoverageState.Normal,
                policy.Classify(
                    0.80,
                    ReachyCameraCoverageState.Unavailable),
                "normal entry");
            Equal(
                ReachyCameraCoverageState.Normal,
                policy.Classify(
                    0.70,
                    ReachyCameraCoverageState.Normal),
                "normal hysteresis retention");
            Equal(
                ReachyCameraCoverageState.Degraded,
                policy.Classify(
                    0.64,
                    ReachyCameraCoverageState.Normal),
                "normal exit");
            Equal(
                ReachyCameraCoverageState.Degraded,
                policy.Classify(
                    0.70,
                    ReachyCameraCoverageState.Degraded),
                "degraded hysteresis retention");
            Equal(
                ReachyCameraCoverageState.Unusable,
                policy.Classify(
                    0.25,
                    ReachyCameraCoverageState.Degraded),
                "unusable entry");
            Equal(
                ReachyCameraCoverageState.Unusable,
                policy.Classify(
                    0.34,
                    ReachyCameraCoverageState.Unusable),
                "unusable hysteresis retention");
            Equal(
                ReachyCameraCoverageState.Degraded,
                policy.Classify(
                    0.35,
                    ReachyCameraCoverageState.Unusable),
                "unusable exit");
            True(
                policy.ShouldStopVisionDrivenTurning(
                    0.35,
                    ReachyCameraCoverageState.Degraded),
                "turning stops before unusable entry");
            True(
                !policy.ShouldStopVisionDrivenTurning(
                    0.36,
                    ReachyCameraCoverageState.Degraded),
                "turning may continue above preemptive stop");
        }

        private static void StaleAndConflictingCoverageFailClosed()
        {
            var state = new ReachyCameraCoverageStateMachine();
            ReachyCameraCoverageMeasurement current =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        16,
                        12,
                        16,
                        12,
                        ReachyMatrix3x3.Identity,
                        8UL,
                        10UL,
                        1000L,
                        20UL,
                        3U));
            True(state.Publish(current).Succeeded, "initial publication");

            ReachyCameraCoverageMeasurement stale =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        16,
                        12,
                        16,
                        12,
                        ReachyMatrix3x3.Identity,
                        8UL,
                        9UL,
                        900L,
                        19UL,
                        3U));
            Equal(
                ReachyCameraCoveragePublishStatus.StaleFrame,
                state.Publish(stale).Status,
                "stale frame rejection");

            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 6.0)
                .ToRotationMatrix();
            ReachyCameraCoverageMeasurement conflicting =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        16,
                        12,
                        16,
                        12,
                        rotation,
                        8UL,
                        10UL,
                        1000L,
                        20UL,
                        3U));
            Equal(
                ReachyCameraCoveragePublishStatus.IdentityMismatch,
                state.Publish(conflicting).Status,
                "same identity with different coverage rejected");
            Equal(
                current.ValidPixelCount,
                state.Current.Measurement!.ValidPixelCount,
                "rejection preserves current snapshot");
        }

        private static void ContinuityResetAllowsSequenceRestart()
        {
            var state = new ReachyCameraCoverageStateMachine();
            ReachyCameraCoverageMeasurement beforeReset =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        12,
                        8,
                        12,
                        8,
                        ReachyMatrix3x3.Identity,
                        4UL,
                        100UL,
                        5000L,
                        100UL,
                        7U));
            True(
                state.Publish(beforeReset).Succeeded,
                "pre-reset publication");

            ReachyCameraCoverageMeasurement afterReset =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        12,
                        8,
                        12,
                        8,
                        ReachyMatrix3x3.Identity,
                        4UL,
                        1UL,
                        100L,
                        1UL,
                        8U));
            Equal(
                ReachyCameraCoveragePublishStatus.Accepted,
                state.Publish(afterReset).Status,
                "continuity permits sequence restart");
            Equal(
                8U,
                state.Current.Measurement!.ContinuityId,
                "new continuity retained");
        }

        private static void ConsumerMetadataIsExplicit()
        {
            var state = new ReachyCameraCoverageStateMachine();
            True(
                state.Current.ShouldStopVisionDrivenTurning,
                "unavailable coverage stops turning");
            True(
                !state.Current.CanCreateVisualObservations,
                "unavailable coverage blocks observations");
            True(
                state.Current.CoverageDisclosureRequired,
                "unavailable coverage requires disclosure");

            ReachyCameraCoverageMeasurement measurement =
                ReachyCameraValidCoverageCalculator.Calculate(
                    BuildPlan(
                        6,
                        4,
                        6,
                        4,
                        ReachyMatrix3x3.Identity,
                        1UL,
                        1UL,
                        100L,
                        1UL,
                        1U));
            ReachyCameraCoverageSnapshot snapshot =
                state.Publish(measurement).Snapshot;
            True(snapshot.HasValidityMask, "validity-mask contract");
            True(
                snapshot.CanCreateVisualObservations,
                "normal coverage enables bounded observations");
            True(
                !snapshot.ShouldStopVisionDrivenTurning,
                "normal coverage permits turning");
            True(
                !snapshot.CoverageDisclosureRequired,
                "normal coverage needs no degradation disclosure");
        }

        private static ReachyCameraHomographyPlan BuildPlan(
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight,
            ReachyMatrix3x3 reachyFromPhone,
            ulong sourceSessionId,
            ulong sourceSequence,
            long sourceTimestampNanoseconds,
            ulong authoritativeSequence,
            uint continuityId)
        {
            ReachyCameraIntrinsicMatrix phone =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    sourceWidth,
                    sourceHeight,
                    8.0,
                    8.0,
                    (sourceWidth - 1.0) * 0.5,
                    (sourceHeight - 1.0) * 0.5);
            ReachyCameraIntrinsicMatrix reachy =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    outputWidth,
                    outputHeight,
                    8.0 * outputWidth / sourceWidth,
                    8.0 * outputHeight / sourceHeight,
                    (outputWidth - 1.0) * 0.5,
                    (outputHeight - 1.0) * 0.5);
            var profile = new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "rma103-profile",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "RMA-103 contract calibration",
                "sha256:rma103-contract",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                new ReachyCameraImageNormalization(
                    sourceWidth,
                    sourceHeight,
                    0,
                    0,
                    sourceWidth,
                    sourceHeight,
                    0,
                    false),
                phone,
                reachy,
                ReachyQuaternionD.Identity);
            var rotation = new ReachyCameraRelativeRotationSample(
                0x1234UL,
                authoritativeSequence,
                authoritativeSequence * 0.002,
                continuityId,
                sourceTimestampNanoseconds,
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
            ReachyCameraHomographyBuildResult result =
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    rotation,
                    sourceSessionId,
                    sourceSequence,
                    sourceTimestampNanoseconds,
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    sourceWidth,
                    sourceHeight);
            True(result.Succeeded, result.Message);
            return result.Plan!;
        }

        private static bool IsShaderValid(
            ReachyCameraHomographyPlan plan,
            int outputX,
            int outputY)
        {
            ReachyVector3D projected =
                plan.ReachyToPhonePixels.Transform(
                    new ReachyVector3D(outputX, outputY, 1.0));
            if (projected.Z <=
                ReachyCameraValidCoverageCalculator
                    .ShaderDepthEpsilon)
            {
                return false;
            }
            double sourceX = projected.X / projected.Z;
            double sourceY = projected.Y / projected.Z;
            return sourceX >= 0.0 &&
                sourceX <= plan.SourceWidth - 1.0 &&
                sourceY >= 0.0 &&
                sourceY <= plan.SourceHeight - 1.0;
        }

        private static void Near(
            double expected,
            double actual,
            string label,
            double tolerance = 1.0e-12)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, received {actual}.");
            }
        }

        private static void Equal<T>(
            T expected,
            T actual,
            string label)
            where T : notnull
        {
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, received {actual}.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label);
            }
        }
    }
}
