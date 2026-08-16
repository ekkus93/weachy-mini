#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public enum LocalLlmGovernedGenerationStatus
    {
        ProviderCompleted = 0,
        ResourceSuspendedBeforeStart = 1,
        ResourceCancelledDuringGeneration = 2,
        SignalFailure = 3,
        Busy = 4,
        ResourceExhausted = 5,
    }

    public enum LocalLlmProviderAdmissionStatus
    {
        Ready = 0,
        Suspended = 1,
        SignalFailure = 2,
    }

    public interface ILocalLlmPhysicsBudgetSource
    {
        LocalLlmPhysicsBudgetState Capture();
    }

    public sealed class LocalLlmProviderAdmissionResult
    {
        internal LocalLlmProviderAdmissionResult(
            LocalLlmProviderAdmissionStatus status,
            LocalLlmGovernorDecision? decision,
            string detail)
        {
            Status = status;
            Decision = decision;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
        }

        public LocalLlmProviderAdmissionStatus Status { get; }
        public LocalLlmGovernorDecision? Decision { get; }
        public LocalLlmExecutionProfile? EffectiveProfile => Decision?.EffectiveProfile;
        public string Detail { get; }
        public bool Succeeded =>
            Status == LocalLlmProviderAdmissionStatus.Ready &&
            EffectiveProfile != null;
    }

    public sealed class LocalLlmGovernedGenerationResult
    {
        internal LocalLlmGovernedGenerationResult(
            LocalLlmGovernedGenerationStatus status,
            LocalLlmGovernorDecision? decision,
            LocalLlmGenerationResult? providerResult,
            string detail)
        {
            Status = status;
            Decision = decision;
            ProviderResult = providerResult;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
        }

        public LocalLlmGovernedGenerationStatus Status { get; }
        public LocalLlmGovernorDecision? Decision { get; }
        public LocalLlmGenerationResult? ProviderResult { get; }
        public string Detail { get; }
        public bool ProviderInvoked => ProviderResult != null;
        public bool Succeeded =>
            Status == LocalLlmGovernedGenerationStatus.ProviderCompleted &&
            ProviderResult?.Succeeded == true;
    }

    public sealed class LocalLlmGovernedGenerationCoordinator
    {
        private static readonly TimeSpan DefaultMonitorInterval =
            TimeSpan.FromMilliseconds(250.0);
        private static readonly TimeSpan MinimumMonitorInterval =
            TimeSpan.FromMilliseconds(25.0);
        private static readonly TimeSpan MaximumMonitorInterval =
            TimeSpan.FromSeconds(5.0);

        private readonly ILocalLlmGenerationExecutor executor;
        private readonly LocalLlmExecutionProfile baselineProfile;
        private readonly LocalLlmResourceGovernor governor;
        private readonly ILocalLlmResourceSignalSource resourceSignals;
        private readonly ILocalLlmPhysicsBudgetSource physicsBudget;
        private readonly TimeSpan monitorInterval;
        private readonly object decisionGate = new object();

        private LocalLlmGovernorDecision? currentDecision;
        private int activeGeneration;

        public LocalLlmGovernedGenerationCoordinator(
            LocalLlmProvider provider,
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceGovernor governor,
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            TimeSpan? monitorInterval = null)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            executor = new LocalLlmProviderExecutor(provider);
            this.baselineProfile = baselineProfile ??
                throw new ArgumentNullException(nameof(baselineProfile));
            this.governor = governor ?? throw new ArgumentNullException(nameof(governor));
            this.resourceSignals = resourceSignals ??
                throw new ArgumentNullException(nameof(resourceSignals));
            this.physicsBudget = physicsBudget ??
                throw new ArgumentNullException(nameof(physicsBudget));
            this.monitorInterval = monitorInterval ?? DefaultMonitorInterval;
            ValidateMonitorInterval(this.monitorInterval, nameof(monitorInterval));
        }

        internal LocalLlmGovernedGenerationCoordinator(
            ILocalLlmGenerationExecutor executor,
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceGovernor governor,
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            TimeSpan monitorInterval)
        {
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            this.baselineProfile = baselineProfile ??
                throw new ArgumentNullException(nameof(baselineProfile));
            this.governor = governor ?? throw new ArgumentNullException(nameof(governor));
            this.resourceSignals = resourceSignals ??
                throw new ArgumentNullException(nameof(resourceSignals));
            this.physicsBudget = physicsBudget ??
                throw new ArgumentNullException(nameof(physicsBudget));
            this.monitorInterval = monitorInterval;
            ValidateMonitorInterval(this.monitorInterval, nameof(monitorInterval));
        }

        /// <param name="pendingArtifactBytes">
        /// Bytes the caller is about to load and that the current memory signal therefore
        /// does not yet account for. Admission runs BEFORE the model is loaded, so an
        /// unreserved evaluation reports the memory of a process that has not paid for the
        /// artifact yet. That lets a device admit a profile which loading immediately
        /// invalidates: the load consumes the headroom, the next observation reports memory
        /// pressure, and the freshly loaded profile no longer fits the allowed envelope.
        /// Reserving the artifact up front makes admission answer the question that actually
        /// matters -- what is safe to run once this model is resident. Zero (the default)
        /// preserves the previous behaviour and must be used for every post-load evaluation,
        /// where the artifact is already reflected in the real signal and reserving again
        /// would double-count it. The reservation is deliberately conservative: it charges
        /// the full artifact size without assuming how much of it a given runtime maps
        /// lazily.
        /// </param>
        public static LocalLlmProviderAdmissionResult EvaluateAdmission(
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceGovernor governor,
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            long pendingArtifactBytes = 0L,
            int mandatoryPromptTokens = 0)
        {
            if (baselineProfile == null)
            {
                throw new ArgumentNullException(nameof(baselineProfile));
            }
            if (pendingArtifactBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(pendingArtifactBytes));
            }
            if (governor == null)
            {
                throw new ArgumentNullException(nameof(governor));
            }
            if (resourceSignals == null)
            {
                throw new ArgumentNullException(nameof(resourceSignals));
            }
            if (physicsBudget == null)
            {
                throw new ArgumentNullException(nameof(physicsBudget));
            }

            try
            {
                LocalLlmGovernorDecision decision = CaptureDecision(
                    baselineProfile,
                    governor,
                    resourceSignals,
                    physicsBudget,
                    pendingArtifactBytes,
                    mandatoryPromptTokens);
                return new LocalLlmProviderAdmissionResult(
                    decision.InferenceAllowed && decision.EffectiveProfile != null
                        ? LocalLlmProviderAdmissionStatus.Ready
                        : LocalLlmProviderAdmissionStatus.Suspended,
                    decision,
                    decision.Diagnostic);
            }
            catch (Exception exception)
            {
                return new LocalLlmProviderAdmissionResult(
                    LocalLlmProviderAdmissionStatus.SignalFailure,
                    null,
                    "Local LLM resource admission failed: " + exception.Message);
            }
        }

        public LocalLlmGovernorDecision? CurrentDecision
        {
            get
            {
                lock (decisionGate)
                {
                    return currentDecision;
                }
            }
        }

        public LocalLlmGovernorDecision EvaluateCurrentBudget()
        {
            LocalLlmGovernorDecision decision = CaptureDecision();
            PublishDecision(decision);
            return decision;
        }

        public async Task<LocalLlmGovernedGenerationResult> GenerateAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            if (Interlocked.CompareExchange(ref activeGeneration, 1, 0) != 0)
            {
                return Result(
                    LocalLlmGovernedGenerationStatus.Busy,
                    CurrentDecision,
                    null,
                    "The local LLM resource governor already has an active generation.");
            }

            try
            {
                LocalLlmGovernorDecision preflight;
                try
                {
                    preflight = CaptureDecision();
                    PublishDecision(preflight);
                }
                catch (Exception exception)
                {
                    return Result(
                        LocalLlmGovernedGenerationStatus.SignalFailure,
                        CurrentDecision,
                        null,
                        "Local LLM resource preflight failed: " + exception.Message);
                }

                if (!preflight.InferenceAllowed || preflight.EffectiveProfile == null)
                {
                    return Result(
                        LocalLlmGovernedGenerationStatus.ResourceSuspendedBeforeStart,
                        preflight,
                        null,
                        "Local LLM inference is suspended by the resource governor: " +
                            preflight.Diagnostic);
                }
                if (!ProfileFitsWithin(executor.ExecutionProfile, preflight.EffectiveProfile))
                {
                    return Result(
                        LocalLlmGovernedGenerationStatus.ResourceSuspendedBeforeStart,
                        preflight,
                        null,
                        "The loaded local LLM execution profile exceeds the current safe resource envelope; " +
                        "explicit provider recreation with the reported effective profile is required.");
                }

                using var resourceCancellation = new CancellationTokenSource();
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    resourceCancellation.Token);
                Task<LocalLlmGenerationResult> generationTask = executor.GenerateAsync(
                    request,
                    sink,
                    linkedCancellation.Token);

                bool resourceCancellationIssued = false;
                bool signalFailure = false;
                string cancellationDetail = string.Empty;
                LocalLlmGovernorDecision? cancellationDecision = null;

                while (!generationTask.IsCompleted)
                {
                    Task delay = Task.Delay(monitorInterval, CancellationToken.None);
                    Task completed = await Task.WhenAny(generationTask, delay).ConfigureAwait(false);
                    if (ReferenceEquals(completed, generationTask))
                    {
                        break;
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        LocalLlmGovernorDecision observed = CaptureDecision();
                        PublishDecision(observed);
                        if (!observed.InferenceAllowed || observed.EffectiveProfile == null ||
                            !ProfileFitsWithin(executor.ExecutionProfile, observed.EffectiveProfile))
                        {
                            resourceCancellationIssued = true;
                            cancellationDecision = observed;
                            cancellationDetail =
                                "Local LLM generation was cancelled because the current resource envelope " +
                                "became more restrictive than the loaded execution profile: " +
                                observed.Diagnostic;
                            string? cancelFailure = TryCancel(resourceCancellation);
                            if (cancelFailure != null)
                            {
                                signalFailure = true;
                                cancellationDetail = cancellationDetail +
                                    " Cancellation callback failure: " + cancelFailure;
                            }
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        signalFailure = true;
                        resourceCancellationIssued = true;
                        cancellationDetail =
                            "Local LLM generation was cancelled because resource monitoring failed: " +
                            exception.Message;
                        string? cancelFailure = TryCancel(resourceCancellation);
                        if (cancelFailure != null)
                        {
                            cancellationDetail = cancellationDetail +
                                " Cancellation callback failure: " + cancelFailure;
                        }
                        break;
                    }
                }

                LocalLlmGenerationResult providerResult =
                    await generationTask.ConfigureAwait(false);
                if (providerResult.Status == LocalLlmGenerationStatus.ResourceExhausted)
                {
                    governor.RecordOutOfMemory();
                    LocalLlmGovernorDecision exhausted = BuildOutOfMemoryDecision(CurrentDecision);
                    PublishDecision(exhausted);
                    return Result(
                        LocalLlmGovernedGenerationStatus.ResourceExhausted,
                        exhausted,
                        providerResult,
                        "Local LLM inference exhausted memory; the governor is latched suspended until three consecutive nominal observations.");
                }
                if (resourceCancellationIssued)
                {
                    return Result(
                        signalFailure
                            ? LocalLlmGovernedGenerationStatus.SignalFailure
                            : LocalLlmGovernedGenerationStatus.ResourceCancelledDuringGeneration,
                        cancellationDecision ?? CurrentDecision,
                        providerResult,
                        cancellationDetail);
                }

                return Result(
                    LocalLlmGovernedGenerationStatus.ProviderCompleted,
                    CurrentDecision,
                    providerResult,
                    providerResult.Detail);
            }
            finally
            {
                Volatile.Write(ref activeGeneration, 0);
            }
        }

        internal static bool ProfileFitsWithin(
            LocalLlmExecutionProfile loaded,
            LocalLlmExecutionProfile allowed)
        {
            if (loaded == null)
            {
                throw new ArgumentNullException(nameof(loaded));
            }
            if (allowed == null)
            {
                throw new ArgumentNullException(nameof(allowed));
            }

            return loaded.ContextTokens <= allowed.ContextTokens &&
                loaded.BatchTokens <= allowed.BatchTokens &&
                loaded.MicroBatchTokens <= allowed.MicroBatchTokens &&
                loaded.Threads <= allowed.Threads &&
                loaded.BatchThreads <= allowed.BatchThreads &&
                loaded.MaximumGeneratedTokens == allowed.MaximumGeneratedTokens &&
                loaded.Temperature == allowed.Temperature &&
                loaded.MinP == allowed.MinP &&
                loaded.Seed == allowed.Seed &&
                loaded.StreamQueueCapacity == allowed.StreamQueueCapacity &&
                loaded.MaximumConversationMessages == allowed.MaximumConversationMessages &&
                loaded.MaximumMessageCharacters == allowed.MaximumMessageCharacters &&
                loaded.MaximumResponseUtf8Bytes == allowed.MaximumResponseUtf8Bytes;
        }

        private LocalLlmGovernorDecision CaptureDecision()
        {
            // Every decision after the model is loaded uses the executor's measured
            // mandatory prompt cost, so throttling can never propose a context that cannot
            // hold a request.
            return CaptureDecision(
                baselineProfile,
                governor,
                resourceSignals,
                physicsBudget,
                pendingArtifactBytes: 0L,
                mandatoryPromptTokens: executor.MandatoryPromptTokens);
        }

        private static LocalLlmGovernorDecision CaptureDecision(
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceGovernor governor,
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            long pendingArtifactBytes = 0L,
            int mandatoryPromptTokens = 0)
        {
            LocalLlmPhysicsBudgetState physics = physicsBudget.Capture();
            LocalLlmResourceSnapshot snapshot = resourceSignals.Capture(physics);
            if (pendingArtifactBytes > 0L)
            {
                snapshot = ReserveAvailableMemory(snapshot, pendingArtifactBytes);
            }
            return governor.Evaluate(baselineProfile, snapshot, mandatoryPromptTokens);
        }

        private static LocalLlmResourceSnapshot ReserveAvailableMemory(
            LocalLlmResourceSnapshot snapshot,
            long reserveBytes)
        {
            if (snapshot.TotalMemoryBytes <= 0L)
            {
                // The memory signal is explicitly unavailable, and an unavailable signal
                // requires every memory field to stay zero. Charging a reservation against
                // it would invent a number the device never reported; the governor already
                // treats unknown memory as its own conservative condition.
                return snapshot;
            }

            long available = snapshot.AvailableMemoryBytes - reserveBytes;
            if (available < 0L)
            {
                available = 0L;
            }
            return new LocalLlmResourceSnapshot(
                snapshot.TotalMemoryBytes,
                available,
                snapshot.LowMemoryThresholdBytes,
                snapshot.SystemReportsLowMemory,
                snapshot.LogicalProcessorCount,
                snapshot.ThermalStatus,
                snapshot.PhysicsBudgetState,
                snapshot.RecentOutOfMemory);
        }

        private static LocalLlmGovernorDecision BuildOutOfMemoryDecision(
            LocalLlmGovernorDecision? previous)
        {
            LocalLlmDeviceProfile deviceProfile = previous?.DeviceProfile ??
                LocalLlmDeviceProfile.Select(0L, 0);
            LocalLlmGovernorReason reasons =
                (previous?.Reasons ?? LocalLlmGovernorReason.None) |
                LocalLlmGovernorReason.RecentOutOfMemory;
            return new LocalLlmGovernorDecision(
                LocalLlmGovernorMode.Suspended,
                reasons,
                deviceProfile,
                null,
                "Local LLM governor mode=Suspended; recent local inference out-of-memory is latched; explicit recovery is required after healthy observations.");
        }

        private static string? TryCancel(CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Cancel();
                return null;
            }
            catch (Exception exception)
            {
                return LocalLlmGenerationResult.BoundDiagnostic(exception.Message);
            }
        }

        private static void ValidateMonitorInterval(TimeSpan value, string parameterName)
        {
            if (value < MinimumMonitorInterval || value > MaximumMonitorInterval)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private void PublishDecision(LocalLlmGovernorDecision decision)
        {
            lock (decisionGate)
            {
                currentDecision = decision;
            }
        }

        private static LocalLlmGovernedGenerationResult Result(
            LocalLlmGovernedGenerationStatus status,
            LocalLlmGovernorDecision? decision,
            LocalLlmGenerationResult? providerResult,
            string detail)
        {
            return new LocalLlmGovernedGenerationResult(
                status,
                decision,
                providerResult,
                detail);
        }
    }

    internal interface ILocalLlmGenerationExecutor
    {
        LocalLlmExecutionProfile ExecutionProfile { get; }

        /// <summary>
        /// Tokens every request carries before any user input, measured from the loaded
        /// model's own tokenizer. The governor needs it so a throttled context is never
        /// shrunk below what a request physically requires.
        /// </summary>
        int MandatoryPromptTokens { get; }
        Task<LocalLlmGenerationResult> GenerateAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken);
    }

    internal sealed class LocalLlmProviderExecutor : ILocalLlmGenerationExecutor
    {
        private readonly LocalLlmProvider provider;

        internal LocalLlmProviderExecutor(LocalLlmProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public LocalLlmExecutionProfile ExecutionProfile => provider.ExecutionProfile;

        public int MandatoryPromptTokens => provider.MandatoryPromptTokens;

        public Task<LocalLlmGenerationResult> GenerateAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken)
        {
            return provider.GenerateAsync(request, sink, cancellationToken);
        }
    }
}
