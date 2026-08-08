#!/usr/bin/env python3
"""Temporary staging-only transformation for RMA-133 V6 selected-model closure."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}: {old!r}")
    write(path, text.replace(old, new, 1))


def replace_all(path: str, old: str, new: str, expected: int | None = None) -> None:
    text = read(path)
    count = text.count(old)
    if expected is not None and count != expected:
        raise RuntimeError(f"{path}: expected {expected} matches, found {count}: {old!r}")
    if count == 0:
        raise RuntimeError(f"{path}: no matches: {old!r}")
    write(path, text.replace(old, new))


def check_section(path: str, start: str, end: str) -> None:
    text = read(path)
    start_at = text.index(start)
    end_at = text.index(end, start_at)
    section = text[start_at:end_at]
    section = section.replace("- [ ]", "- [x]")
    write(path, text[:start_at] + section + text[end_at:])


def insert_before_once(path: str, marker: str, insertion: str) -> None:
    text = read(path)
    if text.count(marker) != 1:
        raise RuntimeError(f"{path}: expected one insertion marker {marker!r}")
    write(path, text.replace(marker, insertion + marker, 1))


# ---------------------------------------------------------------------------
# RMA-131 active ABI compatibility: schema shape stays v1; active runtime is v2.
# ---------------------------------------------------------------------------
replace_once(
    "models/manifests/local-llm-manifest.schema.json",
    '"runtime_id": {\n          "const": "reachy_llama"\n        },\n        "abi_version": {\n          "const": 1\n        }',
    '"runtime_id": {\n          "const": "reachy_llama"\n        },\n        "abi_version": {\n          "const": 2\n        }',
)
replace_once(
    "models/manifests/local-llm-manifest.schema.json",
    '"reachy_llama_abi_version": {\n          "const": 1\n        }',
    '"reachy_llama_abi_version": {\n          "const": 2\n        }',
)

replace_once(
    "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelManifest.cs",
    "public const int ReachyLlamaAbiVersion = 1;",
    "public const int ReachyLlamaAbiVersion = 2;",
)

replace_all(
    "models/manifests/examples/rma131-synthetic-experimental.local-llm.json",
    '"abi_version": 1',
    '"abi_version": 2',
    expected=1,
)
replace_all(
    "models/manifests/examples/rma131-synthetic-experimental.local-llm.json",
    '"reachy_llama_abi_version": 1',
    '"reachy_llama_abi_version": 2',
    expected=1,
)

validator = read("scripts/validate_local_llm_manifest.py")
validator = validator.replace(
    'runtime.get("abi_version") != 1',
    'runtime.get("abi_version") != 2',
)
validator = validator.replace(
    'runtime.abi_version must equal ABI 1',
    'runtime.abi_version must equal ABI 2',
)
validator = validator.replace(
    'compatibility.get("reachy_llama_abi_version") != 1',
    'compatibility.get("reachy_llama_abi_version") != 2',
)
validator = validator.replace(
    'device_compatibility.reachy_llama_abi_version must equal ABI 1',
    'device_compatibility.reachy_llama_abi_version must equal ABI 2',
)
if "must equal ABI 1" in validator:
    raise RuntimeError("validator still contains an ABI-1 active-policy error")
write("scripts/validate_local_llm_manifest.py", validator)

# Managed RMA-131 contracts: active ABI is 2; ABI 1 is now the mismatch case.
managed = read("managed/ReachyMini.LocalModelManifest.Tests/Program.cs")
managed = managed.replace('Equal(1, manifest.Runtime.AbiVersion, "runtime ABI");', 'Equal(2, manifest.Runtime.AbiVersion, "runtime ABI");')
managed = managed.replace('Equal(1, manifest.DeviceCompatibility.ReachyLlamaAbiVersion, "compatibility ABI");', 'Equal(2, manifest.DeviceCompatibility.ReachyLlamaAbiVersion, "compatibility ABI");')
managed = managed.replace('new LocalModelRuntimeRequirement("other_runtime", 1, false)', 'new LocalModelRuntimeRequirement("other_runtime", 2, false)')
managed = managed.replace('new LocalModelRuntimeRequirement("reachy_llama", 2, false),\n                "wrong runtime ABI"', 'new LocalModelRuntimeRequirement("reachy_llama", 1, false),\n                "wrong runtime ABI"')
managed = managed.replace('new LocalModelRuntimeRequirement("reachy_llama", 1, true)', 'new LocalModelRuntimeRequirement("reachy_llama", 2, true)')
managed = managed.replace('return new LocalModelRuntimeRequirement("reachy_llama", 1, false);', 'return new LocalModelRuntimeRequirement("reachy_llama", 2, false);')
managed = managed.replace('2147483648L,\n                    1),', '2147483648L,\n                    2),')
managed = managed.replace('536870912L,\n                        1)),', '536870912L,\n                        2)),')
managed = managed.replace('2147483648L,\n                    2),\n                "device/runtime ABI mismatch"', '2147483648L,\n                    1),\n                "device/runtime ABI mismatch"')
managed = managed.replace('2147483648L,\n                1);', '2147483648L,\n                2);')
if 'return new LocalModelRuntimeRequirement("reachy_llama", 1, false);' in managed:
    raise RuntimeError("managed tests still create the active runtime with ABI 1")
write("managed/ReachyMini.LocalModelManifest.Tests/Program.cs", managed)

# ---------------------------------------------------------------------------
# Real selected manifest. Artifact identity is frozen by the passed V6 run.
# ---------------------------------------------------------------------------
chat_template = """{%- if tools %}
 {{- '<|im_start|>system\\n' }}
 {%- if messages[0].role == 'system' %}
 {{- messages[0].content + '\\n\\n' }}
 {%- endif %}
 {{- "# Tools\\n\\nYou may call one or more functions to assist with the user query.\\n\\nYou are provided with function signatures within <tools></tools> XML tags:\\n<tools>" }}
 {%- for tool in tools %}
 {{- "\\n" }}
 {{- tool | tojson }}
 {%- endfor %}
 {{- "\\n</tools>\\n\\nFor each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\\n<tool_call>\\n{\\"name\\": <function-name>, \\"arguments\\": <args-json-object>}\\n</tool_call><|im_end|>\\n" }}
{%- else %}
 {%- if messages[0].role == 'system' %}
 {{- '<|im_start|>system\\n' + messages[0].content + '<|im_end|>\\n' }}
 {%- endif %}
{%- endif %}
{%- set ns = namespace(multi_step_tool=true, last_query_index=messages|length - 1) %}
{%- for message in messages[::-1] %}
 {%- set index = (messages|length - 1) - loop.index0 %}
 {%- if ns.multi_step_tool and message.role == "user" and not(message.content.startswith('<tool_response>') and message.content.endswith('</tool_response>')) %}
 {%- set ns.multi_step_tool = false %}
 {%- set ns.last_query_index = index %}
 {%- endif %}
{%- endfor %}
{%- for message in messages %}
 {%- if (message.role == "user") or (message.role == "system" and not loop.first) %}
 {{- '<|im_start|>' + message.role + '\\n' + message.content + '<|im_end|>' + '\\n' }}
 {%- elif message.role == "assistant" %}
 {%- set content = message.content %}
 {%- set reasoning_content = '' %}
 {%- if message.reasoning_content is defined and message.reasoning_content is not none %}
 {%- set reasoning_content = message.reasoning_content %}
 {%- else %}
 {%- if '</think>' in message.content %}
 {%- set content = message.content.split('</think>')[-1].lstrip('\\n') %}
 {%- set reasoning_content = message.content.split('</think>')[0].rstrip('\\n').split('<think>')[-1].lstrip('\\n') %}
 {%- endif %}
 {%- endif %}
 {%- if loop.index0 > ns.last_query_index %}
 {%- if loop.last or (not loop.last and reasoning_content) %}
 {{- '<|im_start|>' + message.role + '\\n<think>\\n' + reasoning_content.strip('\\n') + '\\n</think>\\n\\n' + content.lstrip('\\n') }}
 {%- else %}
 {{- '<|im_start|>' + message.role + '\\n' + content }}
 {%- endif %}
 {%- else %}
 {{- '<|im_start|>' + message.role + '\\n' + content }}
 {%- endif %}
 {%- if message.tool_calls %}
 {%- for tool_call in message.tool_calls %}
 {%- if (loop.first and content) or (not loop.first) %}
 {{- '\\n' }}
 {%- endif %}
 {%- if tool_call.function %}
 {%- set tool_call = tool_call.function %}
 {%- endif %}
 {{- '<tool_call>\\n{"name": "' }}
 {{- tool_call.name }}
 {{- '", "arguments": ' }}
 {%- if tool_call.arguments is string %}
 {{- tool_call.arguments }}
 {%- else %}
 {{- tool_call.arguments | tojson }}
 {%- endif %}
 {{- '}\\n</tool_call>' }}
 {%- endfor %}
 {%- endif %}
 {{- '<|im_end|>\\n' }}
 {%- elif message.role == "tool" %}
 {%- if loop.first or (messages[loop.index0 - 1].role != "tool") %}
 {{- '<|im_start|>user' }}
 {%- endif %}
 {{- '\\n<tool_response>\\n' }}
 {{- message.content }}
 {{- '\\n</tool_response>' }}
 {%- if loop.last or (messages[loop.index0 + 1].role != "tool") %}
 {{- '<|im_end|>\\n' }}
 {%- endif %}
 {%- endif %}
{%- endfor %}
{%- if add_generation_prompt %}
 {{- '<|im_start|>assistant\\n' }}
 {%- if enable_thinking is defined and enable_thinking is false %}
 {{- '<think>\\n\\n</think>\\n\\n' }}
 {%- endif %}
{%- endif %}"""

selected_manifest = {
    "schema_version": 1,
    "identity": {
        "manifest_id": "rma133.qwen3-0.6b-q4-k-m.v1",
        "model_id": "qwen3-0.6b",
        "display_name": "Qwen3 0.6B Q4_K_M",
        "model_version": "q4_k_m-8e42d41",
        "source_uri": "https://huggingface.co/Qwen/Qwen3-0.6B-GGUF",
        "source_revision": "8e42d41f70cb6c571f58c3f31bd9287b372d97cc",
        "license_id": "Apache-2.0",
        "experimental": False,
        "experimental_reason": "",
    },
    "runtime": {
        "runtime_id": "reachy_llama",
        "abi_version": 2,
        "requires_network_access": False,
    },
    "artifact": {
        "relative_path": "qwen3/qwen3-0.6b-q4_k_m.gguf",
        "file_size_bytes": 396704416,
        "sha256": "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e",
    },
    "gguf_metadata": {
        "gguf_version": 3,
        "architecture": "qwen3",
        "quantization": "Q4_K_M",
        "parameter_count": 596049920,
        "tokenizer_model": "gpt2",
        "tokenizer_pre": "qwen2",
    },
    "inference": {
        "context_limit_tokens": 40960,
        "chat_template": chat_template,
        "stop_tokens": ["<|im_end|>", "<|endoftext|>"],
        "memory_estimate": {
            "peak_ram_bytes": 740380672,
            "basis_context_tokens": 2048,
            "basis_batch_tokens": 256,
        },
        "recommended_threads": 4,
    },
    "device_compatibility": {
        "android_abis": ["arm64-v8a"],
        "minimum_android_api": 26,
        "required_cpu_features": [],
        "minimum_ram_bytes": 740380672,
        "reachy_llama_abi_version": 2,
    },
}
write(
    "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json",
    json.dumps(selected_manifest, indent=2, ensure_ascii=False) + "\n",
)

# ---------------------------------------------------------------------------
# Deterministic manifest tests: validate both manifests and bind the selected
# manifest to the immutable V6 candidate artifact identity and benchmark profile.
# ---------------------------------------------------------------------------
pytests = read("scripts/tests/test_rma131_local_model_manifest.py")
pytests = pytests.replace(
    'FIXTURE_PATH = ROOT / "models/manifests/examples/rma131-synthetic-experimental.local-llm.json"\nSCHEMA_PATH = ROOT / "models/manifests/local-llm-manifest.schema.json"',
    'FIXTURE_PATH = ROOT / "models/manifests/examples/rma131-synthetic-experimental.local-llm.json"\nSELECTED_PATH = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"\nV6_CONFIG_PATH = ROOT / "benchmarks/rma133/candidates-v6.json"\nSCHEMA_PATH = ROOT / "models/manifests/local-llm-manifest.schema.json"',
)
pytests = pytests.replace(
    'lambda doc: doc["device_compatibility"].__setitem__("reachy_llama_abi_version", 2),\n        "must equal ABI 1",\n        "runtime ABI mismatch",',
    'lambda doc: doc["device_compatibility"].__setitem__("reachy_llama_abi_version", 1),\n        "must equal ABI 2",\n        "runtime ABI mismatch",',
)
selected_test = '''\n\ndef test_selected_manifest_matches_frozen_v6() -> None:\n    selected = json.loads(SELECTED_PATH.read_text(encoding="utf-8"))\n    config = json.loads(V6_CONFIG_PATH.read_text(encoding="utf-8"))\n    require_valid(selected, "selected Qwen3 manifest")\n    candidates = {item["candidate_id"]: item for item in config["candidates"]}\n    candidate = candidates["qwen3-0.6b-q4-k-m"]\n    if selected["identity"]["experimental"] is not False:\n        raise AssertionError("RMA-133 selected manifest must not remain experimental.")\n    if selected["identity"]["license_id"] != "Apache-2.0":\n        raise AssertionError("Selected manifest license changed from the accepted candidate.")\n    if selected["identity"]["source_revision"] != candidate["source_revision"]:\n        raise AssertionError("Selected manifest source revision does not match V6.")\n    if selected["artifact"]["file_size_bytes"] != candidate["artifact"]["file_size_bytes"]:\n        raise AssertionError("Selected manifest artifact size does not match V6.")\n    if selected["artifact"]["sha256"] != candidate["artifact"]["sha256"]:\n        raise AssertionError("Selected manifest artifact hash does not match V6.")\n    if selected["runtime"] != {"runtime_id": "reachy_llama", "abi_version": 2, "requires_network_access": False}:\n        raise AssertionError("Selected manifest must require only local reachy_llama ABI 2.")\n    if selected["device_compatibility"]["reachy_llama_abi_version"] != 2:\n        raise AssertionError("Selected manifest device compatibility must require ABI 2.")\n    inference = selected["inference"]\n    if inference["context_limit_tokens"] != 40960:\n        raise AssertionError("Selected manifest context must match measured Qwen3 metadata.")\n    if inference["memory_estimate"] != {"peak_ram_bytes": 740380672, "basis_context_tokens": 2048, "basis_batch_tokens": 256}:\n        raise AssertionError("Selected manifest memory profile must match V6 evidence.")\n    if inference["recommended_threads"] != 4:\n        raise AssertionError("Selected manifest thread recommendation must match V6.")\n    if not inference["chat_template"].strip() or not inference["stop_tokens"]:\n        raise AssertionError("Selected manifest must contain explicit tokenizer/chat metadata.")\n'''
marker = "\ndef test_ui_has_no_candidate_model_ids() -> None:\n"
if selected_test.strip() not in pytests:
    pytests = pytests.replace(marker, selected_test + marker, 1)
pytests = pytests.replace(
    "        test_device_compatibility_rejections,\n        test_ui_has_no_candidate_model_ids,",
    "        test_device_compatibility_rejections,\n        test_selected_manifest_matches_frozen_v6,\n        test_ui_has_no_candidate_model_ids,",
)
pytests = pytests.replace(
    'print("RMA-131 local-model JSON manifest contracts passed (7 groups, 28 checks).")',
    'print("RMA-131 local-model JSON manifest contracts passed (8 groups; synthetic + selected manifests).")',
)
write("scripts/tests/test_rma131_local_model_manifest.py", pytests)

# ---------------------------------------------------------------------------
# RMA-131 permanent workflow must actually watch/validate/evidence the real
# selected manifest and report the active ABI accurately.
# ---------------------------------------------------------------------------
workflow = read(".github/workflows/rma131-local-model-manifest.yml")
workflow = workflow.replace(
    "      - 'models/manifests/examples/rma131-synthetic-experimental.local-llm.json'",
    "      - 'models/manifests/**/*.local-llm.json'",
)
workflow = workflow.replace(
    "      - name: Validate committed synthetic manifest without network\n        run: >-\n          python3 scripts/validate_local_llm_manifest.py\n          models/manifests/examples/rma131-synthetic-experimental.local-llm.json",
    "      - name: Validate committed manifests without network\n        run: |\n          python3 scripts/validate_local_llm_manifest.py models/manifests/examples/rma131-synthetic-experimental.local-llm.json\n          python3 scripts/validate_local_llm_manifest.py models/manifests/qwen3-0.6b-q4-k-m.local-llm.json",
)
workflow = workflow.replace(
    "          python3 -m json.tool \\\n            models/manifests/examples/rma131-synthetic-experimental.local-llm.json \\\n            > /dev/null",
    "          python3 -m json.tool models/manifests/examples/rma131-synthetic-experimental.local-llm.json > /dev/null\n          python3 -m json.tool models/manifests/qwen3-0.6b-q4-k-m.local-llm.json > /dev/null",
)
workflow = workflow.replace(
    "            models/manifests/examples/rma131-synthetic-experimental.local-llm.json \\\n            scripts/validate_local_llm_manifest.py",
    "            models/manifests/examples/rma131-synthetic-experimental.local-llm.json \\\n            models/manifests/qwen3-0.6b-q4-k-m.local-llm.json \\\n            scripts/validate_local_llm_manifest.py",
)
workflow = workflow.replace('"runtime_abi_version": 1,', '"runtime_abi_version": 2,')
workflow = workflow.replace('"synthetic_fixture_only": True,', '"synthetic_fixture_only": False,')
workflow = workflow.replace('"real_model_selected": False,', '"real_model_selected": True,\n              "selected_model_id": "qwen3-0.6b",\n              "selected_artifact_sha256": "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e",')
write(".github/workflows/rma131-local-model-manifest.yml", workflow)

# ---------------------------------------------------------------------------
# Current architecture/docs. Historical validation and ABI-1 snapshots are not
# rewritten.
# ---------------------------------------------------------------------------
manifest_doc = read("docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md")
manifest_doc = manifest_doc.replace("`runtime` — exactly `reachy_llama` ABI 1", "`runtime` — exactly `reachy_llama` ABI 2")
manifest_doc = manifest_doc.replace("It matches the initial sub-1B GGUF\nwork", "It matches the initial single-GGUF\nwork")
manifest_doc = manifest_doc.replace("exact\n`reachy_llama` ABI 1", "exact\n`reachy_llama` ABI 2")
manifest_doc = manifest_doc.replace(
    "## Synthetic fixture\n\n`models/manifests/examples/rma131-synthetic-experimental.local-llm.json` is intentionally fake",
    "## Selected RMA-133 manifest\n\n`models/manifests/qwen3-0.6b-q4-k-m.local-llm.json` is the first real selected-model manifest. It requires active `reachy_llama` ABI 2 and records the exact Qwen3-0.6B Q4_K_M revision, byte size, SHA-256, tokenizer/chat metadata, and V6 benchmark-backed context/thread/memory profile. It does not bundle the GGUF and does not authorize provider/model fallback.\n\nThe schema shape remains version 1 because no manifest field or interpretation was added; the active runtime compatibility policy moved from historical ABI 1 to ABI 2 after RMA-133 constrained-generation validation. Historical RMA-131 and RMA-130 ABI-1 validation records remain immutable evidence of the earlier accepted boundary.\n\n## Synthetic fixture\n\n`models/manifests/examples/rma131-synthetic-experimental.local-llm.json` is intentionally fake",
)
write("docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md", manifest_doc)

readme = read("models/manifests/README.md")
readme = readme.replace(
    "The committed `examples/rma131-synthetic-experimental.local-llm.json` is deliberately synthetic.",
    "The active schema-version-1 compatibility policy requires `reachy_llama` ABI 2. The selected `qwen3-0.6b-q4-k-m.local-llm.json` records the exact RMA-133 V6 winner; it is metadata only and does not bundle or automatically download the GGUF.\n\nThe committed `examples/rma131-synthetic-experimental.local-llm.json` remains deliberately synthetic.",
)
write("models/manifests/README.md", readme)

spec = read("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md")
spec = spec.replace(
    "and a local sub-1B-class LLM form the default configuration.",
    "and a small benchmark-selected local LLM form the default configuration.",
)
spec = spec.replace(
    "Local GGUF LLM approximately 1B parameters or smaller through an Android-native inference runtime, initially llama.cpp.",
    "Local GGUF LLM selected through measured Android-device quality, memory, speed, and thermal gates through an Android-native inference runtime, initially llama.cpp.",
)
spec = spec.replace(
    "The initial model candidate shall be approximately 1B parameters or smaller, with Qwen3-0.6B-class models as an initial benchmark candidate. The final bundled or recommended model MUST be chosen through device testing, license review, quality evaluation, and thermal measurement.",
    "The initial local model shall remain small enough for supported Android devices, but parameter count alone is not an acceptance gate. Candidates up to the documented RMA-133 size policy may be evaluated, and the recommended model MUST be chosen through device testing, license review, quality evaluation, memory/speed measurement, and thermal measurement.",
)
write("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md", spec)

benchmark_doc = read("docs/architecture/RMA_133_LOCAL_LLM_BENCHMARK.md")
insert = '''## V6 selected outcome — 2026-08-08\n\nPermanent run `31257650251` on physical LG-H872 job `93103766921` selected `qwen3-0.6b-q4-k-m`. The candidate completed 12/12 cases with schema reliability 1.0, semantic quality 85.4167, mean decode 2.3465 tokens/s, peak RSS 740,380,672 bytes, peak battery temperature 37.1 C, and a 5.9 C rise. The Qwen2.5-Coder-1.5B candidate remained constrained and structurally reliable but scored 83.3333 semantic quality, below the frozen 85 gate.\n\nThe malformed-grammar negative control terminated with status 16 and zero text events. Artifact `9022498818` has digest `sha256:b529602b281ff948d4ce581534784ca86fce32e62f5dcab122f34b901c67e4b4`. The permanent validation record is `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md`.\n\n'''
if "## V6 selected outcome — 2026-08-08" not in benchmark_doc:
    benchmark_doc = benchmark_doc.replace("## Downstream boundary\n", insert + "## Downstream boundary\n", 1)
write("docs/architecture/RMA_133_LOCAL_LLM_BENCHMARK.md", benchmark_doc)

# Main roadmap: close accepted historical RMA-130 and the now-selected RMA-133,
# while explicitly distinguishing the later ABI-2 extension from ABI-1 evidence.
roadmap = read("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
for task, next_task in (("## RMA-130 — Build llama.cpp Android native plug-in", "## RMA-131 —"), ("## RMA-133 — Benchmark and select initial local model", "## RMA-134 —")):
    start = roadmap.index(task)
    end = roadmap.index(next_task, start)
    section = roadmap[start:end].replace("- [ ]", "- [x]")
    if "**Status:** Complete" not in section:
        section = section.replace("\n\n", "\n\n**Status:** Complete (2026-08-08)\n\n", 1)
    if task.startswith("## RMA-130") and "**Completion evidence — RMA-130**" not in section:
        section += "**Completion evidence — RMA-130**\n\n- Historical ABI-1 implementation remains accepted at source SHA `11233d2967d9864f35f1684da13018110196f682`; dedicated run `31203427475`, job `92948655063`, and artifact `9003903370` passed.\n- RMA-133 later extended the same pinned-runtime boundary to ABI 2 for explicit GBNF constrained generation. That extension is additive evidence and does not rewrite the accepted ABI-1 record in `docs/validation/RMA_130_LLAMA_CPP_ANDROID_RUNTIME_VALIDATION_2026-08-07.md`.\n\n"
    if task.startswith("## RMA-133") and "**Completion evidence — RMA-133**" not in section:
        section += "**Completion evidence — RMA-133**\n\n- V6 permanent physical run `31257650251`, job `93103766921`, on LG-H872 selected `qwen3-0.6b-q4-k-m` under unchanged 12/12, schema 1.0, semantic >=85, decode >=1 token/s, RSS <=1.5 GB, battery <45 C, and rise <=10 C gates.\n- Selected metrics: semantic 85.4167, schema 1.0, decode 2.3465 token/s, peak RSS 740,380,672 bytes, battery peak 37.1 C, rise 5.9 C.\n- The malformed-grammar control failed closed with status 16 and zero text events; no repair or unconstrained fallback exists.\n- Real manifest: `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`, requiring `reachy_llama` ABI 2 and exact artifact SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`.\n- Full evidence: `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md`.\n\n"
    roadmap = roadmap[:start] + section + roadmap[end:]
roadmap = roadmap.replace(
    "Local sub-1B-class LLM works without blocking physics.",
    "Selected benchmark-backed local LLM works without blocking physics.",
)
write("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md", roadmap)

# Constrained-generation TODO: everything through CG-094 is implemented. CG-095
# remains open until the final master exact-SHA hosted/physical policy gates pass.
cg = read("docs/RMA_133_CONSTRAINED_GENERATION_TODO_2026-08-08.md")
if "**Status:**" not in cg[:800]:
    first_break = cg.index("\n\n", cg.index("# "))
    cg = cg[:first_break] + "\n\n**Status:** V6 selected; Phase 9A closure validation in progress" + cg[first_break:]
start = 0
end = cg.index("### RMA-133-CG-095")
cg = cg[:start] + cg[start:end].replace("- [ ]", "- [x]") + cg[end:]
cg = cg.replace(
    "## Phase 9B — No-pass disposition if V6 fails",
    "## Phase 9B — No-pass disposition if V6 fails — not applicable (V6 selected Qwen3-0.6B)",
)
phase8_marker = "## Phase 9A — Selected-model closure if V6 passes"
phase8_evidence = '''### V6 selection evidence\n\n- Permanent run: `31257650251`.\n- Physical job: `93103766921` on LG-H872 (`LGH87250967ab9`, ARM64, API 26).\n- Evidence artifact: `9022498818`; digest `sha256:b529602b281ff948d4ce581534784ca86fce32e62f5dcab122f34b901c67e4b4`.\n- Selected candidate: `qwen3-0.6b-q4-k-m`.\n- Selected metrics: 12/12 complete, schema reliability 1.0, semantic quality 85.4167, decode 2.3465 token/s, peak RSS 740,380,672 bytes, battery peak 37.1 C, rise 5.9 C.\n- Negative control: malformed grammar -> status 16, zero text events, no unconstrained output.\n\n'''
if "### V6 selection evidence" not in cg:
    cg = cg.replace(phase8_marker, phase8_evidence + phase8_marker, 1)
write("docs/RMA_133_CONSTRAINED_GENERATION_TODO_2026-08-08.md", cg)

# Permanent V6 validation record.
validation = '''# RMA-133 candidate-set V6 constrained-generation validation — 2026-08-08\n\n**Status:** Selected — Qwen3-0.6B Q4_K_M\n\n## Immutable experiment identity\n\n- Source SHA: `e3007579d0365d31f5d5efc378fc81a13f2d705e`\n- Benchmark lineage: `rma133-initial-local-model-v3`\n- Experiment: `rma133-candidate-set-v6-constrained-generation`\n- Permanent workflow run: `31257650251`\n- Hosted contract job: `93103412276` — success\n- Hosted ABI-2 job: `93103436444` — success\n- Physical Android job: `93103766921` — success\n- Device: LG-H872, serial `LGH87250967ab9`, `arm64-v8a`, API 26\n- Evidence artifact: `9022498818`\n- Artifact digest: `sha256:b529602b281ff948d4ce581534784ca86fce32e62f5dcab122f34b901c67e4b4`\n- llama.cpp pin: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`\n- `reachy_llama` runtime ABI: 2\n\n## Frozen contract\n\nV6 preserves every V5 numerical acceptance gate and candidate artifact. It changes only the explicitly versioned generation/oracle contract: GBNF generation is mandatory through ABI 2 and behavior cases use the corrected V2 semantic oracle.\n\n- system prompt SHA-256: `0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20`\n- grammar SHA-256: `2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734`\n- grammar root/type: `root` / `GBNF`\n- behavior cases SHA-256: `f5df82ec92022192a351a0bb61d7c2ef2e8b71206de4a941a10e547735f18cfa`\n- required completed cases: 12/12\n- required schema reliability: 1.0\n- minimum semantic quality: 85/100\n- minimum mean decode: 1 token/s\n- maximum peak RSS: 1,500,000,000 bytes\n- maximum battery temperature: 45.0 C\n- maximum battery rise: 10.0 C\n\nThere is no Markdown-fence stripping, JSON repair, partial-parse recovery, unconstrained retry, model/provider substitution, or threshold reduction.\n\n## Malformed-grammar negative control\n\nBefore either candidate could be eligible, the physical runner attempted a deliberately malformed grammar. The runtime returned terminal constraint-initialization status `16`, emitted zero text events, produced no response bytes, and exited nonzero. This proves the device path fails closed rather than silently reverting to unconstrained generation.\n\n## Candidate results\n\n| Candidate | Complete | Schema | Semantic | Decode tok/s | Peak RSS | Battery peak | Rise | Eligible |\n|---|---:|---:|---:|---:|---:|---:|---:|---|\n| Qwen3-0.6B Q4_K_M | 12/12 | 1.000 | 85.4167 | 2.3465 | 740,380,672 B | 37.1 C | 5.9 C | yes |\n| Qwen2.5-Coder-1.5B-Instruct Q4_K_M | 12/12 | 1.000 | 83.3333 | 1.4027 | 1,222,868,992 B | 38.9 C | 3.7 C | no |\n\nQwen3 load time was 1,105.299 ms, mean time-to-first-text 31,072.890 ms, parameter count 596,049,920, and reported training context 40,960 tokens. Qwen2.5-Coder failed only the frozen semantic gate (`83.33 < 85`).\n\n## Selection\n\nThe deterministic selector returned:\n\n```text\nstatus=selected\nselected_candidate_id=qwen3-0.6b-q4-k-m\n```\n\nThe selected artifact is the official Qwen GGUF revision `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`, file `Qwen3-0.6B-Q4_K_M.gguf`, exact size `396704416` bytes, SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`, Apache-2.0.\n\nThe real selected-model manifest is `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`. It requires active `reachy_llama` ABI 2 and carries the V6 benchmark-backed 2,048-token measurement context, 256-token batch, 4-thread recommendation, measured 740,380,672-byte peak-RSS estimate, and explicit Qwen tokenizer/chat metadata. No GGUF is committed or bundled.\n\n## Disposition\n\nV6 passes the RMA-133 model-selection gate. Qwen3-0.6B Q4_K_M is the initial recommended local model. RMA-134 remains responsible for production provider integration and RMA-135 for production thermal/resource governance. Historical V1-V5 evidence remains unchanged.\n'''
write("docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md", validation)

print("RMA-133 V6 closure transformation complete")
