using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public static class VisionProviderExecutor
    {
        public static async ValueTask<FrameSourceResult> AcquireFrameAsync(
            IReachyVisionFrameSource provider,
            VisionProviderSelection selection,
            FrameSourceRequest request,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ProviderDescriptor descriptor = provider.Descriptor;
            ValidateProvider(
                descriptor,
                VisionProviderKind.FrameSource,
                request.Context);
            VisionProviderSelectionSnapshot selectionSnapshot =
                selection.Read();
            if (!selectionSnapshot.Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                return FrameSourceResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Frame-source selection changed before invocation.");
            }

            Execution<FrameSourceResult> execution = await ExecuteAsync(
                token => provider.AcquireAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return FrameSourceResult.Failure(
                    MapStatus(execution.Status),
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    Diagnostic(execution),
                    execution.RequiresProviderReset);
            }

            FrameSourceResult result = execution.Value!;
            if (!selection.Read().Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                if (result.Frame != null)
                {
                    await result.Frame.DisposeAsync().ConfigureAwait(false);
                }
                return FrameSourceResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Frame-source selection changed while the request was in flight.");
            }
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    descriptor,
                    request.Context))
            {
                if (result.Frame != null)
                {
                    await result.Frame.DisposeAsync().ConfigureAwait(false);
                }
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Frame-source result identity did not match the request.",
                    requiresProviderReset: true);
            }
            if (result.Status != VisionOperationStatus.Succeeded)
            {
                if (result.Frame != null)
                {
                    await result.Frame.DisposeAsync().ConfigureAwait(false);
                    return FrameSourceResult.Failure(
                        VisionOperationStatus.ContractViolation,
                        descriptor.InstanceId,
                        request.Context.RequestId,
                        request.Context.SelectionEpoch,
                        "A failed frame-source result retained a frame lease.",
                        requiresProviderReset: true);
                }
                return result;
            }
            if (result.Frame == null)
            {
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "A successful frame-source result omitted its frame lease.",
                    requiresProviderReset: true);
            }

            string? frameFailure = result.Frame.ValidateForPurpose(
                request.Purpose,
                request.MinimumSourceSequence);
            if (frameFailure != null)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    frameFailure);
            }
            if (result.Frame.Width > provider.Capabilities.MaximumWidth ||
                result.Frame.Height > provider.Capabilities.MaximumHeight)
            {
                await result.Frame.DisposeAsync().ConfigureAwait(false);
                return FrameSourceResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Frame dimensions exceeded the provider capability declaration.",
                    requiresProviderReset: true);
            }
            return result;
        }

        public static async ValueTask<TrackingResult> TrackAsync(
            IVisualTracker provider,
            VisionProviderSelection selection,
            TrackingRequest request,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ProviderDescriptor descriptor = provider.Descriptor;
            ValidateProvider(
                descriptor,
                VisionProviderKind.LightweightTracker,
                request.Context);
            if (!provider.Capabilities.SupportsFaceTracking &&
                !provider.Capabilities.SupportsPersonTracking &&
                !provider.Capabilities.SupportsObjectTracking &&
                !provider.Capabilities.SupportsMotionTracking)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Unavailable,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Tracker advertises no supported tracking capability.");
            }
            string? frameFailure = request.Frame.ValidateForPurpose(
                request.Purpose,
                minimumSourceSequence: 0);
            if (frameFailure != null)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    frameFailure);
            }
            if (!selection.Read().Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Tracker selection changed before invocation.");
            }

            Execution<TrackingResult> execution = await ExecuteAsync(
                token => provider.AnalyzeAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return TrackingResult.Failure(
                    MapStatus(execution.Status),
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    Diagnostic(execution),
                    execution.RequiresProviderReset);
            }

            TrackingResult result = execution.Value!;
            if (!selection.Read().Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Tracker selection changed while the request was in flight.");
            }
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    descriptor,
                    request.Context))
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Tracker result identity did not match the request.",
                    requiresProviderReset: true);
            }
            return result;
        }

        public static async ValueTask<VisionLanguageResult>
            AnalyzeVisionLanguageAsync(
                IVisionLanguageProvider provider,
                VisionProviderSelection selection,
                VisionLanguageRequest request,
                CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ProviderDescriptor descriptor = provider.Descriptor;
            ValidateProvider(
                descriptor,
                VisionProviderKind.SemanticVisionLanguage,
                request.Context);
            if (request.Prompt.Length > provider.Capabilities.MaximumPromptCharacters)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Prompt length exceeded the provider capability declaration.");
            }
            if (descriptor.Location != VisionProviderLocation.OnDevice &&
                !request.NetworkDisclosureAcknowledged)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Unavailable,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Network disclosure must be acknowledged before invoking a remote vision provider.");
            }
            string? frameFailure = request.Frame.ValidateForPurpose(
                request.Purpose,
                minimumSourceSequence: 0);
            if (frameFailure != null)
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.InvalidFrame,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    frameFailure);
            }
            if (!selection.Read().Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Vision-language selection changed before invocation.");
            }

            Execution<VisionLanguageResult> execution = await ExecuteAsync(
                token => provider.AnalyzeAsync(request, token),
                request.Context.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.Status != ExecutionStatus.Completed)
            {
                return VisionLanguageResult.Failure(
                    MapStatus(execution.Status),
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    Diagnostic(execution),
                    execution.RequiresProviderReset);
            }

            VisionLanguageResult result = execution.Value!;
            if (!selection.Read().Matches(
                    descriptor.InstanceId,
                    request.Context.SelectionEpoch))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.Superseded,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Vision-language selection changed while the request was in flight.");
            }
            if (!Matches(
                    result.ProviderInstanceId,
                    result.RequestId,
                    result.SelectionEpoch,
                    descriptor,
                    request.Context))
            {
                return VisionLanguageResult.Failure(
                    VisionOperationStatus.ContractViolation,
                    descriptor.InstanceId,
                    request.Context.RequestId,
                    request.Context.SelectionEpoch,
                    "Vision-language result identity did not match the request.",
                    requiresProviderReset: true);
            }
            return result;
        }

        private static void ValidateProvider(
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
            where T : class
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

            public bool RequiresProviderReset =>
                Status == ExecutionStatus.TimedOut ||
                Status == ExecutionStatus.Faulted;

            public static Execution<T> Completed(T value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                return new Execution<T>(
                    ExecutionStatus.Completed,
                    value,
                    exception: null);
            }

            public static Execution<T> Cancelled()
            {
                return new Execution<T>(
                    ExecutionStatus.Cancelled,
                    value: null,
                    exception: null);
            }

            public static Execution<T> TimedOut()
            {
                return new Execution<T>(
                    ExecutionStatus.TimedOut,
                    value: null,
                    exception: null);
            }

            public static Execution<T> Faulted(Exception exception)
            {
                if (exception == null)
                {
                    throw new ArgumentNullException(nameof(exception));
                }
                return new Execution<T>(
                    ExecutionStatus.Faulted,
                    value: null,
                    exception);
            }
        }
    }
}
