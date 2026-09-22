#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyRma190CameraEndToEndAcceptance
    {
        internal static async Task<ReachyApplicationHostBehaviour>
            WaitForShippingHostAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyApplicationHostBehaviour? host =
                    Object.FindAnyObjectByType<ReachyApplicationHostBehaviour>();
                if (host?.Host != null &&
                    (host.Host.Health.State == ReachyApplicationState.Ready ||
                     host.Host.Health.State == ReachyApplicationState.Degraded))
                {
                    return host;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        internal static async Task<ReachyProductionAuthoritativeRuntime>
            WaitForRuntimeAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyProductionAuthoritativeRuntime? runtime =
                    Object.FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>();
                if (runtime?.Status == ReachyProductionRuntimeStatus.Running)
                {
                    return runtime;
                }
                if (runtime?.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    throw new InvalidOperationException(
                        "Production runtime faulted before RMA-190 camera acceptance: " +
                        runtime.Fault);
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        internal static async Task<T> WaitForObjectAsync<T>(
            CancellationToken cancellationToken,
            string label)
            where T : Object
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                T? value = Object.FindAnyObjectByType<T>();
                if (value != null)
                {
                    return value;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        internal static async Task WaitForCameraStoppedAsync(
            ReachyAndroidCameraAcquisition acquisition,
            CancellationToken cancellationToken)
        {
            while (acquisition.State.Current.IsActive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                acquisition.RefreshNow();
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        private static async Task<ReachyCameraTextureFrameDescriptor>
            WaitForFrontTextureAsync(
                ReachyAndroidCameraAcquisition acquisition,
                ReachyAndroidCameraTextureBridge bridge,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                acquisition.RefreshNow();
                ReachyCameraAcquisitionSnapshot acquisitionState =
                    acquisition.State.Current;
                if (acquisitionState.State == ReachyCameraAcquisitionState.Faulted ||
                    acquisitionState.State == ReachyCameraAcquisitionState.Unavailable ||
                    acquisitionState.State == ReachyCameraAcquisitionState.PermissionRevoked)
                {
                    throw new InvalidOperationException(
                        "Front camera acquisition failed: " +
                        acquisitionState.State + " / " + acquisitionState.Message);
                }

                ReachyCameraTextureBridgeSnapshot texture = bridge.Current;
                ReachyCameraTextureFrameDescriptor? frame = texture.Frame;
                if (acquisitionState.State == ReachyCameraAcquisitionState.Running &&
                    texture.HasTexture && frame != null &&
                    frame.LensFacing == ReachyDeviceCameraFacing.Front &&
                    frame.SessionId == acquisitionState.SessionId)
                {
                    return frame;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        internal static async Task<ReachyVisionFrame> CaptureAcceptanceFrameAsync(
            ReachyAndroidPerceptionDriver driver,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReachyVisionFrame? frame =
                    await driver.CaptureFrameForAcceptanceAsync(cancellationToken);
                if (frame != null)
                {
                    return frame;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        internal static async Task<ReachySimAuthoritativeStateFrame>
            WaitForAuthoritativeStateAsync(
                ReachyProductionAuthoritativeRuntime runtime,
                ulong minimumSequenceExclusive,
                CancellationToken cancellationToken)
        {
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame frame))
            {
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance could not allocate an authoritative state frame.");
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runtime.TryCaptureLatestAuthoritativeState(frame) &&
                    frame.Sequence > minimumSequenceExclusive)
                {
                    return frame;
                }
                await Task.Delay(ReadyPollMilliseconds, cancellationToken);
            }
        }

        private static bool HasAvailableFrontCamera(
            ReachyCameraCapabilitySnapshot capabilities)
        {
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (camera.Facing == ReachyDeviceCameraFacing.Front &&
                    camera.Availability == ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal static ReachyCameraCalibrationProfile[] CopyProfiles(
            ReachyCameraCalibrationSnapshot snapshot)
        {
            var copy = new ReachyCameraCalibrationProfile[
                snapshot.Profiles.Count];
            for (int index = 0; index < copy.Length; ++index)
            {
                copy[index] = snapshot.Profiles[index];
            }
            return copy;
        }

        private static ReachyCameraCalibrationProfile CreateDeterministicFixture(
            ReachyCameraTextureFrameDescriptor descriptor,
            ReachySimBodyPoseSnapshot baselineCameraPose)
        {
            return CreateDeterministicFixture(
                descriptor,
                baselineCameraPose,
                ReachyDeviceCameraFacing.Front,
                FixtureProfileId,
                mirrorHorizontally: true,
                "Deterministic RMA-190 front-camera reprojection fixture; not a measured device calibration.");
        }

        internal static ReachyCameraCalibrationProfile CreateDeterministicFixture(
            ReachyCameraTextureFrameDescriptor descriptor,
            ReachySimBodyPoseSnapshot baselineCameraPose,
            ReachyDeviceCameraFacing facing,
            string profileId,
            bool mirrorHorizontally,
            string note)
        {
            int width = descriptor.OutputWidth;
            int height = descriptor.OutputHeight;
            double focalLength = Math.Max(width, height) * 0.75;
            var intrinsics = ReachyCameraIntrinsicMatrix.CreatePinhole(
                width,
                height,
                focalLength,
                focalLength,
                (width - 1.0) * 0.5,
                (height - 1.0) * 0.5);
            var normalization = new ReachyCameraImageNormalization(
                width,
                height,
                0,
                0,
                width,
                height,
                0,
                mirrorHorizontally);

            var cameraQuaternion = new ReachyQuaternionD(
                baselineCameraPose.QuaternionX,
                baselineCameraPose.QuaternionY,
                baselineCameraPose.QuaternionZ,
                baselineCameraPose.QuaternionW);
            ReachyCameraMujocoOpticalBinding binding =
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            ReachyMatrix3x3 currentWorldFromOptical =
                cameraQuaternion.ToRotationMatrix() *
                binding.CameraBodyFromOptical;
            ReachyMatrix3x3 baselineReachyFromPhone =
                binding.NeutralMujocoWorldFromOptical.Transposed() *
                currentWorldFromOptical;

            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                profileId,
                descriptor.CameraId,
                facing,
                ReachyCameraCalibrationProvenance.UncalibratedEstimate,
                note,
                "docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md",
                ReachyCameraMujocoOpticalBinding.OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                intrinsics,
                intrinsics,
                QuaternionFromRotationMatrix(baselineReachyFromPhone));
        }

        private static ReachyBehaviorTrajectoryPlan BuildCuriosityPlan(
            ReachySimAuthoritativeStateFrame state)
        {
            ReachyBehaviorMotionSnapshot motion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(state);
            ReachyBehaviorSafetySnapshot safety =
                ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot(
                    state,
                    normalControllerAvailable: true,
                    workspaceClear: true);
            var planner = new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            ReachyBaselineBehaviorLibrary library =
                ReachyBaselineBehaviorLibrary.CreateMobileDefault(planner);
            ReachyBaselineBehaviorPlanResult result = library.Plan(
                ReachyBaselineBehaviorRequest.Curiosity(),
                worldSnapshot: null,
                motion,
                safety,
                PlanningTimestampNanoseconds);
            ReachyBehaviorTrajectoryPlan? plan = result.PlannerResult.Plan;
            if (!result.PlannerResult.Succeeded || plan == null ||
                plan.Frames.Count == 0)
            {
                throw new InvalidOperationException(
                    "RMA-190 camera acceptance could not plan deterministic head rotation: " +
                    result.PlannerResult.DiagnosticCode + ".");
            }
            return plan;
        }

        internal static ReachySimBodyPoseSnapshot FindCameraPose(
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
                        "RMA-190 authoritative camera body is duplicated.");
                }
                match = index;
            }
            if (match < 0)
            {
                throw new InvalidOperationException(
                    "RMA-190 authoritative camera body is missing.");
            }
            return state.GetBodyPose(match);
        }

        private static double CameraRotationDeltaRadians(
            ReachySimBodyPoseSnapshot left,
            ReachySimBodyPoseSnapshot right)
        {
            var leftQ = new ReachyQuaternionD(
                left.QuaternionX,
                left.QuaternionY,
                left.QuaternionZ,
                left.QuaternionW);
            var rightQ = new ReachyQuaternionD(
                right.QuaternionX,
                right.QuaternionY,
                right.QuaternionZ,
                right.QuaternionW);
            double dot = Math.Abs(
                leftQ.X * rightQ.X +
                leftQ.Y * rightQ.Y +
                leftQ.Z * rightQ.Z +
                leftQ.W * rightQ.W);
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            return 2.0 * Math.Acos(dot);
        }

        private static ReachyQuaternionD QuaternionFromRotationMatrix(
            ReachyMatrix3x3 rotation)
        {
            if (!rotation.IsProperRotation())
            {
                throw new InvalidOperationException(
                    "RMA-190 fixture baseline alignment is not a proper rotation.");
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

        private static FrameEvidence RequireTransformedFrontFrame(
            ReachyVisionFrame frame,
            string cameraId,
            ulong sessionId,
            ulong minimumSourceSequenceExclusive,
            ulong minimumAuthoritativeSequenceExclusive,
            uint expectedContinuityId)
        {
            if (frame.Origin != VisionFrameOrigin.TransformedReachyEye ||
                !frame.IsObservationEligible ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(VisionResourceKind.ValidityMask))
            {
                throw new InvalidOperationException(
                    "RMA-190 camera capture did not produce an observation-eligible TransformedReachyEye frame with a validity mask.");
            }
            if (!string.Equals(
                    frame.Identity.CameraId,
                    cameraId,
                    StringComparison.Ordinal) ||
                frame.Identity.SourceSessionId != sessionId ||
                frame.Identity.SourceSequence <= minimumSourceSequenceExclusive ||
                frame.Identity.AuthoritativeSequence <=
                    minimumAuthoritativeSequenceExclusive ||
                frame.Identity.ContinuityId != expectedContinuityId)
            {
                throw new InvalidOperationException(
                    "RMA-190 transformed-frame identity did not advance on the expected front-camera session and authoritative continuity.");
            }
            return new FrameEvidence(
                frame.Identity.SourceSequence,
                frame.Identity.AuthoritativeSequence,
                frame.Identity.ContinuityId,
                frame.Width,
                frame.Height,
                frame.Coverage.Fraction,
                frame.Coverage.State.ToString());
        }

        private readonly struct FrameEvidence
        {
            public FrameEvidence(
                ulong sourceSequence,
                ulong authoritativeSequence,
                uint continuityId,
                int width,
                int height,
                double coverageFraction,
                string coverageState)
            {
                SourceSequence = sourceSequence;
                AuthoritativeSequence = authoritativeSequence;
                ContinuityId = continuityId;
                Width = width;
                Height = height;
                CoverageFraction = coverageFraction;
                CoverageState = coverageState;
            }

            public ulong SourceSequence { get; }
            public ulong AuthoritativeSequence { get; }
            public uint ContinuityId { get; }
            public int Width { get; }
            public int Height { get; }
            public double CoverageFraction { get; }
            public string CoverageState { get; }
        }

    }
}
