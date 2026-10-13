#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Performance;

namespace ReachyMini.Perception
{
    public sealed class ReachyOnDeviceLightweightTracker :
        IVisualTracker,
        IReachyPriorityDegradationTarget
    {
        public const int DefaultMaximumTrackingDimension = 640;

        private readonly IReachyTrackingDetectionBackend backend;
        private readonly ReachyStableTrackStore trackStore;
        private readonly int maximumTrackingDimension;
        private int priorityMaximumTrackingDimension;
        private long priorityMinimumIntervalNanoseconds;
        private long lastPriorityAcceptedTimestampNanoseconds;
        private int inFlight;
        private int disposed;

        public ReachyOnDeviceLightweightTracker(
            string instanceId,
            IReachyTrackingDetectionBackend backend,
            ReachyStableTrackingPolicy? trackingPolicy = null,
            int maximumTrackingDimension =
                DefaultMaximumTrackingDimension)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }
            if (!backend.SupportsFaces && !backend.SupportsPeople)
            {
                throw new ArgumentException(
                    "The lightweight tracker backend must support faces or people.",
                    nameof(backend));
            }
            if (maximumTrackingDimension < 64 ||
                maximumTrackingDimension > 2048)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTrackingDimension));
            }

            this.backend = backend;
            this.maximumTrackingDimension =
                maximumTrackingDimension;
            priorityMaximumTrackingDimension = maximumTrackingDimension;
            trackStore = new ReachyStableTrackStore(trackingPolicy);
            Descriptor = new ProviderDescriptor(
                VisionProviderKind.LightweightTracker,
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                "On-device lightweight tracker",
                backend.BackendVersion,
                VisionProviderLocation.OnDevice);
            Capabilities = new TrackerCapabilities(
                supportsFaces: backend.SupportsFaces,
                supportsPeople: backend.SupportsPeople,
                supportsObjects: false,
                supportsMotion: false,
                consumesGpuFrames: true,
                supportsCancellation: true,
                maximumConcurrentOperations: 1);
        }

        public ProviderDescriptor Descriptor { get; }

        public TrackerCapabilities Capabilities { get; }

        public void ApplyPriorityDegradation(
            ReachyPriorityDegradationDecision decision)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }
            Volatile.Write(
                ref priorityMaximumTrackingDimension,
                Math.Min(
                    maximumTrackingDimension,
                    decision.TrackingMaximumDimension));
            Interlocked.Exchange(
                ref priorityMinimumIntervalNanoseconds,
                decision.TrackingMinimumIntervalNanoseconds);
        }

        public async ValueTask<TrackingResult> AnalyzeAsync(
            TrackingRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Unavailable,
                    Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The on-device lightweight tracker is busy; requests are not queued or retried.");
            }

            using ReachyPerformanceMeasurement measurement =
                ReachyPerformanceTelemetry.Measure(
                    ReachyPerformanceWorkload.LightweightTracking);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.Frame.IsObservationEligible)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.InvalidFrame,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "Tracking requires an observation-eligible transformed Reachy-eye frame.");
                }

                long minimumIntervalNanoseconds = Interlocked.Read(
                    ref priorityMinimumIntervalNanoseconds);
                long timestampNanoseconds =
                    request.Frame.Identity.SourceTimestampNanoseconds;
                long previousTimestampNanoseconds = Interlocked.Read(
                    ref lastPriorityAcceptedTimestampNanoseconds);
                if (minimumIntervalNanoseconds > 0L &&
                    previousTimestampNanoseconds > 0L &&
                    timestampNanoseconds > previousTimestampNanoseconds &&
                    timestampNanoseconds - previousTimestampNanoseconds <
                        minimumIntervalNanoseconds)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.Unavailable,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "Tracking analysis was deferred by the RMA-181 priority degradation policy; stale tracking content was not reused.");
                }
                Interlocked.Exchange(
                    ref lastPriorityAcceptedTimestampNanoseconds,
                    timestampNanoseconds);
                int trackingDimension = Volatile.Read(
                    ref priorityMaximumTrackingDimension);

                if (!request.Frame.Resources.TryGetResource<IReachyTrackingPixelSource>(
                        VisionResourceKind.Color,
                        out IReachyTrackingPixelSource? pixelSource) ||
                    pixelSource == null)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.Unavailable,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "The transformed frame has no bounded tracking pixel source.");
                }

                ReachyTrackingFramePixels pixels =
                    await pixelSource.StageAsync(
                        request.Frame,
                        trackingDimension,
                        cancellationToken).ConfigureAwait(false);
                if (!pixels.Identity.Matches(request.Frame.Identity))
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.ContractViolation,
                        Descriptor,
                        request,
                        requiresProviderReset: true,
                        "Tracking pixel staging changed frame identity.");
                }

                IReadOnlyList<ReachyTrackingDetection> detections =
                    await backend.DetectAsync(
                        pixels,
                        cancellationToken).ConfigureAwait(false);
                if (detections == null)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.ContractViolation,
                        Descriptor,
                        request,
                        requiresProviderReset: true,
                        "The on-device tracking backend returned a null detection list.");
                }

                var accepted = new List<ReachyTrackingDetection>(
                    detections.Count);
                for (int index = 0; index < detections.Count; ++index)
                {
                    ReachyTrackingDetection detection =
                        detections[index] ??
                        throw new InvalidOperationException(
                            "The on-device tracking backend returned a null detection.");
                    bool supported =
                        (detection.DetectionClass ==
                            ReachyTrackingDetectionClass.Face &&
                         Capabilities.SupportsFaces) ||
                        (detection.DetectionClass ==
                            ReachyTrackingDetectionClass.Person &&
                         Capabilities.SupportsPeople);
                    if (!supported)
                    {
                        return TrackingResult.Failure(
                            VisionOperationStatus.ContractViolation,
                            Descriptor,
                            request,
                            requiresProviderReset: true,
                            "The tracking backend returned an undeclared detection class.");
                    }
                    if (pixels.IsDetectionCenterValid(detection.Bounds))
                    {
                        accepted.Add(detection);
                    }
                }

                IReadOnlyList<TrackedObject> tracked =
                    trackStore.Update(request.Frame.Identity, accepted);
                return TrackingResult.Success(
                    Descriptor,
                    request,
                    tracked);
            }
            catch (OperationCanceledException)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Cancelled,
                    Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The on-device lightweight tracking request was cancelled.");
            }
            catch (Exception exception)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.ProviderFailure,
                    Descriptor,
                    request,
                    requiresProviderReset: true,
                    $"On-device lightweight tracking failed closed ({exception.GetType().Name}): {exception.Message}");
            }
            finally
            {
                Volatile.Write(ref inFlight, 0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            trackStore.Reset();
            await backend.DisposeAsync().ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyOnDeviceLightweightTracker));
            }
        }
    }
}
