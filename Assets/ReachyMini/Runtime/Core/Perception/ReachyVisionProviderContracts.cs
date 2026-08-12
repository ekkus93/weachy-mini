#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
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
