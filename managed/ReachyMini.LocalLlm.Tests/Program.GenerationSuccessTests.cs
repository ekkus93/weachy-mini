#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
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
}
