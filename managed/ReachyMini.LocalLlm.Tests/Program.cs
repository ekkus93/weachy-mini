#nullable enable

using System;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private const string ValidIntent =
        "{\"schema_version\":1,\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}";

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
        await TestFailedCancelDoesNotRetryAsync().ConfigureAwait(false);
        await TestPollExceptionCleanupAsync().ConfigureAwait(false);
        await TestMetricsExceptionStillReleasesAsync().ConfigureAwait(false);
        await TestOutOfMemoryBeforeNativeHandleAsync().ConfigureAwait(false);
        await TestOutOfMemoryAfterNativeStartAsync().ConfigureAwait(false);
        await TestOutOfMemoryCleanupFailureFaultsAsync().ConfigureAwait(false);
        await TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync().ConfigureAwait(false);
        await TestReloadRecoveryAndDisposeAsync().ConfigureAwait(false);
        Console.WriteLine("RMA-134 local LLM managed contracts passed (21 groups).");
    }
}
