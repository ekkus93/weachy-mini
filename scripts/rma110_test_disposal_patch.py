#!/usr/bin/env python3
from pathlib import Path

PATH = Path("managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"unexpected {label} count: {count}")
    return source.replace(old, new)


def main() -> None:
    source = PATH.read_text(encoding="utf-8")

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

    source = source.replace(
        "            var rawResources = new FakeResources(\n",
        "            await using var rawResources = new FakeResources(\n",
    )
    source = source.replace(
        "            ReachyVisionFrame raw = RawFrame(rawResources, 5UL);\n",
        "            await using ReachyVisionFrame raw =\n"
        "                RawFrame(rawResources, 5UL);\n",
    )
    source = source.replace(
        "            var staleResources = new FakeResources(10, 10, hasValidity: true);\n",
        "            await using var staleResources = new FakeResources(\n"
        "                10,\n"
        "                10,\n"
        "                hasValidity: true);\n",
    )
    source = source.replace(
        "            ReachyVisionFrame staleFrame = Frame(\n",
        "            await using ReachyVisionFrame staleFrame = Frame(\n",
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
            raise SystemExit(f"unexpected provider declaration count for {old!r}: {count}")
        source = source.replace(old, new)

    PATH.write_text(source, encoding="utf-8")
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
