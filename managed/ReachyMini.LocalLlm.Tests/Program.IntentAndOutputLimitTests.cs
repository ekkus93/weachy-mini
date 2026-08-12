#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
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
        Require(runtime.CancelCount == 1, "Output-limit breach did not issue exactly one native cancel.");
        Require(runtime.ReleaseCount == 1, "Output-limit generation was not released.");
    }
}
