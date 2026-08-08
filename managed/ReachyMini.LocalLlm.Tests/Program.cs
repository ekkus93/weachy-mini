#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;
using ReachyMini.LocalModels;

internal static class Program
{
    private const string ValidIntent =
        "{\"schema_version\":1,\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}";

    private static readonly string[] StopTokens = { "<|im_end|>", "<|endoftext|>" };
    private static readonly string[] SupportedAbis = { "arm64-v8a" };

    private static async Task Main()
    {
        LocalLlmBehaviorContract.ValidateFrozenBytes();
        TestNativeAbi2Layouts();
        TestRma133BaselineProfile();
        TestStrictIntentParser();
        await TestManifestArtifactAndAbiFailuresAsync().ConfigureAwait(false);
        await TestWorkerPromptAndSuccessAsync().ConfigureAwait(false);
        await TestContextPreflightAsync().ConfigureAwait(false);
        await TestBusyAndCancellationAsync().ConfigureAwait(false);
        await TestResetSuppressesStaleOutputAsync().ConfigureAwait(false);
        await TestInvalidIntentIsNotRepairedAsync().ConfigureAwait(false);
        await TestOutputLimitAsync().ConfigureAwait(false);
        await TestStreamConsumerFailureAsync().ConfigureAwait(false);
        await TestTerminalConsumerFailureAsync().ConfigureAwait(false);
        await TestRuntimeTerminalErrorAsync().ConfigureAwait(false);
        await TestReloadRecoveryAndDisposeAsync().ConfigureAwait(false);
        Console.WriteLine("RMA-134 local LLM managed contracts passed (14 groups).");
    }

    private static void TestNativeAbi2Layouts()
    {
        Require(ReachyLlamaNativeContract.AbiVersion == 2U, "Managed runtime ABI is not 2.");
        Require(Marshal.SizeOf<NativeReachyLlamaErrorInfo>() == 392, "error_info ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaModelConfig>() == 16, "model_config ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationConfig>() == 48, "generation_config ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationConstraint>() == 48, "constraint ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaChatMessage>() == 16, "chat_message ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationEvent>() == 24, "generation_event ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationMetrics>() == 72, "generation_metrics ABI layout drifted.");
    }

    private static void TestRma133BaselineProfile()
    {
        LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();
        Require(profile.ContextTokens == 2048, "RMA-133 context profile drifted.");
        Require(profile.BatchTokens == 256, "RMA-133 batch profile drifted.");
        Require(profile.MicroBatchTokens == 64, "RMA-133 micro-batch profile drifted.");
        Require(profile.MaximumGeneratedTokens == 128, "RMA-133 output-token profile drifted.");
        Require(profile.Threads == 4 && profile.BatchThreads == 4, "RMA-133 thread profile drifted.");
        Require(profile.Temperature == 0.0F && profile.MinP == 0.0F, "RMA-133 sampling profile drifted.");
        Require(profile.Seed == 133U, "RMA-133 seed drifted.");
        Require(profile.StreamQueueCapacity == 64, "RMA-133 queue profile drifted.");
    }

    private static void TestStrictIntentParser()
    {
        Require(
            LocalLlmBehaviorContract.TryParseIntent(
                ValidIntent,
                out LocalLlmBehaviorIntent? parsed,
                out string validDetail),
            "Valid behavior intent was rejected: " + validDetail);
        LocalLlmBehaviorIntent valid = parsed ??
            throw new InvalidOperationException("Valid behavior intent returned null.");
        Require(valid.SchemaVersion == 1, "Behavior schema version changed.");
        Require(valid.Expression == LocalLlmExpression.Attentive, "Expression parse changed.");
        Require(valid.Gesture == LocalLlmGesture.Nod, "Gesture parse changed.");
        Require(valid.Urgency == LocalLlmUrgency.Normal, "Urgency parse changed.");

        const string withGaze =
            "{\"schema_version\":1,\"speech\":\"Looking.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-12\"},\"expression\":\"curious\",\"gesture\":\"small_head_tilt\",\"urgency\":\"low\"}";
        Require(
            LocalLlmBehaviorContract.TryParseIntent(
                withGaze,
                out LocalLlmBehaviorIntent? gaze,
                out string gazeDetail),
            "Valid gaze intent was rejected: " + gazeDetail);
        Require(gaze?.GazeTarget?.EntityId == "entity-12", "Tracked gaze identity changed.");

        string[] invalid =
        {
            "```json\n" + ValidIntent + "\n```",
            "<think>hidden</think>" + ValidIntent,
            ValidIntent + " trailing",
            ValidIntent + ValidIntent,
            "{\"schema_version\":\"1\",\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"joint_angle\":1,\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\",\"torque\":1}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"thing-12\"},\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
        };
        for (int index = 0; index < invalid.Length; ++index)
        {
            Require(
                !LocalLlmBehaviorContract.TryParseIntent(invalid[index], out _, out _),
                "Unsafe or repaired behavior intent was accepted at fixture " + index + ".");
        }
    }

