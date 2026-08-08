#!/usr/bin/env python3
"""Physical Android runner for the frozen RMA-133 V6 constrained benchmark."""

from __future__ import annotations

import hashlib
import json
import os
import shlex
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
CONFIG = Path(os.environ.get("RMA133_CONFIG", ROOT / "benchmarks/rma133/candidates-v6.json"))
CASES = Path(os.environ.get("RMA133_CASES", ROOT / "benchmarks/rma133/behavior_cases-v2.tsv"))
PROMPT = Path(
    os.environ.get("RMA133_SYSTEM_PROMPT", ROOT / "benchmarks/rma133/system_prompt-v4.txt")
)
GRAMMAR = Path(os.environ.get("RMA133_GRAMMAR", ROOT / "benchmarks/rma133/behavior-output-v1.gbnf"))
RUNTIME = (
    Path(os.environ.get("RMA133_RUNTIME_DIR", ROOT / "build/rma133/runtime")) / "libreachy_llama.so"
)
BENCH = (
    Path(os.environ.get("RMA133_BENCHMARK_OUTPUT_DIR", ROOT / "build/rma133/benchmark"))
    / "rma133_benchmark_v6"
)
RESULTS = Path(os.environ.get("RMA133_RESULTS_DIR", ROOT / "build/rma133/results"))
CACHE = Path(
    os.environ.get("RMA133_MODEL_CACHE_DIR", Path.home() / ".cache/weachy-mini/rma133/models")
)
REMOTE = (
    f"/data/local/tmp/reachy-rma133-v6-{os.environ.get('GITHUB_RUN_ID', 'manual')}-{os.getpid()}"
)
SCORER = ROOT / "scripts/score_rma133_benchmark_v6.py"


