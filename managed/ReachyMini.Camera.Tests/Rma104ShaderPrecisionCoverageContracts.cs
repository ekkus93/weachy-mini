#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma104ShaderPrecisionCoverageContracts
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            Run();
        }

        internal static void Run()
        {
            IdentityHomographyIsCanonicalAndFullyValid();
            CoverageMatchesQuantizedShaderPayload();
            Console.WriteLine(
                "RMA-104 shader-precision coverage contracts passed.");
        }

        private static void IdentityHomographyIsCanonicalAndFullyValid()
        {
            ReachyCameraHomographyPlan plan = BuildPlan(
                sourceWidth: 17,
                sourceHeight: 11,
                outputWidth: 17,
                outputHeight: 11,
                phoneFocalX: 14.0,
                phoneFocalY: 14.0,
                reachyFocalX: 14.0,
                reachyFocalY: 14.0,
                reachyFromPhone: ReachyMatrix3x3.Identity,
                sourceSequence: 1UL);
            Equal(
                ReachyMatrix3x3.Identity,
                plan.PhoneToReachyPixels,
                "canonical forward identity");
            Equal(
                ReachyMatrix3x3.Identity,
                plan.ReachyToPhonePixels,
                "canonical inverse identity");

            ReachyCameraCoverageMeasurement coverage =
                ReachyCameraValidCoverageCalculator.Calculate(plan);
            Equal(187L, coverage.ValidPixelCount, "identity valid count");
            Equal(187L, coverage.TotalPixelCount, "identity total count");
        }

        private static void CoverageMatchesQuantizedShaderPayload()
        {
            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 10.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan plan = BuildPlan(
                sourceWidth: 23,
                sourceHeight: 17,
                outputWidth: 19,
                outputHeight: 13,
                phoneFocalX: 8.0,
                phoneFocalY: 8.0,
                reachyFocalX: 8.0 * 19.0 / 23.0,
                reachyFocalY: 8.0 * 13.0 / 17.0,
                reachyFromPhone: rotation,
                sourceSequence: 2UL);
            ReachyCameraCoverageMeasurement coverage =
                ReachyCameraValidCoverageCalculator.Calculate(plan);
            long expected = CountShaderPayload(plan);
            Equal(
                expected,
                coverage.ValidPixelCount,
                "quantized shader-valid count");
            Equal(247L, coverage.TotalPixelCount, "output pixel count");
        }

        private static ReachyCameraHomographyPlan BuildPlan(
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight,
            double phoneFocalX,
            double phoneFocalY,
            double reachyFocalX,
            double reachyFocalY,
            ReachyMatrix3x3 reachyFromPhone,
            ulong sourceSequence)
        {
            ReachyCameraIntrinsicMatrix phone =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    sourceWidth,
                    sourceHeight,
                    phoneFocalX,
                    phoneFocalY,
                    (sourceWidth - 1.0) * 0.5,
                    (sourceHeight - 1.0) * 0.5);
            ReachyCameraIntrinsicMatrix reachy =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    outputWidth,
                    outputHeight,
                    reachyFocalX,
                    reachyFocalY,
                    (outputWidth - 1.0) * 0.5,
                    (outputHeight - 1.0) * 0.5);
            var profile = new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "rma104-shader-precision-profile",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "RMA-104 shader-precision regression calibration",
                "sha256:rma104-shader-precision",
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
            long timestampNanoseconds = checked(
                (long)sourceSequence * 100L);
            var rotation = new ReachyCameraRelativeRotationSample(
                0x1234UL,
                sourceSequence,
                sourceSequence * 0.002,
                2U,
                timestampNanoseconds,
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
            ReachyCameraHomographyBuildResult result =
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    rotation,
                    3UL,
                    sourceSequence,
                    timestampNanoseconds,
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    sourceWidth,
                    sourceHeight);
            True(result.Succeeded, result.Message);
            return result.Plan!;
        }

        private static long CountShaderPayload(
            ReachyCameraHomographyPlan plan)
        {
            ReachyMatrix3x3 matrix = QuantizeForShader(
                plan.ReachyToPhonePixels);
            long count = 0L;
            for (int y = 0; y < plan.OutputHeight; ++y)
            {
                for (int x = 0; x < plan.OutputWidth; ++x)
                {
                    ReachyVector3D projected = matrix.Transform(
                        new ReachyVector3D(x, y, 1.0));
                    if (projected.Z <=
                        ReachyCameraValidCoverageCalculator
                            .ShaderDepthEpsilon)
                    {
                        continue;
                    }
                    double sourceX = projected.X / projected.Z;
                    double sourceY = projected.Y / projected.Z;
                    if (sourceX >= 0.0 &&
                        sourceX <= plan.SourceWidth - 1.0 &&
                        sourceY >= 0.0 &&
                        sourceY <= plan.SourceHeight - 1.0)
                    {
                        ++count;
                    }
                }
            }
            return count;
        }

        private static ReachyMatrix3x3 QuantizeForShader(
            ReachyMatrix3x3 matrix)
        {
            return new ReachyMatrix3x3(
                (double)(float)matrix.M00,
                (double)(float)matrix.M01,
                (double)(float)matrix.M02,
                (double)(float)matrix.M10,
                (double)(float)matrix.M11,
                (double)(float)matrix.M12,
                (double)(float)matrix.M20,
                (double)(float)matrix.M21,
                (double)(float)matrix.M22);
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