    private static async Task TestManifestArtifactAndAbiFailuresAsync()
    {
        LocalModelManifest manifest = CreateManifest();
        LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();

        using (FakeRuntime mismatchRuntime = new FakeRuntime())
        {
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact("wrong-model"),
                profile,
                mismatchRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.InvalidConfiguration,
                "Manifest/artifact identity mismatch did not fail closed.");
            Require(mismatchRuntime.LoadCount == 0, "Mismatch reached native model load.");
        }

        using (FakeRuntime abiRuntime = new FakeRuntime { AbiVersion = 1U })
        {
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact(),
                profile,
                abiRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.Unavailable,
                "ABI mismatch did not surface as unavailable.");
            Require(abiRuntime.LoadCount == 0, "ABI mismatch reached native model load.");
        }

        using (FakeRuntime loadRuntime = new FakeRuntime())
        {
            loadRuntime.EnqueueLoad(6, 0UL, "load failed");
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact(),
                profile,
                loadRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.RuntimeFailure,
                "Native model-load failure was not preserved.");
            Require(result.Provider == null, "Failed model load returned a provider.");
        }
    }

    private static async Task TestWorkerPromptAndSuccessAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { BlockLoad = true };
        Task<LocalLlmProviderCreationResult> creation = LocalLlmProvider.CreateForTestingAsync(
            CreateManifest(),
            CreateApprovedArtifact(),
            LocalLlmExecutionProfile.CreateRma133V6Baseline(),
            runtime,
            CancellationToken.None);
        Require(runtime.LoadEntered.Wait(TimeSpan.FromSeconds(5)), "Worker model load never entered.");
        Require(!creation.IsCompleted, "Provider creation blocked through native model load.");
        runtime.LoadRelease.Set();
        LocalLlmProviderCreationResult creationResult = await creation.ConfigureAwait(false);
        Require(creationResult.Status == LocalLlmProviderCreationStatus.Created,
            "Provider creation failed: " + creationResult.Detail);
        LocalLlmProvider provider = creationResult.Provider ??
            throw new InvalidOperationException("Created provider result had no provider.");

        try
        {
            runtime.BlockStart = true;
            runtime.EnqueueText(1UL, ValidIntent.Substring(0, 45));
            runtime.EnqueueText(2UL, ValidIntent.Substring(45));
            runtime.EnqueueCompleted(3UL);
            CollectingSink sink = new CollectingSink();
            Task<LocalLlmGenerationResult> generation = provider.GenerateAsync(
                Request("req-success", "Please say hello."),
                sink,
                CancellationToken.None);
            Require(runtime.StartEntered.Wait(TimeSpan.FromSeconds(5)), "Worker generation start never entered.");
            Require(!generation.IsCompleted, "GenerateAsync blocked through native generation start.");
            runtime.StartRelease.Set();
            LocalLlmGenerationResult result = await generation.ConfigureAwait(false);

            Require(result.Status == LocalLlmGenerationStatus.Succeeded, "Valid generation did not succeed.");
            Require(result.Intent?.Speech == "Hello.", "Validated intent content changed.");
            Require(runtime.StartCount == 1, "Constrained generation did not start exactly once.");
            Require(runtime.LastChatTemplate == null, "Provider did not use the GGUF-embedded chat template.");
            Require(runtime.LastMessages.Count == 2, "Provider constructed the wrong chat-message count.");
            Require(runtime.LastMessages[0].Role == "system", "Provider did not inject the frozen system message.");
            Require(runtime.LastMessages[0].Content == LocalLlmBehaviorContract.SystemPrompt,
                "Provider system prompt drifted from the frozen RMA-133 bytes.");
            Require(runtime.LastMessages[1].Role == "user", "Final request message role changed.");
            Require(runtime.LastMessages[1].Content == "Please say hello.\n/no_think",
                "Qwen3 no-think suffix was not appended exactly as accepted in RMA-133.");
            Require(runtime.LastGrammar == LocalLlmBehaviorContract.Grammar && runtime.LastGrammarRoot == "root",
                "Provider did not use the frozen GBNF contract.");
            Require(sink.Text == ValidIntent, "Stream fragments were dropped, changed, or reordered.");
            Require(sink.Events.Count == 3 && sink.Events[2].Type == LocalLlmStreamEventType.Completed,
                "Terminal validated stream event was not delivered.");
            Require(!sink.Events[0].IsTrustedExecutableOutput,
                "Partial text was incorrectly marked as executable/trusted.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task TestContextPreflightAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { TokenCount = 2000 };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-context", "A long request."),
            new CollectingSink(),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ContextLimit, "Context overflow was not rejected.");
        Require(runtime.StartCount == 0, "Context overflow reached native generation start.");
        Require(runtime.LastMessages.Count == 2, "Context preflight silently dropped history/messages.");
    }

    private static async Task TestBusyAndCancellationAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { BlockUntilCancel = true };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<LocalLlmGenerationResult> first = provider.GenerateAsync(
            Request("req-first", "Wait here."), new CollectingSink(), cancellation.Token);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        LocalLlmGenerationResult busy = await provider.GenerateAsync(
            Request("req-busy", "Second request."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(busy.Status == LocalLlmGenerationStatus.Busy, "Concurrent request was not rejected as Busy.");
        Require(runtime.StartCount == 1, "Busy request was queued or started implicitly.");
        cancellation.Cancel();
        LocalLlmGenerationResult cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Require(cancelled.Status == LocalLlmGenerationStatus.Cancelled, "Cancellation did not remain explicit.");
        Require(runtime.CancelCount >= 1, "Cancellation did not reach native generation_cancel.");
        Require(runtime.ReleaseCount == 1, "Cancelled generation handle was not released.");
    }

    private static async Task TestResetSuppressesStaleOutputAsync()
    {
        using FakeRuntime runtime = new FakeRuntime
        {
            BlockUntilCancel = true,
            EmitStaleTextAfterCancel = true,
        };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        CollectingSink sink = new CollectingSink();
        Task<LocalLlmGenerationResult> generation = provider.GenerateAsync(
            Request("req-reset", "Track this conversation."), sink, CancellationToken.None);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        ulong priorEpoch = provider.ConversationEpoch;
        ulong newEpoch = provider.ResetConversation();
        Require(newEpoch != priorEpoch, "Conversation reset did not rotate the epoch.");
        LocalLlmGenerationResult result = await generation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.Superseded, "Reset generation was not superseded.");
        Require(!sink.Text.Contains("STALE", StringComparison.Ordinal), "Post-reset stale text reached the stream sink.");
        Require(runtime.ReleaseCount == 1, "Reset generation handle was not released.");
    }

    private static async Task TestInvalidIntentIsNotRepairedAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, "```json\n" + ValidIntent + "\n```");
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-invalid", "Return an intent."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.InvalidIntent, "Fenced JSON was repaired or accepted.");
        Require(result.Intent == null, "Invalid intent produced executable intent data.");
    }

    private static async Task TestOutputLimitAsync()
    {
        LocalLlmExecutionProfile profile = new LocalLlmExecutionProfile(
            2048, 256, 64, 128, 4, 4, 0.0F, 0.0F, 133U, 64, maximumResponseUtf8Bytes: 8);
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, "123456789");
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime, profile).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-output-limit", "Generate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.OutputLimit, "Managed output-byte limit was not enforced.");
        Require(runtime.CancelCount >= 1, "Output-limit breach did not cancel native generation.");
        Require(runtime.ReleaseCount == 1, "Output-limit generation was not released.");
    }

    private static async Task TestStreamConsumerFailureAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, ValidIntent);
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-sink-fail", "Generate."),
            new ThrowingSink(LocalLlmStreamEventType.Text),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ConsumerFailure, "Text sink failure was swallowed.");
        Require(runtime.CancelCount >= 1, "Text sink failure did not cancel native generation.");
        Require(runtime.ReleaseCount == 1, "Text sink failure leaked the generation handle.");
    }

    private static async Task TestTerminalConsumerFailureAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, ValidIntent);
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-terminal-fail", "Generate."),
            new ThrowingSink(LocalLlmStreamEventType.Completed),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ConsumerFailure, "Terminal sink failure was swallowed.");
        Require(result.Detail.Contains("Succeeded", StringComparison.Ordinal),
            "Terminal sink failure did not preserve the underlying disposition.");
    }

    private static async Task TestRuntimeTerminalErrorAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueError(1UL, 11, "decode failed");
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-runtime-error", "Generate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.RuntimeFailure, "Native terminal error was not preserved.");
        Require(result.NativeStatus == 11, "Native terminal status was lost.");
        Require(result.Detail.Contains("decode failed", StringComparison.Ordinal), "Native terminal detail was lost.");
        Require(runtime.ReleaseCount == 1, "Native error terminal generation was not released.");
    }

    private static async Task TestReloadRecoveryAndDisposeAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueLoad(0, 101UL, string.Empty);
        runtime.EnqueueLoad(6, 0UL, "reload load failed");
        runtime.EnqueueLoad(0, 202UL, string.Empty);
        LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        try
        {
            LocalLlmReloadResult failed = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            Require(failed.Status == LocalLlmReloadStatus.RuntimeFailure, "Reload failure was hidden.");
            Require(provider.State == LocalLlmProviderState.Faulted, "Failed reload did not fault provider state.");
            LocalLlmReloadResult recovered = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            Require(recovered.Status == LocalLlmReloadStatus.Reloaded,
                "Provider did not recover in-process on explicit reload.");
            Require(provider.State == LocalLlmProviderState.Ready, "Recovered provider did not return to Ready.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        Require(runtime.UnloadCount == 2, "Reload/dispose did not unload exactly the owned model handles.");
    }

    private static async Task<LocalLlmProvider> CreateProviderAsync(
        FakeRuntime runtime,
        LocalLlmExecutionProfile? profile = null)
    {
        LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
            CreateManifest(),
            CreateApprovedArtifact(),
            profile ?? LocalLlmExecutionProfile.CreateRma133V6Baseline(),
            runtime,
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmProviderCreationStatus.Created,
            "Provider creation failed: " + result.Detail);
        return result.Provider ?? throw new InvalidOperationException("Created provider result had no provider.");
    }

    private static LocalLlmGenerationRequest Request(string requestId, string userText)
    {
        return new LocalLlmGenerationRequest(
            requestId,
            new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, userText) });
    }

    private static LocalModelManifest CreateManifest()
    {
        return new LocalModelManifest(
            1,
            new LocalModelIdentity(
                LocalLlmBehaviorContract.ManifestId,
                LocalLlmBehaviorContract.ModelId,
                "Qwen3 0.6B Q4_K_M",
                "q4_k_m-8e42d41",
                new Uri("https://huggingface.co/Qwen/Qwen3-0.6B-GGUF"),
                "8e42d41f70cb6c571f58c3f31bd9287b372d97cc",
                "Apache-2.0",
                experimental: false,
                experimentalReason: string.Empty),
            new LocalModelRuntimeRequirement("reachy_llama", 2, requiresNetworkAccess: false),
            new LocalModelArtifact(
                "qwen3/qwen3-0.6b-q4_k_m.gguf",
                LocalLlmBehaviorContract.ArtifactBytes,
                LocalLlmBehaviorContract.ArtifactSha256),
            new LocalModelGgufMetadata(3, "qwen3", "Q4_K_M", 596049920L, "gpt2", "qwen2"),
            new LocalModelInferenceProfile(
                40960,
                "manifest-template-must-not-be-used",
                StopTokens,
                new LocalModelMemoryEstimate(740380672L, 2048, 256),
                4),
            new LocalModelDeviceCompatibility(
                SupportedAbis,
                26,
                Array.Empty<string>(),
                740380672L,
                2));
    }

    private static LocalModelApprovedArtifact CreateApprovedArtifact(string? modelId = null)
    {
        return new LocalModelApprovedArtifact(
            LocalLlmBehaviorContract.ManifestId,
            modelId ?? LocalLlmBehaviorContract.ModelId,
            Path.Combine(Path.GetTempPath(), "rma134-qwen3-0.6b-q4_k_m.gguf"),
            LocalLlmBehaviorContract.ArtifactBytes,
            LocalLlmBehaviorContract.ArtifactSha256);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CollectingSink : ILocalLlmStreamSink
    {
        private readonly List<LocalLlmStreamEvent> events = new List<LocalLlmStreamEvent>();
        internal List<LocalLlmStreamEvent> Events => events;
        internal string Text
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                for (int index = 0; index < events.Count; ++index)
                {
                    if (events[index].Type == LocalLlmStreamEventType.Text)
                    {
                        builder.Append(events[index].Text);
                    }
                }
                return builder.ToString();
            }
        }

        public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(streamEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : ILocalLlmStreamSink
    {
        private readonly LocalLlmStreamEventType throwOn;
        internal ThrowingSink(LocalLlmStreamEventType throwOn) => this.throwOn = throwOn;

        public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (streamEvent.Type == throwOn)
            {
                throw new InvalidOperationException("synthetic sink failure");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRuntime : ILocalLlmRuntime
    {
        private readonly Queue<LocalLlmRuntimeLoadResult> loadResults = new Queue<LocalLlmRuntimeLoadResult>();
        private readonly Queue<LocalLlmRuntimePollResult> polls = new Queue<LocalLlmRuntimePollResult>();
        private bool staleAfterCancelEmitted;
        private ulong nextHandle = 1000UL;

        internal uint AbiVersion { get; set; } = 2U;
        internal int TokenCount { get; set; } = 100;
        internal bool BlockLoad { get; set; }
        internal bool BlockStart { get; set; }
        internal bool BlockUntilCancel { get; set; }
        internal bool EmitStaleTextAfterCancel { get; set; }
        internal int LoadCount { get; private set; }
        internal int UnloadCount { get; private set; }
        internal int StartCount { get; private set; }
        internal int CancelCount { get; private set; }
        internal int ReleaseCount { get; private set; }
        internal string? LastChatTemplate { get; private set; }
        internal List<LocalLlmRuntimeChatMessage> LastMessages { get; } = new List<LocalLlmRuntimeChatMessage>();
        internal string LastGrammar { get; private set; } = string.Empty;
        internal string LastGrammarRoot { get; private set; } = string.Empty;
        internal ManualResetEventSlim LoadEntered { get; } = new ManualResetEventSlim(false);
        internal ManualResetEventSlim LoadRelease { get; } = new ManualResetEventSlim(false);
        internal ManualResetEventSlim StartEntered { get; } = new ManualResetEventSlim(false);
        internal ManualResetEventSlim StartRelease { get; } = new ManualResetEventSlim(false);
        internal TaskCompletionSource<bool> GenerationStarted { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public uint GetAbiVersion() => AbiVersion;

        public LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors)
        {
            Require(Path.IsPathRooted(fullPath), "Provider passed a non-rooted approved model path.");
            Require(checkTensors, "Provider disabled tensor checking.");
            ++LoadCount;
            LoadEntered.Set();
            if (BlockLoad && !LoadRelease.Wait(TimeSpan.FromSeconds(5)))
            {
                return new LocalLlmRuntimeLoadResult(14, "synthetic load gate timed out", 0UL);
            }
            if (loadResults.Count > 0)
            {
                return loadResults.Dequeue();
            }
            return new LocalLlmRuntimeLoadResult(0, string.Empty, ++nextHandle);
        }

        public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
        {
            Require(modelHandle != 0UL, "Provider attempted to unload a null model handle.");
            ++UnloadCount;
            return new LocalLlmRuntimeCallResult(0, string.Empty);
        }

        public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
            ulong modelHandle,
            string? chatTemplate,
            IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
        {
            Require(modelHandle != 0UL, "Template called with null model handle.");
            LastChatTemplate = chatTemplate;
            LastMessages.Clear();
            for (int index = 0; index < messages.Count; ++index)
            {
                LastMessages.Add(new LocalLlmRuntimeChatMessage(messages[index].Role, messages[index].Content));
            }
            return new LocalLlmRuntimeTemplateResult(0, string.Empty, "templated-prompt");
        }

        public LocalLlmRuntimeTokenCountResult CountTokens(ulong modelHandle, string prompt)
        {
            Require(modelHandle != 0UL, "Tokenize called with null model handle.");
            Require(prompt == "templated-prompt", "Provider tokenized something other than exact templated output.");
            return new LocalLlmRuntimeTokenCountResult(0, string.Empty, TokenCount);
        }

        public LocalLlmRuntimeStartResult StartConstrained(
            ulong modelHandle,
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot)
        {
            Require(modelHandle != 0UL, "Generation started with null model handle.");
            Require(prompt == "templated-prompt", "Generation did not consume exact templated prompt.");
            Require(profile.ContextTokens == 2048, "Unexpected generation profile reached fake runtime.");
            ++StartCount;
            LastGrammar = grammar;
            LastGrammarRoot = grammarRoot;
            StartEntered.Set();
            GenerationStarted.TrySetResult(true);
            if (BlockStart && !StartRelease.Wait(TimeSpan.FromSeconds(5)))
            {
                return new LocalLlmRuntimeStartResult(14, "synthetic start gate timed out", 0UL);
            }
            return new LocalLlmRuntimeStartResult(0, string.Empty, 5000UL + (ulong)StartCount);
        }

        public LocalLlmRuntimePollResult Poll(ulong generationHandle)
        {
            Require(generationHandle != 0UL, "Poll called with null generation handle.");
            if (CancelCount > 0)
            {
                if (EmitStaleTextAfterCancel && !staleAfterCancelEmitted)
                {
                    staleAfterCancelEmitted = true;
                    return new LocalLlmRuntimePollResult(
                        0, string.Empty, LocalLlmRuntimePollKind.Text, 0, 999UL, "STALE");
                }
                return new LocalLlmRuntimePollResult(
                    0, string.Empty, LocalLlmRuntimePollKind.Cancelled, 13, 1000UL, string.Empty);
            }
            if (BlockUntilCancel)
            {
                return new LocalLlmRuntimePollResult(
                    0, string.Empty, LocalLlmRuntimePollKind.None, 0, 0UL, string.Empty);
            }
            if (polls.Count > 0)
            {
                return polls.Dequeue();
            }
            return new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.None, 0, 0UL, string.Empty);
        }

        public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
        {
            Require(generationHandle != 0UL, "Cancel called with null generation handle.");
            ++CancelCount;
            return new LocalLlmRuntimeCallResult(0, string.Empty);
        }

        public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
        {
            Require(generationHandle != 0UL, "Metrics called with null generation handle.");
            return new LocalLlmRuntimeMetricsResult(
                0,
                string.Empty,
                new LocalLlmGenerationMetrics(100UL, 20UL, 1000UL, 2000UL, 3000UL, 2048, 256, 4, 4));
        }

        public LocalLlmRuntimeCallResult Release(ulong generationHandle)
        {
            Require(generationHandle != 0UL, "Release called with null generation handle.");
            ++ReleaseCount;
            return new LocalLlmRuntimeCallResult(0, string.Empty);
        }

        internal void EnqueueLoad(int status, ulong handle, string detail) =>
            loadResults.Enqueue(new LocalLlmRuntimeLoadResult(status, detail, handle));

        internal void EnqueueText(ulong sequence, string text) =>
            polls.Enqueue(new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.Text, 0, sequence, text));

        internal void EnqueueCompleted(ulong sequence) =>
            polls.Enqueue(new LocalLlmRuntimePollResult(
                0, string.Empty, LocalLlmRuntimePollKind.Completed, 0, sequence, string.Empty));

        internal void EnqueueError(ulong sequence, int eventStatus, string detail) =>
            polls.Enqueue(new LocalLlmRuntimePollResult(
                0, detail, LocalLlmRuntimePollKind.Error, eventStatus, sequence, string.Empty));

        public void Dispose()
        {
            LoadEntered.Dispose();
            LoadRelease.Dispose();
            StartEntered.Dispose();
            StartRelease.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
