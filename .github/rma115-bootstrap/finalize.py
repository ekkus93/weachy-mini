#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import os
from pathlib import Path

PREFIX_SHA = "2671e6d4f8d74507ccd410e9783edc035eac8152b9a48a93500abe746e79126b"
TAIL_SHA = "bba85255dd12a9c958718b98e0d6f1082540b0adc2174e12fb43bc1b1524a5df"
PROGRAM_SHA = "3c0439fb309021bb5bd385c7d81ba2e3f3a44294d577a357e43eb01de9b958ac"
OVERLAP_BYTES = 862


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def replace_exact(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected one {label} replacement target, found {count}")
    return text.replace(old, new, 1)


def replace_exact_count(
    text: str,
    old: str,
    new: str,
    expected_count: int,
    label: str,
) -> str:
    count = text.count(old)
    if count != expected_count:
        raise SystemExit(
            f"expected {expected_count} {label} replacement targets, found {count}"
        )
    return text.replace(old, new)


def prepare() -> None:
    program = Path("managed/ReachyMini.RemoteVlm.Tests/Program.cs")
    prefix = program.read_bytes()
    if len(prefix) != 48048 or digest(prefix) != PREFIX_SHA:
        raise SystemExit("unexpected RMA-115 test prefix")
    encoded = Path(".github/rma115-bootstrap/test-tail.gz.b64").read_text(
        encoding="utf-8"
    )
    tail = gzip.decompress(base64.b64decode(encoded, validate=True))
    if len(tail) != 23444 or digest(tail) != TAIL_SHA:
        raise SystemExit("unexpected RMA-115 test tail")
    if prefix[-OVERLAP_BYTES:] != tail[:OVERLAP_BYTES]:
        raise SystemExit("RMA-115 test prefix/tail overlap mismatch")
    complete = prefix[:-OVERLAP_BYTES] + tail
    if len(complete) != 70630 or digest(complete) != PROGRAM_SHA:
        raise SystemExit("completed RMA-115 test program digest mismatch")

    program_text = complete.decode("utf-8")
    program_text = replace_exact(
        program_text,
        """        private static ReachyVisionFrame Frame(
            VisionCoverageState state = VisionCoverageState.Normal,
            long validPixelCount = 100L,
            long totalPixelCount = 100L,
            bool shouldStopVisionDrivenTurning = false,
            ulong sourceSequence = 1UL)
        {
            var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: true);
            var coverage = new ReachyVisionCoverage(
                state,
                validPixelCount,
                totalPixelCount,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning,
                diagnostic: "synthetic transformed coverage");
            return new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                Identity(sourceSequence),
                coverage,
                resources);
        }
""",
        """        private static ReachyVisionFrame Frame(
            VisionCoverageState state = VisionCoverageState.Normal,
            long validPixelCount = 100L,
            long totalPixelCount = 100L,
            bool shouldStopVisionDrivenTurning = false,
            ulong sourceSequence = 1UL)
        {
            using var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: true);
            var coverage = new ReachyVisionCoverage(
                state,
                validPixelCount,
                totalPixelCount,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning,
                diagnostic: "synthetic transformed coverage");
            var frame = new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                Identity(sourceSequence),
                coverage,
                resources);
            resources.TransferOwnershipToFrame();
            return frame;
        }
""",
        "transformed-frame resource ownership",
    )
    program_text = replace_exact(
        program_text,
        """        private static ReachyVisionFrame RawFrame()
        {
            var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: false);
            var coverage = new ReachyVisionCoverage(
                VisionCoverageState.Unavailable,
                validPixelCount: 0L,
                totalPixelCount: 0L,
                hasValidityMask: false,
                shouldStopVisionDrivenTurning: true,
                diagnostic: "raw debug coverage unavailable");
            return new ReachyVisionFrame(
                VisionFrameOrigin.RawPhoneDebug,
                Identity(),
                coverage,
                resources);
        }
""",
        """        private static ReachyVisionFrame RawFrame()
        {
            using var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: false);
            var coverage = new ReachyVisionCoverage(
                VisionCoverageState.Unavailable,
                validPixelCount: 0L,
                totalPixelCount: 0L,
                hasValidityMask: false,
                shouldStopVisionDrivenTurning: true,
                diagnostic: "raw debug coverage unavailable");
            var frame = new ReachyVisionFrame(
                VisionFrameOrigin.RawPhoneDebug,
                Identity(),
                coverage,
                resources);
            resources.TransferOwnershipToFrame();
            return frame;
        }
""",
        "raw-frame resource ownership",
    )
    program_text = replace_exact(
        program_text,
        """            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
""",
        "            ArgumentNullException.ThrowIfNull(provider);\n",
        "provider null guard",
    )
    program_text = replace_exact_count(
        program_text,
        """                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }
""",
        "                ArgumentNullException.ThrowIfNull(request);\n",
        2,
        "request null guards",
    )
    program_text = replace_exact(
        program_text,
        '"StoreResponse => false"',
        '"public bool StoreResponse { get; }"',
        "StoreResponse source declaration assertion",
    )
    program_text = replace_exact(
        program_text,
        '"Stream => false"',
        '"public bool Stream { get; }"',
        "Stream source declaration assertion",
    )
    program_text = replace_exact(
        program_text,
        "        private sealed class FakeResources : IReachyVisionFrameResources\n",
        "        private sealed class FakeResources : IReachyVisionFrameResources, IDisposable\n",
        "FakeResources IDisposable contract",
    )
    program_text = replace_exact(
        program_text,
        """            private readonly object color = new object();
            private readonly object? validityMask;
            private int disposed;
""",
        """            private readonly object color = new object();
            private readonly object? validityMask;
            private int disposed;
            private int ownershipTransferred;
""",
        "FakeResources ownership state",
    )
    program_text = replace_exact(
        program_text,
        """            public ValueTask DisposeAsync()
            {
                _ = Interlocked.Exchange(ref disposed, 1);
                return default;
            }
""",
        """            public void TransferOwnershipToFrame()
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (Interlocked.Exchange(ref ownershipTransferred, 1) != 0)
                {
                    throw new InvalidOperationException(
                        "Fake resource ownership was already transferred.");
                }
            }

            public void Dispose()
            {
                if (Volatile.Read(ref ownershipTransferred) == 0)
                {
                    _ = Interlocked.Exchange(ref disposed, 1);
                }
                GC.SuppressFinalize(this);
            }

            public ValueTask DisposeAsync()
            {
                _ = Interlocked.Exchange(ref disposed, 1);
                GC.SuppressFinalize(this);
                return default;
            }
""",
        "FakeResources ownership-aware disposal",
    )
    program.write_text(program_text, encoding="utf-8")

    source_path = Path(
        "Assets/ReachyMini/Runtime/Core/Perception/"
        "ReachyOpenAiVisionLanguageAdapters.cs"
    )
    source = source_path.read_text(encoding="utf-8")
    source = replace_exact(
        source,
        "        public bool RequireValidityMask => true;",
        "        public bool RequireValidityMask { get; } = true;",
        "RequireValidityMask auto-property",
    )
    source = replace_exact(
        source,
        "        public bool ApplyValidityBeforeResize => true;",
        "        public bool ApplyValidityBeforeResize { get; } = true;",
        "ApplyValidityBeforeResize auto-property",
    )
    source = replace_exact(
        source,
        "        public bool StoreResponse => false;",
        "        public bool StoreResponse { get; }",
        "StoreResponse auto-property",
    )
    source = replace_exact(
        source,
        "        public bool Stream => false;",
        "        public bool Stream { get; }",
        "Stream auto-property",
    )
    source = replace_exact(
        source,
        "                if (value.IndexOf(\n"
        "                        ForbiddenDetailFragments[index],\n"
        "                        StringComparison.OrdinalIgnoreCase) >= 0)",
        "                if (value.Contains(\n"
        "                        ForbiddenDetailFragments[index],\n"
        "                        StringComparison.OrdinalIgnoreCase))",
        "case-insensitive forbidden-detail Contains",
    )
    source = replace_exact(
        source,
        "        public ValueTask DisposeAsync()\n"
        "        {\n"
        "            _ = Interlocked.Exchange(ref disposed, 1);\n"
        "            return default;\n"
        "        }",
        "        public ValueTask DisposeAsync()\n"
        "        {\n"
        "            _ = Interlocked.Exchange(ref disposed, 1);\n"
        "            GC.SuppressFinalize(this);\n"
        "            return default;\n"
        "        }",
        "DisposeAsync finalizer suppression",
    )
    source_path.write_text(source, encoding="utf-8")

    project = """<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=\"../ReachyMini.Core/ReachyMini.Core.csproj\" />
  </ItemGroup>
</Project>
"""
    Path(
        "managed/ReachyMini.RemoteVlm.Tests/ReachyMini.RemoteVlm.Tests.csproj"
    ).write_text(project, encoding="utf-8")
    readme = """# RMA-115 remote VLM contracts

Run:

```bash
dotnet run --project managed/ReachyMini.RemoteVlm.Tests/ReachyMini.RemoteVlm.Tests.csproj --configuration Release
```

The deterministic suite opens no network connection and needs no API key. Its 60 cases cover Responses and Chat Completions selection, transformed-frame and validity-mask enforcement, bounded image policy, coverage limitations, stale-entity exclusion, cancellation, concurrency, structured error validation, secret redaction, disposal, and single-attempt/no-fallback behavior.
"""
    Path("managed/ReachyMini.RemoteVlm.Tests/README.md").write_text(
        readme, encoding="utf-8"
    )

    todo_path = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
    todo = todo_path.read_text(encoding="utf-8")
    start = todo.index("## RMA-115 — Implement OpenAI and compatible VLM adapters")
    end = todo.index("\n---\n", start)
    section = """## RMA-115 — Implement OpenAI and compatible VLM adapters

**Status:** Complete (2026-08-06)

- [x] Reuse the selected Responses- or Chat-style provider transport.
- [x] Encode only transformed valid image content.
- [x] Define image resizing and quality policy.
- [x] Include prompt context stating coverage limitations where relevant.
- [x] Validate structured results and preserve provider error detail without secrets.

**Acceptance criteria**

- [x] Basic face tracking works without a VLM.
- [x] VLM requests are selective and cancellable.
- [x] Stale entities are not presented to the LLM as currently visible.

**Completion evidence**

- Responses-style and Chat Completions-style providers share one explicit transport boundary. Endpoint style, model ID, output limit, image policy, and provider identity are configuration values; mismatches fail construction rather than trying another protocol.
- Only observation-eligible transformed Reachy-eye frames with validity masks reach the encoder. Encoded results must prove identity, mask application before resize, valid-only content, exact policy application, bounded dimensions and bytes, and no upscaling before one transport call is permitted.
- The default policy is aspect-preserving 1024x1024 maximum, 4 MiB maximum, JPEG quality 85, automatic detail, black replacement for invalid pixels, and no upscaling. The owned encoded payload is copied and zeroed on disposal.
- Coverage context states normal or degraded coverage and the valid-pixel fraction, prohibits inference outside valid regions, and explicitly excludes world-model history and stale entities from current visual evidence.
- Structured outcomes retain safe category, code, HTTP status, provider request ID, and bounded detail. Credential-, data-URL-, payload-, and opaque-token-like detail is redacted; uncaught exceptions expose only their type.
- Automatic retry, provider fallback, response storage, and streaming are disabled. Concurrency overflow and cancellation are typed and visible; no request is silently queued or rerouted.
- RMA-111 face/person tracking remains independent and bundled on device. RMA-113 remains the selective admission policy; these adapters contain no frame-rate loop, timer, or automatic invocation path.
- The permanent 60-case suite and exact-SHA gate are documented in `docs/architecture/OPENAI_COMPATIBLE_VLM_ADAPTERS.md` and `docs/validation/RMA_115_OPENAI_COMPATIBLE_VLM_ADAPTERS_VALIDATION_2026-08-06.md`.
"""
    todo_path.write_text(todo[:start] + section + todo[end:], encoding="utf-8")


def record() -> None:
    run_id = os.environ.get("GITHUB_RUN_ID", "unknown")
    source_sha = os.environ.get("GITHUB_SHA", "unknown")
    text = f"""# RMA-115 OpenAI-compatible VLM adapter validation

**Status:** Candidate implementation validated
**Date:** 2026-08-06
**Bootstrap source SHA:** `{source_sha}`
**Bootstrap workflow run:** `{run_id}`

The warnings-as-errors managed-core build and all 60 deterministic remote-VLM contracts passed before the candidate files were committed to the disposable branch. The suite used fake encoders and a mock transport, opened no network connection, and required no credential.

This is not the final exact-master evidence boundary. The permanent RMA-115 workflow must pass on the final implementation commit before final evidence is added.
"""
    Path(
        "docs/validation/RMA_115_OPENAI_COMPATIBLE_VLM_ADAPTERS_VALIDATION_2026-08-06.md"
    ).write_text(text, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "record"))
    args = parser.parse_args()
    if args.mode == "prepare":
        prepare()
    else:
        record()


if __name__ == "__main__":
    main()
