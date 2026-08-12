#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
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
