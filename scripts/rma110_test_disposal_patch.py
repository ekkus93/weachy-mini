#!/usr/bin/env python3
from pathlib import Path

TEST_PATH = Path(
    "managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs"
)
EXECUTOR_PATH = Path(
    "Assets/ReachyMini/Runtime/Core/Perception/ReachyVisionProviderExecutor.cs"
)
PROGRESS_PATH = Path("scripts/rma110_test_progress_patch.py")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"unexpected {label} count: {count}")
    return source.replace(old, new)


def patch_tests() -> None:
    source = TEST_PATH.read_text(encoding="utf-8")

    source = replace_once(
        source,
        "            TransformedFramesRequireOwnedColorValidityAndCoverage();\n",
        "            await TransformedFramesRequireOwnedColorValidityAndCoverageAsync()\n"
        "                .ConfigureAwait(false);\n",
        "transformed test invocation",
    )
    source = replace_once(
        source,
        "        private static void TransformedFramesRequireOwnedColorValidityAndCoverage()\n"
        "        {\n"
        "            var resources = new FakeResources(\n",
        "        private static async Task\n"
        "            TransformedFramesRequireOwnedColorValidityAndCoverageAsync()\n"
        "        {\n"
        "            await using var resources = new FakeResources(\n",
        "transformed test declaration",
    )
    source = replace_once(
        source,
        "            ReachyVisionFrame frame = Frame(\n"
        "                resources,\n"
        "                VisionFrameOrigin.TransformedReachyEye,\n"
        "                VisionCoverageState.Normal,\n"
        "                sourceSequence: 1UL);\n",
        "            await using ReachyVisionFrame frame = Frame(\n"
        "                resources,\n"
        "                VisionFrameOrigin.TransformedReachyEye,\n"
        "                VisionCoverageState.Normal,\n"
        "                sourceSequence: 1UL);\n",
        "transformed frame lease",
    )
    source = replace_once(
        source,
        "            Throws<ArgumentException>(\n"
        "                () =>\n"
        "                {\n"
        "                    _ = Frame(\n"
        "                        new FakeResources(10, 10, hasValidity: false),\n",
        "            await using var invalidResources = new FakeResources(\n"
        "                10,\n"
        "                10,\n"
        "                hasValidity: false);\n"
        "            Throws<ArgumentException>(\n"
        "                () =>\n"
        "                {\n"
        "                    _ = Frame(\n"
        "                        invalidResources,\n",
        "invalid resource ownership",
    )

    source = replace_once(
        source,
        "            var rawResources = new FakeResources(\n",
        "            await using var rawResources = new FakeResources(\n",
        "raw resource ownership",
    )
    source = replace_once(
        source,
        "            ReachyVisionFrame raw = RawFrame(rawResources, 5UL);\n",
        "            await using ReachyVisionFrame raw =\n"
        "                RawFrame(rawResources, 5UL);\n",
        "raw frame ownership",
    )
    source = replace_once(
        source,
        "            var staleResources = new FakeResources(10, 10, hasValidity: true);\n",
        "            await using var staleResources = new FakeResources(\n"
        "                10,\n"
        "                10,\n"
        "                hasValidity: true);\n",
        "stale resource ownership",
    )
    source = replace_once(
        source,
        "            ReachyVisionFrame staleFrame = Frame(\n",
        "            await using ReachyVisionFrame staleFrame = Frame(\n",
        "stale frame ownership",
    )

    inline_frame = (
        "            ReachyVisionFrame frame = Frame(\n"
        "                new FakeResources(10, 10, hasValidity: true),\n"
    )
    inline_count = source.count(inline_frame)
    if inline_count != 6:
        raise SystemExit(f"unexpected inline frame resource count: {inline_count}")
    source = source.replace(
        inline_frame,
        "            await using var resources = new FakeResources(\n"
        "                10,\n"
        "                10,\n"
        "                hasValidity: true);\n"
        "            await using ReachyVisionFrame frame = Frame(\n"
        "                resources,\n",
    )

    final_resources = (
        "            var resources = new FakeResources(10, 10, hasValidity: true);\n"
    )
    final_count = source.count(final_resources)
    if final_count != 1:
        raise SystemExit(f"unexpected final resource count: {final_count}")
    source = source.replace(
        final_resources,
        "            await using var resources = new FakeResources(\n"
        "                10,\n"
        "                10,\n"
        "                hasValidity: true);\n",
    )

    provider_replacements = {
        "            var source = new FakeFrameSource(\n":
            "            await using var source = new FakeFrameSource(\n",
        "            var tracker = new FakeTracker(\n":
            "            await using var tracker = new FakeTracker(\n",
        "            var provider = new FakeVisionLanguageProvider(\n":
            "            await using var provider = new FakeVisionLanguageProvider(\n",
    }
    expected_counts = {
        "            var source = new FakeFrameSource(\n": 1,
        "            var tracker = new FakeTracker(\n": 5,
        "            var provider = new FakeVisionLanguageProvider(\n": 1,
    }
    for old, new in provider_replacements.items():
        count = source.count(old)
        if count != expected_counts[old]:
            raise SystemExit(
                f"unexpected provider declaration count for {old!r}: {count}"
            )
        source = source.replace(old, new)

    cancellation_block = """            cancellation.Cancel();
            TrackingResult result = await pending.ConfigureAwait(false);
"""
    bounded_cancellation_block = """            cancellation.Cancel();
            Task completed = await Task.WhenAny(
                pending,
                Task.Delay(
                    TimeSpan.FromSeconds(1.0),
                    CancellationToken.None)).ConfigureAwait(false);
            if (completed != pending)
            {
                throw new InvalidOperationException(
                    "Managed test failed: caller cancellation did not complete within one second.");
            }
            TrackingResult result = await pending.ConfigureAwait(false);
"""
    source = replace_once(
        source,
        cancellation_block,
        bounded_cancellation_block,
        "bounded caller cancellation",
    )

    TEST_PATH.write_text(source, encoding="utf-8")


def patch_executor() -> None:
    source = EXECUTOR_PATH.read_text(encoding="utf-8")
    old = """            Task callerCancellationTask = Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            Task completed = await Task.WhenAny(
                providerTask,
                timeoutTask,
                callerCancellationTask).ConfigureAwait(false);
"""
    new = """            var callerCancellationSignal =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration callerCancellationRegistration =
                cancellationToken.Register(
                    static state =>
                    {
                        var signal =
                            (TaskCompletionSource<bool>)state!;
                        signal.TrySetResult(true);
                    },
                    callerCancellationSignal);
            Task completed = await Task.WhenAny(
                providerTask,
                timeoutTask,
                callerCancellationSignal.Task).ConfigureAwait(false);
"""
    source = replace_once(
        source,
        old,
        new,
        "explicit caller cancellation signal",
    )
    EXECUTOR_PATH.write_text(source, encoding="utf-8")


def main() -> None:
    patch_tests()
    patch_executor()
    if PROGRESS_PATH.exists():
        PROGRESS_PATH.unlink()
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