def run(args: list[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(args, check=check, text=True, stdout=subprocess.PIPE, stderr=None)


def adb(serial: str, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return run(["adb", "-s", serial, *args], check=check)


def shell(serial: str, argv: list[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    return adb(serial, "shell", shlex.join(argv), check=check)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_file(path: Path) -> None:
    if not path.is_file() or path.stat().st_size == 0:
        raise SystemExit(f"RMA-133 V6 required input missing/empty: {path}")


def model_cache(candidate: dict[str, Any]) -> Path:
    art = candidate["artifact"]
    CACHE.mkdir(parents=True, exist_ok=True)
    target = CACHE / f"{art['sha256']}-{art['filename']}"
    if target.exists() and (
        target.stat().st_size != art["file_size_bytes"] or sha256(target) != art["sha256"]
    ):
        target.unlink()
    if not target.exists():
        partial = target.with_name(target.name + f".partial.{os.getpid()}")
        partial.unlink(missing_ok=True)
        subprocess.run(
            [
                "curl",
                "--fail-with-body",
                "--location",
                "--proto",
                "=https",
                "--tlsv1.2",
                "--retry",
                "2",
                "--retry-all-errors",
                "--output",
                str(partial),
                art["url"],
            ],
            check=True,
        )
        if partial.stat().st_size != art["file_size_bytes"] or sha256(partial) != art["sha256"]:
            partial.unlink(missing_ok=True)
            raise SystemExit(f"RMA-133 V6 artifact integrity failure: {candidate['candidate_id']}")
        partial.replace(target)
    return target


def benchmark_command(
    config: dict[str, Any],
    candidate: dict[str, Any],
    grammar_remote: str,
    grammar_path: str,
    grammar_sha: str,
) -> list[str]:
    profile = config["runtime_profile"]
    thermal = config["selection_policy"]["maximum_battery_temperature_c"]
    suffix = candidate["user_prompt_suffix"] or "-"
    return [
        f"{REMOTE}/rma133_benchmark_v6",
        f"{REMOTE}/model.gguf",
        candidate["candidate_id"],
        f"{REMOTE}/behavior_cases-v2.tsv",
        f"{REMOTE}/system_prompt-v4.txt",
        suffix,
        str(profile["context_tokens"]),
        str(profile["batch_tokens"]),
        str(profile["micro_batch_tokens"]),
        str(profile["max_generated_tokens"]),
        str(profile["threads"]),
        str(profile["batch_threads"]),
        str(profile["temperature"]),
        str(profile["min_p"]),
        str(profile["seed"]),
        str(profile["stream_queue_capacity"]),
        str(thermal),
        grammar_remote,
        grammar_path,
        grammar_sha,
        config["constrained_generation_contract"]["grammar_root"],
        "GBNF",
    ]


def main() -> int:
    for tool in ("adb", "curl"):
        if shutil.which(tool) is None:
            raise SystemExit(f"RMA-133 V6 required command missing: {tool}")
    for path in (CONFIG, CASES, PROMPT, GRAMMAR, RUNTIME, BENCH, SCORER):
        require_file(path)
    subprocess.run(
        [sys.executable, str(SCORER), "validate", "--config", str(CONFIG), "--cases", str(CASES)],
        check=True,
    )
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    contract = config["constrained_generation_contract"]

    serial = os.environ.get("RMA133_DEVICE_SERIAL", "")
    if not serial:
        lines = run(["adb", "devices"]).stdout.splitlines()[1:]
        devices = [
            line.split()[0]
            for line in lines
            if len(line.split()) >= 2 and line.split()[1] == "device"
        ]
        if len(devices) != 1:
            raise SystemExit(
                f"RMA-133 V6 requires exactly one authorized device; found {len(devices)}"
            )
        serial = devices[0]
    if adb(serial, "get-state").stdout.strip() != "device":
        raise SystemExit("RMA-133 V6 Android device is not ready")
    abi = shell(serial, ["getprop", "ro.product.cpu.abi"]).stdout.strip()
    api_raw = shell(serial, ["getprop", "ro.build.version.sdk"]).stdout.strip()
    model = shell(serial, ["getprop", "ro.product.model"]).stdout.strip()
    qemu = shell(serial, ["getprop", "ro.kernel.qemu"]).stdout.strip()
    if abi != "arm64-v8a" or not api_raw.isdigit() or int(api_raw) < 26 or qemu == "1":
        raise SystemExit(
            f"RMA-133 V6 requires physical ARM64 API26+; ABI={abi} API={api_raw} qemu={qemu}"
        )

    RESULTS.mkdir(parents=True, exist_ok=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    for old in RESULTS.iterdir():
        if old.is_file() or old.is_symlink():
            old.unlink()
        elif old.is_dir():
            shutil.rmtree(old)
    (RESULTS / "device.txt").write_text(
        f"serial={serial} model={model} ABI={abi} API={api_raw}\n", encoding="utf-8"
    )
    (RESULTS / "contract.txt").write_text(
        f"benchmark_id={config['benchmark_id']}\nconstraint_type=GBNF\ngrammar_path={contract['grammar_path']}\n"
        f"grammar_sha256={contract['grammar_sha256']}\ngrammar_root={contract['grammar_root']}\n",
        encoding="utf-8",
    )

    shell(serial, ["rm", "-rf", REMOTE], check=False)
    shell(serial, ["mkdir", "-p", REMOTE])
    try:
        for local, remote in (
            (RUNTIME, "libreachy_llama.so"),
            (BENCH, "rma133_benchmark_v6"),
            (CASES, "behavior_cases-v2.tsv"),
            (PROMPT, "system_prompt-v4.txt"),
            (GRAMMAR, "behavior-output-v1.gbnf"),
        ):
            adb(serial, "push", str(local), f"{REMOTE}/{remote}")
        shell(serial, ["chmod", "0755", f"{REMOTE}/rma133_benchmark_v6"])
        remote_grammar_sha = shell(
            serial, ["toybox", "sha256sum", f"{REMOTE}/behavior-output-v1.gbnf"]
        ).stdout.split()[0]
        if remote_grammar_sha != contract["grammar_sha256"]:
            raise SystemExit("RMA-133 V6 remote grammar SHA-256 mismatch")

        reports: list[Path] = []
        for index, candidate in enumerate(config["candidates"]):
            art = candidate["artifact"]
            cached = model_cache(candidate)
            free_kib = int(
                shell(serial, ["df", "-Pk", "/data/local/tmp"]).stdout.splitlines()[-1].split()[3]
            )
            need_kib = (art["file_size_bytes"] + 1023) // 1024 + 262144
            if free_kib < need_kib:
                raise SystemExit(
                    f"RMA-133 V6 insufficient device storage for {candidate['candidate_id']}"
                )
            shell(serial, ["rm", "-f", f"{REMOTE}/model.gguf"], check=False)
            adb(serial, "push", str(cached), f"{REMOTE}/model.gguf")
            remote_sha = shell(
                serial, ["toybox", "sha256sum", f"{REMOTE}/model.gguf"]
            ).stdout.split()[0]
            remote_size = int(
                shell(serial, ["stat", "-c", "%s", f"{REMOTE}/model.gguf"]).stdout.strip()
            )
            if remote_sha != art["sha256"] or remote_size != art["file_size_bytes"]:
                raise SystemExit(
                    f"RMA-133 V6 device model integrity failure: {candidate['candidate_id']}"
                )

            if index == 0:
                bad = RESULTS / "invalid-grammar.gbnf"
                bad.write_text("root ::= (\n", encoding="utf-8")
                adb(serial, "push", str(bad), f"{REMOTE}/invalid-grammar.gbnf")
                negative = shell(
                    serial,
                    benchmark_command(
                        config,
                        candidate,
                        f"{REMOTE}/invalid-grammar.gbnf",
                        "negative-control/invalid-grammar.gbnf",
                        sha256(bad),
                    ),
                    check=False,
                )
                (RESULTS / "constraint-negative-control.raw.jsonl").write_text(
                    negative.stdout, encoding="utf-8"
                )
                rows = [json.loads(line) for line in negative.stdout.splitlines() if line.strip()]
                constraints = [row for row in rows if row.get("record") == "constraint"]
                cases = [row for row in rows if row.get("record") == "case"]
                if (
                    negative.returncode == 0
                    or len(constraints) != 1
                    or constraints[0].get("terminal_error_status") != 16
                    or constraints[0].get("text_event_count") != 0
                    or constraints[0].get("constrained_mode_active") is not False
                    or len(cases) != 1
                    or cases[0].get("response_bytes_hex") != ""
                ):
                    raise SystemExit(
                        "RMA-133 V6 malformed-grammar negative control failed closed incorrectly"
                    )

            proc = shell(
                serial,
                benchmark_command(
                    config,
                    candidate,
                    f"{REMOTE}/behavior-output-v1.gbnf",
                    contract["grammar_path"],
                    contract["grammar_sha256"],
                ),
                check=False,
            )
            raw = RESULTS / f"{candidate['candidate_id']}.raw.jsonl"
            raw.write_text(proc.stdout, encoding="utf-8")
            (RESULTS / f"{candidate['candidate_id']}.benchmark-exit-code.txt").write_text(
                f"{proc.returncode}\n", encoding="utf-8"
            )
            report = RESULTS / f"{candidate['candidate_id']}.report.json"
            subprocess.run(
                [
                    sys.executable,
                    str(SCORER),
                    "score",
                    "--config",
                    str(CONFIG),
                    "--cases",
                    str(CASES),
                    "--raw",
                    str(raw),
                    "--candidate-id",
                    candidate["candidate_id"],
                    "--output",
                    str(report),
                ],
                check=True,
            )
            reports.append(report)
            shell(serial, ["rm", "-f", f"{REMOTE}/model.gguf"], check=False)

        selection = RESULTS / "selection.json"
        args = [sys.executable, str(SCORER), "select", "--config", str(CONFIG)]
        for report in reports:
            args += ["--report", str(report)]
        args += ["--output", str(selection)]
        selected = subprocess.run(args, text=True)
        data = json.loads(selection.read_text(encoding="utf-8"))
        lines = [
            f"benchmark_id={data['benchmark_id']}",
            f"status={data['status']}",
            f"selected_candidate_id={data['selected_candidate_id']}",
        ]
        for report in data["candidate_reports"]:
            metrics = report["measurements"]
            lines.append(
                f"candidate={report['candidate_id']} eligible={report['eligible']} constrained={report['constraint_evidence_valid']} quality={metrics['semantic_quality_score']:.2f} json={metrics['schema_reliability']:.3f} decode_tps={metrics['mean_decode_tokens_per_second']:.2f} peak_rss={metrics['peak_rss_bytes']}"  # noqa: E501
            )
        (RESULTS / "summary.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
        print("\n".join(lines))
        return selected.returncode
    finally:
        shell(serial, ["rm", "-rf", REMOTE], check=False)


if __name__ == "__main__":
    raise SystemExit(main())
