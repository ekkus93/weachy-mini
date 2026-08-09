#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Validation
{
    /// <summary>
    /// Physical-acceptance-only proxy around the real reachy_llama managed runtime.
    /// It records bounded, non-content diagnostics for native call boundaries and
    /// samples the read-only native metrics API while a generation is active.
    /// It never changes generation configuration, retries a call, repairs output,
    /// substitutes a runtime, or suppresses a runtime failure.
    /// </summary>
    internal sealed class ReachyRma134DiagnosticLlmRuntime : ILocalLlmRuntime
    {
        private static readonly TimeSpan MetricsInterval = TimeSpan.FromSeconds(5);

        private readonly ReachyLlamaLocalLlmRuntime inner = new ReachyLlamaLocalLlmRuntime();
        private readonly ConcurrentQueue<ReachyRma134RuntimeDiagnostic> diagnostics =
            new ConcurrentQueue<ReachyRma134RuntimeDiagnostic>();
        private readonly object monitorSync = new object();

        private CancellationTokenSource? monitorCancellation;
        private Task? monitorTask;
        private long pollCount;
        private bool firstPollReturned;
        private bool disposed;

        internal bool TryDequeue(out ReachyRma134RuntimeDiagnostic? diagnostic)
        {
            return diagnostics.TryDequeue(out diagnostic);
        }

        public uint GetAbiVersion()
        {
            Emit("abi_call_entered", string.Empty);
            uint abi = inner.GetAbiVersion();
            Emit("abi_call_returned", "abi=" + abi.ToString(CultureInfo.InvariantCulture));
            return abi;
        }

        public LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors)
        {
            Emit("model_load_call_entered", "check_tensors=" + checkTensors.ToString().ToLowerInvariant());
            LocalLlmRuntimeLoadResult result = inner.LoadModel(fullPath, checkTensors);
            Emit(
                "model_load_call_returned",
                "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                " handle_nonzero=" + (result.ModelHandle != 0UL).ToString().ToLowerInvariant());
            return result;
        }

        public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
        {
            Emit("model_unload_call_entered", string.Empty);
            LocalLlmRuntimeCallResult result = inner.UnloadModel(modelHandle);
            Emit("model_unload_call_returned", StatusDetail(result));
            return result;
        }

        public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
            ulong modelHandle,
            string? chatTemplate,
            IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
        {
            Emit(
                "chat_template_call_entered",
                "messages=" + messages.Count.ToString(CultureInfo.InvariantCulture) +
                " embedded_template=" + (chatTemplate == null).ToString().ToLowerInvariant());
            LocalLlmRuntimeTemplateResult result = inner.ApplyChatTemplate(
                modelHandle,
                chatTemplate,
                messages);
            Emit(
                "chat_template_call_returned",
                "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                " prompt_chars=" + result.Prompt.Length.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        public LocalLlmRuntimeTokenCountResult CountTokens(ulong modelHandle, string prompt)
        {
            Emit(
                "token_count_call_entered",
                "prompt_chars=" + prompt.Length.ToString(CultureInfo.InvariantCulture));
            LocalLlmRuntimeTokenCountResult result = inner.CountTokens(modelHandle, prompt);
            Emit(
                "token_count_call_returned",
                "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                " tokens=" + result.TokenCount.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        public LocalLlmRuntimeStartResult StartConstrained(
            ulong modelHandle,
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot)
        {
            Emit(
                "constrained_start_call_entered",
                "context=" + profile.ContextTokens.ToString(CultureInfo.InvariantCulture) +
                " batch=" + profile.BatchTokens.ToString(CultureInfo.InvariantCulture) +
                " ubatch=" + profile.MicroBatchTokens.ToString(CultureInfo.InvariantCulture) +
                " max_gen=" + profile.MaximumGeneratedTokens.ToString(CultureInfo.InvariantCulture) +
                " threads=" + profile.Threads.ToString(CultureInfo.InvariantCulture) +
                " batch_threads=" + profile.BatchThreads.ToString(CultureInfo.InvariantCulture) +
                " grammar_chars=" + grammar.Length.ToString(CultureInfo.InvariantCulture) +
                " root_chars=" + grammarRoot.Length.ToString(CultureInfo.InvariantCulture));
            LocalLlmRuntimeStartResult result = inner.StartConstrained(
                modelHandle,
                prompt,
                profile,
                grammar,
                grammarRoot);
            Emit(
                "constrained_start_call_returned",
                "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                " handle_nonzero=" + (result.GenerationHandle != 0UL).ToString().ToLowerInvariant());
            if (result.Succeeded && result.GenerationHandle != 0UL)
            {
                StartMetricsMonitor(result.GenerationHandle);
            }
            return result;
        }

        public LocalLlmRuntimePollResult Poll(ulong generationHandle)
        {
            long currentPoll = Interlocked.Increment(ref pollCount);
            if (currentPoll == 1L)
            {
                Emit("first_poll_call_entered", string.Empty);
            }

            LocalLlmRuntimePollResult result = inner.Poll(generationHandle);
            if (!firstPollReturned)
            {
                firstPollReturned = true;
                Emit(
                    "first_poll_call_returned",
                    "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                    " kind=" + result.Kind.ToString());
            }
            if (result.Kind != LocalLlmRuntimePollKind.None || !result.Succeeded)
            {
                Emit(
                    "poll_event",
                    "poll=" + currentPoll.ToString(CultureInfo.InvariantCulture) +
                    " status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                    " kind=" + result.Kind.ToString() +
                    " sequence=" + result.Sequence.ToString(CultureInfo.InvariantCulture) +
                    " text_utf16_chars=" + result.Text.Length.ToString(CultureInfo.InvariantCulture));
            }
            return result;
        }

        public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
        {
            Emit("cancel_call_entered", string.Empty);
            LocalLlmRuntimeCallResult result = inner.Cancel(generationHandle);
            Emit("cancel_call_returned", StatusDetail(result));
            return result;
        }

        public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
        {
            Emit("terminal_metrics_call_entered", string.Empty);
            LocalLlmRuntimeMetricsResult result = inner.GetGenerationMetrics(generationHandle);
            Emit("terminal_metrics_call_returned", MetricsDetail(result));
            return result;
        }

        public LocalLlmRuntimeCallResult Release(ulong generationHandle)
        {
            Emit("release_call_entered", string.Empty);
            LocalLlmRuntimeCallResult result = inner.Release(generationHandle);
            Emit("release_call_returned", StatusDetail(result));
            StopMetricsMonitor();
            return result;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            StopMetricsMonitor();
            inner.Dispose();
        }

        private void StartMetricsMonitor(ulong generationHandle)
        {
            lock (monitorSync)
            {
                if (monitorTask != null)
                {
                    throw new InvalidOperationException(
                        "RMA-134 diagnostic runtime already has an active metrics monitor.");
                }
                monitorCancellation = new CancellationTokenSource();
                CancellationToken token = monitorCancellation.Token;
                monitorTask = Task.Run(async () =>
                {
                    int sample = 0;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(MetricsInterval, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            return;
                        }
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        ++sample;
                        LocalLlmRuntimeMetricsResult metrics;
                        try
                        {
                            metrics = inner.GetGenerationMetrics(generationHandle);
                        }
                        catch (Exception exception)
                        {
                            Emit(
                                "live_metrics_threw",
                                "sample=" + sample.ToString(CultureInfo.InvariantCulture) +
                                " exception=" + Bound(exception.GetType().Name + ": " + exception.Message));
                            return;
                        }
                        Emit(
                            "live_metrics",
                            "sample=" + sample.ToString(CultureInfo.InvariantCulture) + " " +
                            MetricsDetail(metrics));
                        if (!metrics.Succeeded ||
                            (metrics.Metrics != null && metrics.Metrics.FinishedMonotonicMicroseconds != 0UL))
                        {
                            return;
                        }
                    }
                }, CancellationToken.None);
            }
        }

        private void StopMetricsMonitor()
        {
            CancellationTokenSource? cancellation;
            Task? task;
            lock (monitorSync)
            {
                cancellation = monitorCancellation;
                task = monitorTask;
                monitorCancellation = null;
                monitorTask = null;
            }
            if (cancellation == null)
            {
                return;
            }
            cancellation.Cancel();
            if (task != null && !task.IsCompleted)
            {
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
            }
            cancellation.Dispose();
        }

        private void Emit(string stage, string detail)
        {
            diagnostics.Enqueue(new ReachyRma134RuntimeDiagnostic(stage, Bound(detail)));
        }

        private static string StatusDetail(LocalLlmRuntimeCallResult result)
        {
            return "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                (result.Detail.Length == 0 ? string.Empty : " detail=" + Bound(result.Detail));
        }

        private static string MetricsDetail(LocalLlmRuntimeMetricsResult result)
        {
            if (!result.Succeeded || result.Metrics == null)
            {
                return "status=" + result.Status.ToString(CultureInfo.InvariantCulture) +
                    (result.Detail.Length == 0 ? string.Empty : " detail=" + Bound(result.Detail));
            }
            LocalLlmGenerationMetrics metrics = result.Metrics;
            return "status=0 prompt_tokens=" +
                metrics.PromptTokens.ToString(CultureInfo.InvariantCulture) +
                " generated_tokens=" + metrics.GeneratedTokens.ToString(CultureInfo.InvariantCulture) +
                " started_us=" + metrics.StartedMonotonicMicroseconds.ToString(CultureInfo.InvariantCulture) +
                " first_text_us=" + metrics.FirstTextMonotonicMicroseconds.ToString(CultureInfo.InvariantCulture) +
                " finished_us=" + metrics.FinishedMonotonicMicroseconds.ToString(CultureInfo.InvariantCulture);
        }

        private static string Bound(string value)
        {
            const int maximum = 768;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }

    internal sealed class ReachyRma134RuntimeDiagnostic
    {
        internal ReachyRma134RuntimeDiagnostic(string stage, string detail)
        {
            Stage = stage;
            Detail = detail;
        }

        internal string Stage { get; }
        internal string Detail { get; }
    }
}
