from pathlib import Path

provider_path = Path("Assets/ReachyMini/Runtime/Core/Language/ReachyLocalLlmProvider.cs")
provider = provider_path.read_text(encoding="utf-8")
old_finally = """            finally
            {
                if (generation != null)
                {
                    try
                    {
                        generation.Dispose();
"""
new_finally = """            finally
            {
                if (generation != null)
                {
                    if (terminal?.Failure == LocalLlmFailure.TimedOut)
                    {
                        terminal = AttachTimeoutMetrics(terminal, generation);
                    }
                    try
                    {
                        generation.Dispose();
"""
if provider.count(old_finally) != 1:
    raise SystemExit(
        f"expected one provider finally insertion point, found {provider.count(old_finally)}"
    )
provider = provider.replace(old_finally, new_finally)

anchor = """        private LocalLlmEvent RuntimeCleanupFailure(ulong sequence, string detail)
        {
"""
method = """        private static LocalLlmEvent AttachTimeoutMetrics(
            LocalLlmEvent terminal,
            ILocalLlmGeneration generation)
        {
            try
            {
                LocalLlmGenerationMetrics metrics = generation.GetMetrics();
                return LocalLlmEvent.Failed(
                    terminal.Sequence,
                    LocalLlmFailure.TimedOut,
                    terminal.Detail +
                    " Native progress: prompt_tokens=" + metrics.PromptTokens +
                    " generated_tokens=" + metrics.GeneratedTokens +
                    " time_to_first_token_us=" + metrics.TimeToFirstTokenMicroseconds +
                    " decode_us=" + metrics.DecodeMicroseconds + ".");
            }
            catch (Exception exception)
            {
                return LocalLlmEvent.Failed(
                    terminal.Sequence,
                    LocalLlmFailure.TimedOut,
                    terminal.Detail +
                    " Native progress metrics unavailable explicitly: " +
                    exception.GetType().Name + ".");
            }
        }

"""
if provider.count(anchor) != 1:
    raise SystemExit(f"expected one provider method anchor, found {provider.count(anchor)}")
if "private static LocalLlmEvent AttachTimeoutMetrics(" in provider:
    raise SystemExit("timeout metric helper already exists unexpectedly")
provider_path.write_text(provider.replace(anchor, method + anchor), encoding="utf-8")

acceptance_path = Path(
    "Assets/ReachyMini/Runtime/Application/ReachyRma134LocalLlmAcceptance.cs"
)
acceptance = acceptance_path.read_text(encoding="utf-8")
old_throw = """                    throw new InvalidOperationException(
                        "Local LLM completion request failed: " +
                        item.Failure + ": " + item.Detail);
"""
new_throw = """                    throw new InvalidOperationException(
                        "Local LLM completion request failed after " +
                        deltas + " streamed delta event(s): " +
                        item.Failure + ": " + item.Detail);
"""
if acceptance.count(old_throw) != 1:
    raise SystemExit(
        f"expected one completion failure throw, found {acceptance.count(old_throw)}"
    )
acceptance_path.write_text(acceptance.replace(old_throw, new_throw), encoding="utf-8")

tests_path = Path("managed/ReachyMini.LocalLlm.Tests/Program.cs")
tests = tests_path.read_text(encoding="utf-8")
list_anchor = """            ("second request is rejected instead of queued", BusyRequestIsRejectedAsync),
            ("reset cancels active generation and clears history", ResetCancelsAndClearsAsync),
"""
list_replacement = """            ("second request is rejected instead of queued", BusyRequestIsRejectedAsync),
            ("timeout preserves native progress diagnostics", TimeoutReportsNativeProgressAsync),
            ("reset cancels active generation and clears history", ResetCancelsAndClearsAsync),
"""
if tests.count(list_anchor) != 1:
    raise SystemExit("could not find unique timeout test list insertion point")
tests = tests.replace(list_anchor, list_replacement)

method_anchor = """    private static async Task ResetCancelsAndClearsAsync()
    {
"""
timeout_test = """    private static async Task TimeoutReportsNativeProgressAsync()
    {
        await using TestContext context = await TestContext.CreateAsync().ConfigureAwait(false);
        await RequireLoadedAsync(context).ConfigureAwait(false);
        using var hold = new FakeGeneration(holdUntilCancelled: true);
        context.Runtime.Session.EnqueueGeneration(hold);

        var events = new List<LocalLlmEvent>();
        await foreach (LocalLlmEvent item in context.Provider.GenerateAsync(
            new LocalLlmRequest(
                "timeout-progress",
                "Keep generating until the explicit timeout.",
                TimeSpan.FromMilliseconds(25.0)),
            CancellationToken.None))
        {
            events.Add(item);
        }

        Require(events.Count > 0, "timeout produced no terminal event");
        LocalLlmEvent terminal = events[^1];
        Require(terminal.Failure == LocalLlmFailure.TimedOut, "timeout was not explicit");
        Require(
            terminal.Detail.Contains("prompt_tokens=128", StringComparison.Ordinal),
            "timeout omitted native prompt-token diagnostics");
        Require(
            terminal.Detail.Contains("generated_tokens=32", StringComparison.Ordinal),
            "timeout omitted native generated-token diagnostics");
        Require(
            terminal.Detail.Contains("time_to_first_token_us=1000", StringComparison.Ordinal),
            "timeout omitted native TTFT diagnostics");
        Require(
            terminal.Detail.Contains("decode_us=2000", StringComparison.Ordinal),
            "timeout omitted native decode diagnostics");
        Require(
            context.Provider.Availability.State == LocalLlmProviderState.Ready,
            "an ordinary request timeout faulted the provider");
    }

"""
if tests.count(method_anchor) != 1:
    raise SystemExit("could not find unique timeout test method insertion point")
if "TimeoutReportsNativeProgressAsync" in tests:
    raise SystemExit("timeout progress test already exists unexpectedly")
tests_path.write_text(
    tests.replace(method_anchor, timeout_test + method_anchor),
    encoding="utf-8",
)
