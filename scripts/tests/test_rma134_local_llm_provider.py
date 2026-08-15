#!/usr/bin/env python3
"""Deterministic static contracts for the RMA-134 local LLM provider."""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LOCAL_MODELS = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels"
INTEROP = ROOT / "Assets/ReachyMini/Runtime/Interop"
PROVIDER_PARTS = tuple(sorted(LOCAL_MODELS.glob("ReachyLocalLlmProvider.*.cs")))
RUNTIME = LOCAL_MODELS / "ReachyLlamaLocalLlmRuntime.cs"
RUNTIME_CONTRACTS = LOCAL_MODELS / "ReachyLocalLlmRuntimeContracts.cs"
BEHAVIOR = LOCAL_MODELS / "ReachyLocalLlmBehaviorContract.cs"
NATIVE = INTEROP / "NativeReachyLlama.cs"
SYSTEM_PROMPT = ROOT / "benchmarks/rma133/system_prompt-v4.txt"
GRAMMAR = ROOT / "benchmarks/rma133/behavior-output-v1.gbnf"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def provider_source() -> str:
    if not PROVIDER_PARTS:
        raise AssertionError("RMA-134 split provider source set is missing.")
    return "\n".join(read(path) for path in PROVIDER_PARTS)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def literal_constant(source: str, name: str) -> str:
    match = re.search(
        rf'internal const string {re.escape(name)}\s*=\s*\n?\s*"([^"]+)";',
        source,
    )
    if match is None:
        raise AssertionError(f"Unable to locate constant {name}.")
    return match.group(1)


def test_no_unconstrained_generation_binding() -> None:
    native = read(NATIVE)
    provider = provider_source()
    runtime = read(RUNTIME)
    combined = "\n".join((native, provider, runtime))
    require(
        "reachy_llama_generation_start_constrained" in native,
        "RMA-134 must bind constrained generation explicitly.",
    )
    require(
        re.search(r"reachy_llama_generation_start(?!_constrained)", combined) is None,
        "RMA-134 production code exposes unconstrained native generation.",
    )
    require(
        re.search(r"\bGenerationStart\s*\(", combined) is None,
        "RMA-134 production code exposes an unconstrained managed start call.",
    )


def test_no_network_or_provider_fallback() -> None:
    sources = "\n".join(
        (
            provider_source(),
            read(RUNTIME),
            read(RUNTIME_CONTRACTS),
            read(BEHAVIOR),
            read(NATIVE),
        )
    )
    for prohibited in (
        "HttpClient",
        "HttpRequestMessage",
        "WebRequest",
        "TcpClient",
        "UdpClient",
        "System.Net.Sockets",
        "api.openai.com",
        "fallbackProvider",
        "fallbackModel",
    ):
        require(
            prohibited not in sources,
            f"RMA-134 contains prohibited fallback/network token {prohibited!r}.",
        )


def test_approved_artifact_boundary() -> None:
    provider = provider_source()
    public_create = re.search(
        r"public static async Task<LocalLlmProviderCreationResult> CreateAsync\((.*?)\)\n",
        provider,
        flags=re.DOTALL,
    )
    require(public_create is not None, "RMA-134 public CreateAsync boundary is missing.")
    signature = public_create.group(1)
    require("LocalModelManifest manifest" in signature, "CreateAsync does not require a manifest.")
    require(
        "LocalModelApprovedArtifact artifact" in signature,
        "CreateAsync does not require an RMA-132 approved artifact.",
    )
    require("string" not in signature, "CreateAsync exposes an arbitrary string/model path.")
    require(
        "runtime.LoadModel(artifact.FullPath, checkTensors: true)" in provider,
        "Provider does not load only the approved artifact with tensor checking.",
    )


def test_embedded_model_chat_template_is_authoritative() -> None:
    provider = provider_source()
    runtime = read(RUNTIME)
    require(
        re.search(
            r"runtime\.ApplyChatTemplate\(\s*modelHandle,\s*null,\s*messages\s*\)",
            provider,
            flags=re.DOTALL,
        )
        is not None,
        "Provider must select the chat template embedded in the SHA-pinned GGUF.",
    )
    require(
        "templatePointer = template == null ? IntPtr.Zero : template.Pointer" in runtime,
        "Managed runtime does not map null template selection to the native null pointer.",
    )
    require(
        "manifest.Inference.ChatTemplate" not in provider,
        "Mutable manifest chat-template text controls production prompt rendering.",
    )


