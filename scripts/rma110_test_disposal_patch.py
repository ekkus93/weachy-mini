#!/usr/bin/env python3
from pathlib import Path

TEST_PATH = Path(
    "managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs"
)
PROGRAM_PATH = Path("managed/ReachyMini.Camera.Tests/Program.cs")
PROGRESS_PATH = Path("scripts/rma110_test_progress_patch.py")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"unexpected {label} count: {count}")
    return source.replace(old, new)


def patch_test_bootstrap_and_ownership() -> None:
    source = TEST_PATH.read_text(encoding="utf-8")
    source = replace_once(
        source,
        "using System.Runtime.CompilerServices;\n",
        "",
        "module-initializer using",
    )
    source = replace_once(
        source,
        """        [ModuleInitializer]
        internal static void Initialize()
        {
            RunAsync().GetAwaiter().GetResult();
        }

        private static async Task RunAsync()
""",
        """        internal static async Task RunAsync()
""",
        "async contract entry point",
    )
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


def patch_program_entry_point() -> None:
    source = PROGRAM_PATH.read_text(encoding="utf-8")
    source = replace_once(
        source,
        "using System.Collections.Generic;\n",
        "using System.Collections.Generic;\nusing System.Threading.Tasks;\n",
        "task using",
    )
    source = replace_once(
        source,
        "        private static int Main()\n",
        "        private static async Task<int> Main()\n",
        "async main signature",
    )
    source = replace_once(
        source,
        "            Rma101AuthoritativeRotationContracts.Run();\n"
        "            Console.WriteLine(\"RMA-090/RMA-091/RMA-100 camera contracts passed.\");\n",
        "            Rma101AuthoritativeRotationContracts.Run();\n"
        "            await Rma110VisionProviderContracts.RunAsync()\n"
        "                .ConfigureAwait(false);\n"
        "            Console.WriteLine(\"RMA-090/RMA-091/RMA-100 camera contracts passed.\");\n",
        "RMA-110 async invocation",
    )
    PROGRAM_PATH.write_text(source, encoding="utf-8")


def main() -> None:
    patch_test_bootstrap_and_ownership()
    patch_program_entry_point()
    if PROGRESS_PATH.exists():
        PROGRESS_PATH.unlink()
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
