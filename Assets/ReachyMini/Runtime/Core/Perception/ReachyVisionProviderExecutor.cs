#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public static class VisionProviderExecutor
    {
        public static async ValueTask<FrameSourceResult> AcquireFrameAsync(
            IReachyVisionFrameSource source,
            FrameSourceRequest request,
            VisionProviderSelection selection,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            ValidateDescriptor(
                source.Descriptor,
                VisionProviderKind.FrameSource,
                request.Context);
            if (source.Capabilities == null)
            {
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: true,
                    "Frame source capability metadata is unavailable.");
            }

            Execution<FrameSourceResult> execution = await ExecuteAsync(
                token => source.AcquireAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return FrameSourceResult.Failure(
                    MapStatus(execution.Status),
                    source.Descriptor,
                    request.Context,
                    execution.Status == ExecutionStatus.TimedOut ||
                        execution.Status == ExecutionStatus.Faulted,
                    Diagnostic(execution));
            }

            FrameSourceResult result = execution.Value ??
                throw new InvalidOperationException(
                    "Completed frame-source execution has no result.");
            if (!selection.IsCurrent(request.Context))
            {
                if (result.Frame != null)
                {
                    await result.Frame.DisposeAsync().ConfigureAwait(false);
                }
                return FrameSourceResult.Failure(
                    VisionOperationStatus.Superseded,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: false,
                    "The selected frame source changed before completion.");
            }
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    source.Descriptor,
                    request.Context))
            {
                if (result.Frame != null)
                {
                    await result.Frame.DisposeAsync().ConfigureAwait(false);
                }
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: true,
                    "Frame-source result identity does not match the request.");
            }
            if (!result.Succeeded)
            {
                return result;
            }
            if (result.Frame == null)
            {
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: true,
                    "Frame source reported success without a frame.");
            }
            if (request.Purpose != VisionFramePurpose.ExplicitRawDebug &&
                !result.Frame.IsObservationEligible)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: false,
                    "Perception requires a transformed, validity-bearing, observation-eligible frame.");
            }
            if (request.Purpose == VisionFramePurpose.ExplicitRawDebug &&
                result.Frame.Origin != VisionFrameOrigin.RawPhoneDebug)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: true,
                    "Explicit raw debug acquisition returned a transformed frame.");
            }
            if (result.Frame.Width > source.Capabilities.MaximumWidth ||
                result.Frame.Height > source.Capabilities.MaximumHeight)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: true,
                    "Frame dimensions exceed the source capability declaration.");
            }
            if (result.Frame.Identity.SourceSequence <
                request.MinimumSourceSequence)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    source.Descriptor,
                    request.Context,
                    requiresProviderReset: false,
                    "Frame source returned a sequence older than requested.");
            }
            return result;
        }

        public static async ValueTask<TrackingResult> TrackAsync(
            IVisualTracker tracker,
            TrackingRequest request,
            VisionProviderSelection selection,
            CancellationToken cancellationToken)
        {
            if (tracker == null)
            {
                throw new ArgumentNullException(nameof(tracker));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            ValidateDescriptor(
                tracker.Descriptor,
                VisionProviderKind.LightweightTracker,
                request.Context);
            if (tracker.Capabilities == null)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    tracker.Descriptor,
                    request,
                    requiresProviderReset: true,
                    "Tracker capability metadata is unavailable.");
            }
            if (!request.Frame.IsObservationEligible)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    tracker.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "Tracker input is not an eligible transformed Reachy-eye frame.");
            }

            Execution<TrackingResult> execution = await ExecuteAsync(
                token => tracker.AnalyzeAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return TrackingResult.Failure(
                    MapStatus(execution.Status),
                    tracker.Descriptor,
                    request,
                    execution.Status == ExecutionStatus.TimedOut ||
                        execution.Status == ExecutionStatus.Faulted,
                    Diagnostic(execution));
            }
            if (!selection.IsCurrent(request.Context))
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Superseded,
                    tracker.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The selected tracker changed before completion.");
            }

            TrackingResult result = execution.Value ??
                throw new InvalidOperationException(
                    "Completed tracker execution has no result.");
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    tracker.Descriptor,
                    request.Context) ||
                !request.Frame.Identity.Matches(result.FrameIdentity))
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    tracker.Descriptor,
                    request,
                    requiresProviderReset: true,
                    "Tracker result identity does not match the request frame and provider.");
            }
            return result;
        }

        public static async ValueTask<VisionLanguageResult> AnalyzeVisionLanguageAsync(
            IVisionLanguageProvider provider,
            VisionLanguageRequest request,
            VisionProviderSelection selection,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            ValidateDescriptor(
                provider.Descriptor,
                VisionProviderKind.SemanticVisionLanguage,
                request.Context);
            if (provider.Capabilities == null)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: true,
                    "VLM capability metadata is unavailable.");
            }
            if (request.Prompt.Length >
                provider.Capabilities.MaximumPromptCharacters)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The VLM prompt exceeds the provider capability limit.");
            }
            if (provider.Descriptor.RequiresNetworkDisclosure &&
                !request.NetworkDisclosureAcknowledged)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Unavailable,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "Network disclosure acknowledgement is required for this provider.");
            }
            if (!request.Frame.IsObservationEligible)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "VLM input is not an eligible transformed Reachy-eye frame.");
            }

            Execution<VisionLanguageResult> execution = await ExecuteAsync(
                token => provider.AnalyzeAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return VisionLanguageResult.Failure(
                    MapStatus(execution.Status),
                    provider.Descriptor,
                    request,
                    execution.Status == ExecutionStatus.TimedOut ||
                        execution.Status == ExecutionStatus.Faulted,
                    Diagnostic(execution));
            }
            if (!selection.IsCurrent(request.Context))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Superseded,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The selected VLM provider changed before completion.");
            }

            VisionLanguageResult result = execution.Value ??
                throw new InvalidOperationException(
                    "Completed VLM execution has no result.");
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    provider.Descriptor,
                    request.Context) ||
                !request.Frame.Identity.Matches(result.FrameIdentity))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: true,
                    "VLM result identity does not match the request frame and provider.");
            }
            if (result.Succeeded && string.IsNullOrWhiteSpace(result.Text))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    provider.Descriptor,
                    request,
                    requiresProviderReset: true,
                    "VLM provider reported success without semantic text.");
            }
            return result;
        }

        private static void ValidateDescriptor(
            ProviderDescriptor descriptor,
            VisionProviderKind expectedKind,
            VisionRequestContext context)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (descriptor.Kind != expectedKind ||
                context.ProviderKind != expectedKind ||
                descriptor.InstanceId != context.ProviderInstanceId)
            {
                throw new InvalidOperationException(
                    "Provider descriptor, request context, and interface kind do not match.");
            }
        }

        private static bool Matches(
            string providerInstanceId,
            string requestId,
            ulong selectionEpoch,
            ProviderDescriptor provider,
            VisionRequestContext context)
        {
            return providerInstanceId == provider.InstanceId &&
                requestId == context.RequestId &&
                selectionEpoch == context.SelectionEpoch;
        }

        private static VisionOperationStatus MapStatus(
            ExecutionStatus status)
        {
            switch (status)
            {
                case ExecutionStatus.Cancelled:
                    return VisionOperationStatus.Cancelled;
                case ExecutionStatus.TimedOut:
                    return VisionOperationStatus.TimedOut;
                case ExecutionStatus.Faulted:
                    return VisionOperationStatus.ProviderFailure;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static string Diagnostic<T>(Execution<T> execution)
        {
            switch (execution.Status)
            {
                case ExecutionStatus.Cancelled:
                    return "Vision operation was cancelled by the caller.";
                case ExecutionStatus.TimedOut:
                    return "Vision operation exceeded its explicit timeout; the provider must be reset before reuse.";
                case ExecutionStatus.Faulted:
                    return "Vision provider failed: " +
                        (execution.Exception?.GetType().Name ?? "unknown exception") +
                        ".";
                default:
                    throw new ArgumentOutOfRangeException(nameof(execution));
            }
        }

        private static async ValueTask<Execution<T>> ExecuteAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            where T : class
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Execution<T>.Cancelled();
            }

            using var providerCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            Task<T> providerTask;
            try
            {
                providerTask = operation(providerCancellation.Token).AsTask();
            }
            catch (Exception exception)
            {
                return Execution<T>.Faulted(exception);
            }

            Task timeoutTask = Task.Delay(timeout);
            Task callerCancellationTask = Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            Task completed = await Task.WhenAny(
                providerTask,
                timeoutTask,
                callerCancellationTask).ConfigureAwait(false);

            if (completed == providerTask)
            {
                try
                {
                    T value = await providerTask.ConfigureAwait(false);
                    return Execution<T>.Completed(value);
                }
                catch (OperationCanceledException exception)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Execution<T>.Cancelled();
                    }
                    return Execution<T>.Faulted(exception);
                }
                catch (Exception exception)
                {
                    return Execution<T>.Faulted(exception);
                }
            }

            providerCancellation.Cancel();
            Observe(providerTask);
            return cancellationToken.IsCancellationRequested
                ? Execution<T>.Cancelled()
                : Execution<T>.TimedOut();
        }

        private static void Observe<T>(Task<T> task)
        {
            _ = task.ContinueWith(
                static completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private enum ExecutionStatus
        {
            Completed = 0,
            Cancelled = 1,
            TimedOut = 2,
            Faulted = 3,
        }

        private sealed class Execution<T>
            where T : class
        {
            private Execution(
                ExecutionStatus status,
                T? value,
                Exception? exception)
            {
                Status = status;
                Value = value;
                Exception = exception;
            }

            public ExecutionStatus Status { get; }

            public T? Value { get; }

            public Exception? Exception { get; }

            public static Execution<T> Completed(T value)
            {
                return new Execution<T>(
                    ExecutionStatus.Completed,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);
            }

            public static Execution<T> Cancelled()
            {
                return new Execution<T>(
                    ExecutionStatus.Cancelled,
                    null,
                    null);
            }

            public static Execution<T> TimedOut()
            {
                return new Execution<T>(
                    ExecutionStatus.TimedOut,
                    null,
                    null);
            }

            public static Execution<T> Faulted(Exception exception)
            {
                return new Execution<T>(
                    ExecutionStatus.Faulted,
                    null,
                    exception ?? throw new ArgumentNullException(
                        nameof(exception)));
            }
        }
    }
}
