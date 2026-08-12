#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public sealed class ReachyVisionFrameIdentity
    {
        public ReachyVisionFrameIdentity(
            string cameraId,
            ulong sourceSessionId,
            ulong sourceSequence,
            long sourceTimestampNanoseconds,
            ulong authoritativeSequence,
            uint continuityId)
        {
            CameraId = ProviderDescriptor.RequireText(
                cameraId,
                nameof(cameraId));
            if (sourceSessionId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceSessionId));
            }
            if (sourceSequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceSequence));
            }
            if (sourceTimestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceTimestampNanoseconds));
            }
            if (authoritativeSequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativeSequence));
            }
            if (continuityId == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(continuityId));
            }

            SourceSessionId = sourceSessionId;
            SourceSequence = sourceSequence;
            SourceTimestampNanoseconds = sourceTimestampNanoseconds;
            AuthoritativeSequence = authoritativeSequence;
            ContinuityId = continuityId;
        }

        public string CameraId { get; }

        public ulong SourceSessionId { get; }

        public ulong SourceSequence { get; }

        public long SourceTimestampNanoseconds { get; }

        public ulong AuthoritativeSequence { get; }

        public uint ContinuityId { get; }

        public bool Matches(ReachyVisionFrameIdentity? other)
        {
            return other != null &&
                CameraId == other.CameraId &&
                SourceSessionId == other.SourceSessionId &&
                SourceSequence == other.SourceSequence &&
                SourceTimestampNanoseconds ==
                    other.SourceTimestampNanoseconds &&
                AuthoritativeSequence == other.AuthoritativeSequence &&
                ContinuityId == other.ContinuityId;
        }
    }

    public sealed class ReachyVisionCoverage
    {
        public ReachyVisionCoverage(
            VisionCoverageState state,
            long validPixelCount,
            long totalPixelCount,
            bool hasValidityMask,
            bool shouldStopVisionDrivenTurning,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(VisionCoverageState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            bool unavailable = state == VisionCoverageState.Unavailable;
            if (unavailable)
            {
                if (validPixelCount != 0L ||
                    totalPixelCount != 0L ||
                    hasValidityMask ||
                    !shouldStopVisionDrivenTurning)
                {
                    throw new ArgumentException(
                        "Unavailable coverage must have no measurement or validity mask and must stop vision-driven turning.",
                        nameof(state));
                }
            }
            else
            {
                if (totalPixelCount <= 0L ||
                    validPixelCount < 0L ||
                    validPixelCount > totalPixelCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(validPixelCount),
                        "Coverage counts must satisfy 0 <= valid <= total.");
                }
                if (!hasValidityMask)
                {
                    throw new ArgumentException(
                        "Perception coverage must include an explicit validity mask.",
                        nameof(hasValidityMask));
                }
            }
            if (state == VisionCoverageState.Unusable &&
                !shouldStopVisionDrivenTurning)
            {
                throw new ArgumentException(
                    "Unusable coverage must stop vision-driven turning.",
                    nameof(shouldStopVisionDrivenTurning));
            }

            State = state;
            ValidPixelCount = validPixelCount;
            TotalPixelCount = totalPixelCount;
            HasValidityMask = hasValidityMask;
            ShouldStopVisionDrivenTurning =
                shouldStopVisionDrivenTurning;
            Diagnostic = ProviderDescriptor.RequireText(
                diagnostic,
                nameof(diagnostic));
        }

        public VisionCoverageState State { get; }

        public long ValidPixelCount { get; }

        public long TotalPixelCount { get; }

        public bool HasMeasurement => TotalPixelCount > 0L;

        public double Fraction =>
            HasMeasurement
                ? (double)ValidPixelCount / TotalPixelCount
                : 0.0;

        public bool HasValidityMask { get; }

        public bool CanCreateVisualObservations =>
            State == VisionCoverageState.Normal ||
            State == VisionCoverageState.Degraded;

        public bool ShouldStopVisionDrivenTurning { get; }

        public string Diagnostic { get; }
    }

    public interface IReachyVisionFrameResources : IAsyncDisposable
    {
        string OwnerId { get; }

        ulong Generation { get; }

        int Width { get; }

        int Height { get; }

        bool IsDisposed { get; }

        bool HasResource(VisionResourceKind kind);

        VisionPixelEncoding GetEncoding(VisionResourceKind kind);

        bool TryGetResource<TResource>(
            VisionResourceKind kind,
            out TResource? resource)
            where TResource : class;
    }

    public sealed class ReachyVisionFrame : IAsyncDisposable
    {
        private int disposed;

        public ReachyVisionFrame(
            VisionFrameOrigin origin,
            ReachyVisionFrameIdentity identity,
            ReachyVisionCoverage coverage,
            IReachyVisionFrameResources resources)
        {
            if (!Enum.IsDefined(typeof(VisionFrameOrigin), origin))
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }
            Identity = identity ??
                throw new ArgumentNullException(nameof(identity));
            Coverage = coverage ??
                throw new ArgumentNullException(nameof(coverage));
            Resources = resources ??
                throw new ArgumentNullException(nameof(resources));
            if (resources.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(resources));
            }
            if (resources.Width <= 0 || resources.Height <= 0)
            {
                throw new ArgumentException(
                    "Vision frame resources require positive dimensions.",
                    nameof(resources));
            }
            if (!resources.HasResource(VisionResourceKind.Color))
            {
                throw new ArgumentException(
                    "Vision frames require a color resource.",
                    nameof(resources));
            }

            long totalPixels = checked(
                (long)resources.Width * resources.Height);
            if (origin == VisionFrameOrigin.TransformedReachyEye)
            {
                if (!coverage.HasMeasurement ||
                    coverage.TotalPixelCount != totalPixels ||
                    !coverage.HasValidityMask ||
                    !resources.HasResource(VisionResourceKind.ValidityMask))
                {
                    throw new ArgumentException(
                        "Transformed frames require matching coverage and a validity-mask resource.",
                        nameof(coverage));
                }
            }
            else if (coverage.State != VisionCoverageState.Unavailable ||
                coverage.HasMeasurement ||
                coverage.HasValidityMask)
            {
                throw new ArgumentException(
                    "Raw debug frames must keep coverage explicitly unavailable.",
                    nameof(coverage));
            }

            Origin = origin;
        }

        public VisionFrameOrigin Origin { get; }

        public ReachyVisionFrameIdentity Identity { get; }

        public ReachyVisionCoverage Coverage { get; }

        public IReachyVisionFrameResources Resources { get; }

        public int Width => Resources.Width;

        public int Height => Resources.Height;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public bool IsObservationEligible =>
            !IsDisposed &&
            Origin == VisionFrameOrigin.TransformedReachyEye &&
            Coverage.HasValidityMask &&
            Coverage.CanCreateVisualObservations &&
            !Resources.IsDisposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            await Resources.DisposeAsync().ConfigureAwait(false);
        }
    }
}
