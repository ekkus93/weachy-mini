#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public sealed class ProviderDescriptor
    {
        public ProviderDescriptor(
            VisionProviderKind kind,
            string providerId,
            string instanceId,
            string displayName,
            string version,
            VisionProviderLocation location)
        {
            if (!Enum.IsDefined(typeof(VisionProviderKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (!Enum.IsDefined(typeof(VisionProviderLocation), location))
            {
                throw new ArgumentOutOfRangeException(nameof(location));
            }

            Kind = kind;
            ProviderId = RequireText(providerId, nameof(providerId));
            InstanceId = RequireText(instanceId, nameof(instanceId));
            DisplayName = RequireText(displayName, nameof(displayName));
            Version = RequireText(version, nameof(version));
            Location = location;
        }

        public VisionProviderKind Kind { get; }

        public string ProviderId { get; }

        public string InstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }

        public VisionProviderLocation Location { get; }

        public bool RequiresNetworkDisclosure =>
            Location == VisionProviderLocation.LocalNetwork ||
            Location == VisionProviderLocation.Cloud;

        internal static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Provider contract text cannot be empty.",
                    name);
            }
            return value;
        }
    }

    public sealed class FrameSourceCapabilities
    {
        public FrameSourceCapabilities(
            bool supportsGpuColor,
            bool supportsGpuValidityMask,
            bool supportsCancellation,
            int maximumWidth,
            int maximumHeight,
            int maximumOutstandingFrames)
        {
            if (maximumWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumWidth));
            }
            if (maximumHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHeight));
            }
            if (maximumOutstandingFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutstandingFrames));
            }
            if (!supportsGpuColor || !supportsGpuValidityMask)
            {
                throw new ArgumentException(
                    "The RMA-110 frame source must expose GPU color and validity resources.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "The RMA-110 frame source must support cancellation.");
            }

            SupportsGpuColor = supportsGpuColor;
            SupportsGpuValidityMask = supportsGpuValidityMask;
            SupportsCancellation = supportsCancellation;
            MaximumWidth = maximumWidth;
            MaximumHeight = maximumHeight;
            MaximumOutstandingFrames = maximumOutstandingFrames;
        }

        public bool SupportsGpuColor { get; }

        public bool SupportsGpuValidityMask { get; }

        public bool SupportsCancellation { get; }

        public int MaximumWidth { get; }

        public int MaximumHeight { get; }

        public int MaximumOutstandingFrames { get; }
    }

    public sealed class TrackerCapabilities
    {
        public TrackerCapabilities(
            bool supportsFaces,
            bool supportsPeople,
            bool supportsObjects,
            bool supportsMotion,
            bool consumesGpuFrames,
            bool supportsCancellation,
            int maximumConcurrentOperations)
        {
            if (!supportsFaces &&
                !supportsPeople &&
                !supportsObjects &&
                !supportsMotion)
            {
                throw new ArgumentException(
                    "A tracker must declare at least one supported tracking capability.");
            }
            if (!consumesGpuFrames)
            {
                throw new ArgumentException(
                    "RMA-110 trackers must consume the transformed GPU frame contract.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-110 trackers must support cancellation.");
            }
            if (maximumConcurrentOperations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }

            SupportsFaces = supportsFaces;
            SupportsPeople = supportsPeople;
            SupportsObjects = supportsObjects;
            SupportsMotion = supportsMotion;
            ConsumesGpuFrames = consumesGpuFrames;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentOperations = maximumConcurrentOperations;
        }

        public bool SupportsFaces { get; }

        public bool SupportsPeople { get; }

        public bool SupportsObjects { get; }

        public bool SupportsMotion { get; }

        public bool ConsumesGpuFrames { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentOperations { get; }
    }

    public sealed class VisionLanguageCapabilities
    {
        public VisionLanguageCapabilities(
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            bool supportsCancellation,
            int maximumConcurrentOperations,
            int maximumPromptCharacters)
        {
            if (!supportsVisualQuestions && !supportsSceneDescription)
            {
                throw new ArgumentException(
                    "A VLM provider must declare at least one semantic capability.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-110 VLM providers must support cancellation.");
            }
            if (maximumConcurrentOperations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }
            if (maximumPromptCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPromptCharacters));
            }

            SupportsVisualQuestions = supportsVisualQuestions;
            SupportsSceneDescription = supportsSceneDescription;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentOperations = maximumConcurrentOperations;
            MaximumPromptCharacters = maximumPromptCharacters;
        }

        public bool SupportsVisualQuestions { get; }

        public bool SupportsSceneDescription { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentOperations { get; }

        public int MaximumPromptCharacters { get; }
    }

    public sealed class VisionProviderSelectionSnapshot
    {
        internal VisionProviderSelectionSnapshot(
            VisionProviderKind kind,
            string providerInstanceId,
            ulong epoch)
        {
            Kind = kind;
            ProviderInstanceId = ProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            if (epoch == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(epoch));
            }
            Epoch = epoch;
        }

        public VisionProviderKind Kind { get; }

        public string ProviderInstanceId { get; }

        public ulong Epoch { get; }
    }

    public sealed class VisionProviderSelection
    {
        private readonly object sync = new object();
        private VisionProviderSelectionSnapshot current;

        public VisionProviderSelection(ProviderDescriptor initialProvider)
        {
            if (initialProvider == null)
            {
                throw new ArgumentNullException(nameof(initialProvider));
            }
            current = new VisionProviderSelectionSnapshot(
                initialProvider.Kind,
                initialProvider.InstanceId,
                1UL);
        }

        public VisionProviderSelectionSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public VisionProviderSelectionSnapshot Select(
            ProviderDescriptor provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (sync)
            {
                if (provider.Kind != current.Kind)
                {
                    throw new ArgumentException(
                        "A provider selection cannot change provider kind.",
                        nameof(provider));
                }
                ulong nextEpoch = checked(current.Epoch + 1UL);
                current = new VisionProviderSelectionSnapshot(
                    provider.Kind,
                    provider.InstanceId,
                    nextEpoch);
                return current;
            }
        }

        public bool IsCurrent(VisionRequestContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (sync)
            {
                return context.ProviderKind == current.Kind &&
                    context.ProviderInstanceId == current.ProviderInstanceId &&
                    context.SelectionEpoch == current.Epoch;
            }
        }
    }

    public sealed class VisionRequestContext
    {
        public static readonly TimeSpan MaximumTimeout =
            TimeSpan.FromMinutes(5.0);

        public VisionRequestContext(
            string requestId,
            VisionProviderSelectionSnapshot selection,
            TimeSpan timeout)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    timeout,
                    "Vision operation timeouts must be in (0, 5 minutes].");
            }

            RequestId = ProviderDescriptor.RequireText(
                requestId,
                nameof(requestId));
            ProviderKind = selection.Kind;
            ProviderInstanceId = selection.ProviderInstanceId;
            SelectionEpoch = selection.Epoch;
            Timeout = timeout;
        }

        public string RequestId { get; }

        public VisionProviderKind ProviderKind { get; }

        public string ProviderInstanceId { get; }

        public ulong SelectionEpoch { get; }

        public TimeSpan Timeout { get; }
    }

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

    public sealed class FrameSourceResult
    {
        private FrameSourceResult(
            VisionOperationStatus status,
            string providerInstanceId,
            string requestId,
            ulong selectionEpoch,
            ReachyVisionFrame? frame,
            bool requiresProviderReset,
            string diagnostic)
        {
            Status = status;
            ProviderInstanceId = providerInstanceId;
            RequestId = requestId;
            SelectionEpoch = selectionEpoch;
            Frame = frame;
            RequiresProviderReset = requiresProviderReset;
            Diagnostic = diagnostic;
        }

        public VisionOperationStatus Status { get; }

        public string ProviderInstanceId { get; }

        public string RequestId { get; }

        public ulong SelectionEpoch { get; }

        public ReachyVisionFrame? Frame { get; }

        public bool RequiresProviderReset { get; }

        public string Diagnostic { get; }

        public bool Succeeded => Status == VisionOperationStatus.Succeeded;

        public static FrameSourceResult Success(
            ProviderDescriptor provider,
            VisionRequestContext context,
            ReachyVisionFrame frame)
        {
            return new FrameSourceResult(
                VisionOperationStatus.Succeeded,
                provider.InstanceId,
                context.RequestId,
                context.SelectionEpoch,
                frame ?? throw new ArgumentNullException(nameof(frame)),
                requiresProviderReset: false,
                "Frame acquired.");
        }

        public static FrameSourceResult Failure(
            VisionOperationStatus status,
            ProviderDescriptor provider,
            VisionRequestContext context,
            bool requiresProviderReset,
            string diagnostic)
        {
            RequireFailure(status);
            return new FrameSourceResult(
                status,
                provider.InstanceId,
                context.RequestId,
                context.SelectionEpoch,
                null,
                requiresProviderReset,
                ProviderDescriptor.RequireText(
                    diagnostic,
                    nameof(diagnostic)));
        }

        internal static void RequireFailure(VisionOperationStatus status)
        {
            if (status == VisionOperationStatus.Succeeded ||
                !Enum.IsDefined(typeof(VisionOperationStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
        }
    }

    public sealed class TrackingResult
    {
        private readonly ReadOnlyCollection<TrackedObject> objects;

        private TrackingResult(
            VisionOperationStatus status,
            string providerInstanceId,
            string requestId,
            ulong selectionEpoch,
            ReachyVisionFrameIdentity frameIdentity,
            IReadOnlyList<TrackedObject> objects,
            bool requiresProviderReset,
            string diagnostic)
        {
            Status = status;
            ProviderInstanceId = providerInstanceId;
            RequestId = requestId;
            SelectionEpoch = selectionEpoch;
            FrameIdentity = frameIdentity;
            var copy = new List<TrackedObject>(objects.Count);
            for (int index = 0; index < objects.Count; ++index)
            {
                copy.Add(objects[index] ?? throw new ArgumentException(
                    "Tracking results cannot contain null objects.",
                    nameof(objects)));
            }
            this.objects = copy.AsReadOnly();
            RequiresProviderReset = requiresProviderReset;
            Diagnostic = diagnostic;
        }

        public VisionOperationStatus Status { get; }

        public string ProviderInstanceId { get; }

        public string RequestId { get; }

        public ulong SelectionEpoch { get; }

        public ReachyVisionFrameIdentity FrameIdentity { get; }

        public IReadOnlyList<TrackedObject> Objects => objects;

        public bool RequiresProviderReset { get; }

        public string Diagnostic { get; }

        public bool Succeeded => Status == VisionOperationStatus.Succeeded;

        public static TrackingResult Success(
            ProviderDescriptor provider,
            TrackingRequest request,
            IReadOnlyList<TrackedObject> objects)
        {
            return new TrackingResult(
                VisionOperationStatus.Succeeded,
                provider.InstanceId,
                request.Context.RequestId,
                request.Context.SelectionEpoch,
                request.Frame.Identity,
                objects ?? throw new ArgumentNullException(nameof(objects)),
                requiresProviderReset: false,
                "Tracking completed.");
        }

        public static TrackingResult Failure(
            VisionOperationStatus status,
            ProviderDescriptor provider,
            TrackingRequest request,
            bool requiresProviderReset,
            string diagnostic)
        {
            FrameSourceResult.RequireFailure(status);
            return new TrackingResult(
                status,
                provider.InstanceId,
                request.Context.RequestId,
                request.Context.SelectionEpoch,
                request.Frame.Identity,
                Array.Empty<TrackedObject>(),
                requiresProviderReset,
                ProviderDescriptor.RequireText(
                    diagnostic,
                    nameof(diagnostic)));
        }
    }

    public sealed class VisionLanguageResult
    {
        private VisionLanguageResult(
            VisionOperationStatus status,
            string providerInstanceId,
            string requestId,
            ulong selectionEpoch,
            ReachyVisionFrameIdentity frameIdentity,
            string? text,
            bool requiresProviderReset,
            string diagnostic)
        {
            Status = status;
            ProviderInstanceId = providerInstanceId;
            RequestId = requestId;
            SelectionEpoch = selectionEpoch;
            FrameIdentity = frameIdentity;
            Text = text;
            RequiresProviderReset = requiresProviderReset;
            Diagnostic = diagnostic;
        }

        public VisionOperationStatus Status { get; }

        public string ProviderInstanceId { get; }

        public string RequestId { get; }

        public ulong SelectionEpoch { get; }

        public ReachyVisionFrameIdentity FrameIdentity { get; }

        public string? Text { get; }

        public bool RequiresProviderReset { get; }

        public string Diagnostic { get; }

        public bool Succeeded => Status == VisionOperationStatus.Succeeded;

        public static VisionLanguageResult Success(
            ProviderDescriptor provider,
            VisionLanguageRequest request,
            string text)
        {
            return new VisionLanguageResult(
                VisionOperationStatus.Succeeded,
                provider.InstanceId,
                request.Context.RequestId,
                request.Context.SelectionEpoch,
                request.Frame.Identity,
                ProviderDescriptor.RequireText(text, nameof(text)),
                requiresProviderReset: false,
                "Semantic analysis completed.");
        }

        public static VisionLanguageResult Failure(
            VisionOperationStatus status,
            ProviderDescriptor provider,
            VisionLanguageRequest request,
            bool requiresProviderReset,
            string diagnostic)
        {
            FrameSourceResult.RequireFailure(status);
            return new VisionLanguageResult(
                status,
                provider.InstanceId,
                request.Context.RequestId,
                request.Context.SelectionEpoch,
                request.Frame.Identity,
                null,
                requiresProviderReset,
                ProviderDescriptor.RequireText(
                    diagnostic,
                    nameof(diagnostic)));
        }
    }
}