def test_frozen_behavior_lineage() -> None:
    behavior = read(BEHAVIOR)
    require(
        literal_constant(behavior, "SystemPromptSha256") == sha256(SYSTEM_PROMPT),
        "Embedded system-prompt hash constant drifted from the frozen RMA-133 file.",
    )
    require(
        literal_constant(behavior, "GrammarSha256") == sha256(GRAMMAR),
        "Embedded grammar hash constant drifted from the frozen RMA-133 file.",
    )
    require(
        literal_constant(behavior, "UserPromptSuffix") == "/no_think",
        "Selected Qwen3 user suffix drifted from /no_think.",
    )
    provider = provider_source()
    require(
        'content = content + "\\n" +' in provider
        and "LocalLlmBehaviorContract.UserPromptSuffix" in provider,
        "Provider does not append the selected suffix using the accepted RMA-133 newline form.",
    )
    require(
        "LocalLlmBehaviorContract.ValidateFrozenBytes();" in provider,
        "Provider does not validate frozen embedded prompt/grammar bytes before load/reload.",
    )


def test_no_repair_or_hidden_retry() -> None:
    provider = provider_source()
    behavior = read(BEHAVIOR)
    combined = provider + "\n" + behavior
    prohibited_patterns = (
        r"Replace\([^\n]*```",
        r"Trim[^\n]*```",
        r"strip[_A-Za-z]*fence",
        r"repair[_A-Za-z]*json",
        r"retry[_A-Za-z]*generation",
        r"fallback[_A-Za-z]*generation",
    )
    for pattern in prohibited_patterns:
        require(
            re.search(pattern, combined, flags=re.IGNORECASE) is None,
            f"RMA-134 contains prohibited repair/retry pattern {pattern!r}.",
        )
    require(
        "LocalLlmGenerationStatus.Busy" in provider,
        "Concurrent generation does not expose an explicit Busy state.",
    )
    require(
        "The local LLM provider already has an active generation." in provider,
        "Busy behavior is not explicit in the provider.",
    )
    require(
        provider.count("runtime.Cancel(") == 1,
        "RMA-134 has more than one direct native cancel call site; cleanup could hide retries.",
    )
    drain = provider.split(
        "private async Task<bool> DrainAndReleaseAsync",
        1,
    )[1].split("private LocalLlmRuntimePollResult SafePoll", 1)[0]
    require(
        "SafeCancel(" not in drain,
        "Drain-and-release must never issue a second implicit cancel.",
    )


def test_terminal_validation_and_consumer_failures_are_visible() -> None:
    provider = provider_source()
    require(
        "LocalLlmBehaviorContract.TryParseIntent" in provider,
        "Native completion bypasses strict behavior-intent validation.",
    )
    require(
        "LocalLlmGenerationStatus.InvalidIntent" in provider,
        "Invalid final intent has no explicit terminal status.",
    )
    require(
        "LocalLlmGenerationStatus.ConsumerFailure" in provider,
        "Stream-consumer failures are not explicit.",
    )
    require(
        "Terminal stream notification failed after " in provider,
        "Terminal consumer notification failures can become silent.",
    )
    require(
        "IsTrustedExecutableOutput = false" in read(LOCAL_MODELS / "ReachyLocalLlmContracts.cs"),
        "Partial stream text is not explicitly marked untrusted.",
    )


def main() -> int:
    tests = (
        test_no_unconstrained_generation_binding,
        test_no_network_or_provider_fallback,
        test_approved_artifact_boundary,
        test_embedded_model_chat_template_is_authoritative,
        test_frozen_behavior_lineage,
        test_no_repair_or_hidden_retry,
        test_terminal_validation_and_consumer_failures_are_visible,
    )
    for test in tests:
        test()
    print("RMA-134 local LLM static contracts passed (7 groups).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
