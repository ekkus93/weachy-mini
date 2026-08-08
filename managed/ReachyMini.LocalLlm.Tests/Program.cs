#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Language;
using ReachyMini.LocalModels;

internal static class Program
{
    private const string ValidIntent =
        "{\"schema_version\":1,\"speech\":\"Sure.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-3\"},\"expression\":\"attentive\",\"gesture\":\"none\",\"urgency\":\"normal\"}";
    private const string NoGazeIntent =
        "{\"schema_version\":1,\"speech\":\"Hello!\",\"expression\":\"pleased\",\"gesture\":\"nod\",\"urgency\":\"normal\"}";
    private static readonly string[] Entity3Allowlist = { "entity-3" };

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("strict parser accepts frozen shape", ParserAcceptsFrozenShapeAsync),
            ("strict parser rejects framing and schema drift", ParserRejectsFramingAsync),
            ("tracked gaze authorization snapshot is immutable", TrackedGazeAuthorizationIsImmutableAsync),
            ("ABI mismatch is explicit and does not load", AbiMismatchFailsClosedAsync),
            ("provider streams and commits only validated turns", StreamsAndCommitsAsync),
            ("invalid intent is not committed", InvalidIntentIsTransactionalAsync),
            ("unallowed gaze is not committed", UnallowedGazeIsTransactionalAsync),
            ("context preflight rejects before generation", ContextLimitFailsClosedAsync),
            ("second request is rejected instead of queued", BusyRequestIsRejectedAsync),
            ("reset cancels active generation and clears history", ResetCancelsAndClearsAsync),
            ("runtime fault requires explicit reload", RuntimeFaultRequiresReloadAsync),
            ("history bound requires explicit reset", HistoryBoundRequiresResetAsync),
            ("Qwen no-think suffix is explicit", NoThinkSuffixIsAppliedAsync),
        };

        int passed = 0;
        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run().ConfigureAwait(false);
                Console.WriteLine($"PASS: {name}");
                ++passed;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL: {name}: {exception}");
                return 1;
            }
        }

        Console.WriteLine($"RMA-134 local LLM provider tests passed: {passed}/{tests.Length}");
        return 0;
    }

    private static Task ParserAcceptsFrozenShapeAsync()
    {
        LocalLlmIntentParseResult result = LocalLlmBehaviorIntentParser.Parse(ValidIntent);
        Require(result.Succeeded, "valid constrained JSON was rejected");
        LocalLlmBehaviorIntent intent = result.Intent ??
            throw new InvalidOperationException("Valid parse returned no intent.");
        Require(intent.SchemaVersion == 1, "schema version mismatch");
        Require(intent.GazeTarget?.EntityId == "entity-3", "gaze entity mismatch");
        Require(intent.Expression == LocalLlmExpression.Attentive, "expression mismatch");
        Require(intent.Gesture == LocalLlmGesture.None, "gesture mismatch");
        Require(intent.Urgency == LocalLlmUrgency.Normal, "urgency mismatch");
        return Task.CompletedTask;
    }

    private static Task ParserRejectsFramingAsync()
    {
        Require(
            !LocalLlmBehaviorIntentParser.Parse("```json\n" + NoGazeIntent + "\n```").Succeeded,
            "Markdown fences were accepted");
        Require(
            !LocalLlmBehaviorIntentParser.Parse(NoGazeIntent + NoGazeIntent).Succeeded,
            "two JSON objects were accepted");
        Require(
            !LocalLlmBehaviorIntentParser.Parse(
                NoGazeIntent.Replace("\"schema_version\":1", "\"schema_version\":\"1\"", StringComparison.Ordinal))
                .Succeeded,
            "string schema version was accepted");
        Require(
            !LocalLlmBehaviorIntentParser.Parse(
                NoGazeIntent.Replace(
                    "\"expression\":\"pleased\"",
                    "\"raw_motor\":1,\"expression\":\"pleased\"",
                    StringComparison.Ordinal)).Succeeded,
            "unknown raw-actuation property was accepted");
        Require(
            !LocalLlmBehaviorIntentParser.Parse(
                NoGazeIntent.Replace("\"gesture\":\"nod\"", "\"gesture\":\"dance\"", StringComparison.Ordinal))
                .Succeeded,
            "unknown gesture was accepted");
        return Task.CompletedTask;
    }

    private static Task TrackedGazeAuthorizationIsImmutableAsync()
    {
        string[] source = { "entity-3" };
        var request = new LocalLlmRequest(
            "immutable-gaze",
            "Look at the current entity.",
            TimeSpan.FromSeconds(30.0),
            source);
        source[0] = "entity-99";
        Require(
            request.ValidTrackedEntityIds.Count == 1 &&
            request.ValidTrackedEntityIds[0] == "entity-3",
            "request did not preserve its tracked-entity authorization snapshot");
        Require(
            request.ValidTrackedEntityIds is not string[],
            "request exposed a mutable tracked-entity authorization array");
        return Task.CompletedTask;
    }

    private static async Task AbiMismatchFailsClosedAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        context.Runtime.AbiVersionOverride = 1U;
        LocalLlmOperationResult load = await context.Provider.LoadAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(!load.Succeeded && load.Failure == LocalLlmFailure.AbiMismatch, "ABI mismatch did not fail closed");
        Require(context.Runtime.LoadCount == 0, "ABI mismatch still loaded the model");
    }

    private static async Task StreamsAndCommitsAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(ValidIntent, splitAt: 24);
        List<LocalLlmEvent> first = await GenerateAsync(
                context.Provider,
                "one",
                "Look at me.",
                Entity3Allowlist)
            .ConfigureAwait(false);
        Require(first.Count >= 3, "streaming deltas were discarded before completion");
        Require(first[0].Kind == LocalLlmEventKind.OutputDelta, "first event was not a delta");
        Require(first[^1].Kind == LocalLlmEventKind.Completed, "validated intent did not complete");
        Require(first[^1].Intent?.GazeTarget?.EntityId == "entity-3", "validated gaze was not exposed");

        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 16);
        List<LocalLlmEvent> second = await GenerateAsync(context.Provider, "two", "Say hello again.")
            .ConfigureAwait(false);
        Require(second[^1].Kind == LocalLlmEventKind.Completed, "second turn failed");
        LocalLlmChatMessage[] messages = context.Runtime.Session.LastMessages;
        Require(messages.Length == 4, "validated prior turn was not committed to conversation history");
        Require(messages[1].Role == "user" && messages[2].Role == "assistant", "history role order is wrong");
        Require(messages[1].Content == "Look at me.\n/no_think", "history did not preserve the exact user message seen by Qwen3");
        Require(messages[2].Content == ValidIntent, "history did not preserve exact validated JSON");
    }

    private static async Task InvalidIntentIsTransactionalAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(
            "{\"schema_version\":1,\"speech\":\"No\",\"expression\":\"invalid\",\"gesture\":\"none\",\"urgency\":\"normal\"}",
            splitAt: 12);
        List<LocalLlmEvent> failed = await GenerateAsync(context.Provider, "bad", "Bad turn")
            .ConfigureAwait(false);
        Require(failed[^1].Failure == LocalLlmFailure.InvalidIntent, "invalid intent was not rejected");

        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 12);
        List<LocalLlmEvent> next = await GenerateAsync(context.Provider, "good", "Good turn")
            .ConfigureAwait(false);
        Require(next[^1].Kind == LocalLlmEventKind.Completed, "provider did not recover after invalid intent");
        Require(context.Runtime.Session.LastMessages.Length == 2, "invalid turn was committed to history");
    }

    private static async Task UnallowedGazeIsTransactionalAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(ValidIntent, splitAt: 24);
        List<LocalLlmEvent> rejected = await GenerateAsync(
                context.Provider,
                "unallowed-gaze",
                "Look somewhere")
            .ConfigureAwait(false);
        Require(
            rejected[^1].Failure == LocalLlmFailure.InvalidIntent,
            "unallowed tracked gaze was exposed as a completed intent");

        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 16);
        List<LocalLlmEvent> recovered = await GenerateAsync(
                context.Provider,
                "after-unallowed-gaze",
                "Hello")
            .ConfigureAwait(false);
        Require(recovered[^1].Kind == LocalLlmEventKind.Completed, "provider did not recover after rejecting unallowed gaze");
        Require(context.Runtime.Session.LastMessages.Length == 2, "unallowed gaze turn was committed to history");
    }

    private static async Task ContextLimitFailsClosedAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.TokenCount = 2000;
        List<LocalLlmEvent> events = await GenerateAsync(context.Provider, "ctx", "Too much context")
            .ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Failure == LocalLlmFailure.ContextLimit, "context overflow did not fail explicitly");
        Require(context.Runtime.Session.StartCount == 0, "generation started after context overflow");
    }

    private static async Task BusyRequestIsRejectedAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        using var hold = new FakeGeneration(holdUntilCancelled: true);
        context.Runtime.Session.EnqueueGeneration(hold);

        Task<List<LocalLlmEvent>> firstTask = GenerateAsync(context.Provider, "first", "Hold");
        await hold.Started.Task.ConfigureAwait(false);
        List<LocalLlmEvent> second = await GenerateAsync(context.Provider, "second", "Do not queue")
            .ConfigureAwait(false);
        Require(second.Count == 1 && second[0].Failure == LocalLlmFailure.Busy, "second request was queued or hidden");
        hold.AllowCompletion(NoGazeIntent);
        List<LocalLlmEvent> first = await firstTask.ConfigureAwait(false);
        Require(first[^1].Kind == LocalLlmEventKind.Completed, "first request did not finish after busy rejection");
    }

    private static async Task ResetCancelsAndClearsAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 20);
        Require(
            (await GenerateAsync(context.Provider, "commit", "Commit one").ConfigureAwait(false))[^1].Kind ==
                LocalLlmEventKind.Completed,
            "setup turn did not commit");

        using var hold = new FakeGeneration(holdUntilCancelled: true);
        context.Runtime.Session.EnqueueGeneration(hold);
        Task<List<LocalLlmEvent>> active = GenerateAsync(context.Provider, "cancel", "Cancel me");
        await hold.Started.Task.ConfigureAwait(false);
        LocalLlmOperationResult reset = await context.Provider.ResetConversationAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(reset.Succeeded, "reset failed");
        List<LocalLlmEvent> cancelled = await active.ConfigureAwait(false);
        Require(cancelled[^1].Kind == LocalLlmEventKind.Cancelled, "reset did not cancel active generation");

        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 12);
        Require(
            (await GenerateAsync(context.Provider, "post-reset", "Fresh").ConfigureAwait(false))[^1].Kind ==
                LocalLlmEventKind.Completed,
            "provider did not recover after reset");
        Require(context.Runtime.Session.LastMessages.Length == 2, "reset did not clear committed history");
    }

    private static async Task RuntimeFaultRequiresReloadAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        using FakeGeneration errorGeneration = FakeGeneration.TerminalError(11);
        context.Runtime.Session.EnqueueGeneration(errorGeneration);
        List<LocalLlmEvent> failed = await GenerateAsync(context.Provider, "fault", "Fault")
            .ConfigureAwait(false);
        Require(failed[^1].Failure == LocalLlmFailure.RuntimeFailure, "runtime error was not visible");
        Require(context.Provider.Availability.State == LocalLlmProviderState.Faulted, "provider did not retain fault state");

        List<LocalLlmEvent> blocked = await GenerateAsync(context.Provider, "blocked", "No automatic retry")
            .ConfigureAwait(false);
        Require(blocked.Count == 1 && blocked[0].Failure == LocalLlmFailure.RuntimeFailure, "fault silently retried");
        Require(context.Runtime.LoadCount == 1, "fault triggered an automatic model reload");

        LocalLlmOperationResult ordinaryLoad = await context.Provider.LoadAsync(
                CancellationToken.None)
            .ConfigureAwait(false);
        Require(
            !ordinaryLoad.Succeeded && ordinaryLoad.Failure == LocalLlmFailure.RuntimeFailure,
            "ordinary LoadAsync recovered a retained runtime fault");
        Require(context.Runtime.LoadCount == 1, "ordinary LoadAsync reloaded a faulted provider");

        LocalLlmOperationResult reload = await context.Provider.ReloadAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(reload.Succeeded && context.Runtime.LoadCount == 2, "explicit reload did not recover provider");
    }

    private static async Task HistoryBoundRequiresResetAsync()
    {
        await using TestContext context = await TestContext.CreateAsync(maximumHistoryTurns: 1)
            .ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 16);
        Require(
            (await GenerateAsync(context.Provider, "one", "One").ConfigureAwait(false))[^1].Kind ==
                LocalLlmEventKind.Completed,
            "first bounded-history turn failed");
        List<LocalLlmEvent> second = await GenerateAsync(context.Provider, "two", "Two")
            .ConfigureAwait(false);
        Require(second.Count == 1 && second[0].Failure == LocalLlmFailure.ContextLimit, "history was silently truncated");
        Require(context.Runtime.Session.StartCount == 1, "history overflow started another generation");
    }

    private static async Task NoThinkSuffixIsAppliedAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        context.Runtime.Session.EnqueueSuccess(NoGazeIntent, splitAt: 16);
        _ = await GenerateAsync(context.Provider, "suffix", "Hello")
            .ConfigureAwait(false);
        string finalUser = context.Runtime.Session.LastMessages[^1].Content;
        Require(finalUser == "Hello\n/no_think", "selected Qwen3 no-think suffix was not applied exactly");
        Require(context.Runtime.Session.LastGrammar == TestContext.Grammar, "constrained grammar was not supplied");
        Require(context.Runtime.Session.LastGrammarRoot == "root", "constrained grammar root changed");
    }

    private static async Task RequireLoadedAsync(TestContext context)
    {
        LocalLlmOperationResult load = await context.Provider.LoadAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(load.Succeeded, "provider failed to load in test setup: " + load.Detail);
    }

    private static async Task<List<LocalLlmEvent>> GenerateAsync(
        ReachyLocalLlmProvider provider,
        string requestId,
        string prompt,
        IEnumerable<string>? validTrackedEntityIds = null)
    {
        var result = new List<LocalLlmEvent>();
        await foreach (LocalLlmEvent item in provider.GenerateAsync(
            new LocalLlmRequest(
                requestId,
                prompt,
                TimeSpan.FromSeconds(30.0),
                validTrackedEntityIds),
            CancellationToken.None))
        {
            result.Add(item);
        }
        Require(result.Count > 0 && result[^1].IsTerminal, "generation produced no terminal event");
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestContext : IAsyncDisposable
    {
        internal const string Grammar = "root ::= \"{\" \"}\"";
        private static readonly string[] SupportedAbis = { "arm64-v8a" };
        private readonly string root;
        private readonly ReachyLocalLlmProvider provider;

        private TestContext(
            string root,
            FakeRuntimeFactory runtime,
            LocalModelApprovedArtifact approved,
            LocalModelManifest manifest,
            LocalLlmProviderConfiguration configuration)
        {
            this.root = root;
            Runtime = runtime;
            provider = new ReachyLocalLlmProvider(
                runtime,
                approved,
                manifest,
                configuration);
        }

        public ReachyLocalLlmProvider Provider => provider;

        public FakeRuntimeFactory Runtime { get; }

        public static async Task<TestContext> CreateAsync(int maximumHistoryTurns = 8)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "reachy-rma134-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            byte[] artifactBytes = Encoding.UTF8.GetBytes("rma134-synthetic-model-bytes");
            string sha = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant();
            LocalModelManifest manifest = CreateManifest(artifactBytes.Length, sha);
            using var manager = new LocalModelPackageManager(
                Path.Combine(root, "store"),
                new UnlimitedStorageProbe());
            await using var stream = new MemoryStream(artifactBytes, writable: false);
            LocalModelPackageResult import = await manager.ImportAsync(
                    manifest,
                    stream,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(import.Succeeded, "synthetic approved artifact import failed");
            LocalModelApprovedArtifact approved = import.Artifact ??
                throw new InvalidOperationException("Synthetic approved artifact is missing.");

            var runtime = new FakeRuntimeFactory();
            var config = new LocalLlmProviderConfiguration(
                "system prompt",
                Grammar,
                "root",
                "/no_think",
                LocalLlmExecutionProfile.CreateRma133SelectedProfile(),
                maximumHistoryTurns,
                managedEventQueueCapacity: 64);
            return new TestContext(
                root,
                runtime,
                approved,
                manifest,
                config);
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        private static LocalModelManifest CreateManifest(long size, string sha)
        {
            return new LocalModelManifest(
                1,
                new LocalModelIdentity(
                    "rma134.synthetic.v1",
                    "rma134-synthetic",
                    "RMA-134 Synthetic",
                    "test",
                    new Uri("https://example.invalid/rma134"),
                    "test-revision",
                    "Apache-2.0",
                    experimental: true,
                    "test fixture only"),
                new LocalModelRuntimeRequirement("reachy_llama", 2, requiresNetworkAccess: false),
                new LocalModelArtifact("rma134-synthetic.gguf", size, sha),
                new LocalModelGgufMetadata(3, "qwen3", "Q4_K_M", 1, "gpt2", "qwen2"),
                new LocalModelInferenceProfile(
                    40960,
                    "{{ chat_template }}",
                    Array.Empty<string>(),
                    new LocalModelMemoryEstimate(800_000_000L, 2048, 256),
                    4),
                new LocalModelDeviceCompatibility(
                    SupportedAbis,
                    26,
                    Array.Empty<string>(),
                    800_000_000L,
                    2));
        }
    }

    private sealed class UnlimitedStorageProbe : ILocalModelStorageProbe
    {
        public long GetAvailableBytes(string managedStoreRoot)
        {
            _ = managedStoreRoot ?? throw new ArgumentNullException(nameof(managedStoreRoot));
            return long.MaxValue;
        }
    }

    private sealed class FakeRuntimeFactory : ILocalLlmRuntimeFactory
    {
        public uint AbiVersionOverride { get; set; } = 2U;

        public uint AbiVersion => AbiVersionOverride;

        public int LoadCount { get; private set; }

        public FakeSession Session { get; private set; } = new FakeSession();

        public ILocalLlmModelSession LoadModel(
            LocalModelApprovedArtifact artifact,
            LocalModelManifest manifest)
        {
            _ = artifact ?? throw new ArgumentNullException(nameof(artifact));
            _ = manifest ?? throw new ArgumentNullException(nameof(manifest));
            ++LoadCount;
            Session = new FakeSession();
            return Session;
        }
    }

    private sealed class FakeSession : ILocalLlmModelSession
    {
        private readonly Queue<Func<FakeGeneration>> generationFactories =
            new Queue<Func<FakeGeneration>>();

        public int TokenCount { get; set; } = 128;

        public int StartCount { get; private set; }

        public LocalLlmChatMessage[] LastMessages { get; private set; } =
            Array.Empty<LocalLlmChatMessage>();

        public string LastGrammar { get; private set; } = string.Empty;

        public string LastGrammarRoot { get; private set; } = string.Empty;

        public void EnqueueSuccess(string json, int splitAt)
        {
            generationFactories.Enqueue(() => FakeGeneration.Success(json, splitAt));
        }

        public void EnqueueGeneration(FakeGeneration generation)
        {
            ArgumentNullException.ThrowIfNull(generation);
            generationFactories.Enqueue(() => generation);
        }

        public string RenderChatTemplate(IReadOnlyList<LocalLlmChatMessage> messages)
        {
            var copy = new LocalLlmChatMessage[messages.Count];
            for (int index = 0; index < messages.Count; ++index)
            {
                copy[index] = messages[index];
            }
            LastMessages = copy;
            return "rendered-prompt";
        }

        public int CountTokens(string prompt)
        {
            Require(prompt == "rendered-prompt", "provider did not use rendered chat template");
            return TokenCount;
        }

        public ILocalLlmGeneration StartConstrained(
            string prompt,
            LocalLlmExecutionProfile profile,
            string grammar,
            string grammarRoot)
        {
            Require(prompt == "rendered-prompt", "generation prompt mismatch");
            Require(profile.ContextTokens == 2048U, "RMA-133 selected context profile changed");
            LastGrammar = grammar;
            LastGrammarRoot = grammarRoot;
            ++StartCount;
            if (generationFactories.Count == 0)
            {
                throw new InvalidOperationException("Fake runtime has no queued generation.");
            }
            FakeGeneration generation = generationFactories.Dequeue()();
            generation.MarkStarted();
            return generation;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGeneration : ILocalLlmGeneration
    {
        private readonly Queue<LocalLlmRuntimeEvent> events =
            new Queue<LocalLlmRuntimeEvent>();
        private readonly bool holdUntilCancelled;
        private bool started;
        private bool cancelled;
        private volatile string? completionAfterHold;

        public FakeGeneration(bool holdUntilCancelled)
        {
            this.holdUntilCancelled = holdUntilCancelled;
        }

        public TaskCompletionSource<bool> Started { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeGeneration Success(string json, int splitAt)
        {
            if (splitAt <= 0 || splitAt >= json.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(splitAt));
            }
            var generation = new FakeGeneration(holdUntilCancelled: false);
            generation.events.Enqueue(new LocalLlmRuntimeEvent(
                LocalLlmRuntimeEventType.Text,
                0,
                1UL,
                json.Substring(0, splitAt)));
            generation.events.Enqueue(new LocalLlmRuntimeEvent(
                LocalLlmRuntimeEventType.Text,
                0,
                2UL,
                json.Substring(splitAt)));
            generation.events.Enqueue(new LocalLlmRuntimeEvent(
                LocalLlmRuntimeEventType.Completed,
                0,
                3UL,
                string.Empty));
            return generation;
        }

        public static FakeGeneration TerminalError(int status)
        {
            var generation = new FakeGeneration(holdUntilCancelled: false);
            generation.events.Enqueue(new LocalLlmRuntimeEvent(
                LocalLlmRuntimeEventType.Error,
                status,
                1UL,
                string.Empty));
            return generation;
        }

        public void MarkStarted()
        {
            started = true;
            Started.TrySetResult(true);
        }

        public void AllowCompletion(string json)
        {
            completionAfterHold = json;
        }

        public LocalLlmRuntimeEvent Poll()
        {
            Require(started, "generation was polled before start");
            if (cancelled)
            {
                return new LocalLlmRuntimeEvent(
                    LocalLlmRuntimeEventType.Cancelled,
                    13,
                    99UL,
                    string.Empty);
            }
            if (events.Count > 0)
            {
                return events.Dequeue();
            }
            if (holdUntilCancelled)
            {
                if (completionAfterHold != null)
                {
                    string json = completionAfterHold;
                    completionAfterHold = null;
                    events.Enqueue(new LocalLlmRuntimeEvent(
                        LocalLlmRuntimeEventType.Text,
                        0,
                        1UL,
                        json));
                    events.Enqueue(new LocalLlmRuntimeEvent(
                        LocalLlmRuntimeEventType.Completed,
                        0,
                        2UL,
                        string.Empty));
                    return events.Dequeue();
                }
                return new LocalLlmRuntimeEvent(
                    LocalLlmRuntimeEventType.None,
                    0,
                    0UL,
                    string.Empty);
            }
            return new LocalLlmRuntimeEvent(
                LocalLlmRuntimeEventType.Error,
                14,
                100UL,
                string.Empty);
        }

        public void Cancel()
        {
            cancelled = true;
        }

        public LocalLlmGenerationMetrics GetMetrics()
        {
            return new LocalLlmGenerationMetrics(128UL, 32UL, 1000UL, 2000UL);
        }

        public void Dispose()
        {
        }
    }
}
