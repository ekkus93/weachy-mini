from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one formatter target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


scorer = ROOT / "scripts" / "score_rma133_benchmark.py"
tests = ROOT / "scripts" / "tests" / "test_rma133_benchmark_contract.py"

replace_once(
    scorer,
    "    return value, reasons\n\n\n\ndef _response_text_from_record",
    "    return value, reasons\n\n\ndef _response_text_from_record",
)
replace_once(
    tests,
    'response_bytes_override={case_id: b"{\\\"speech\\\":\\\"\\xf0\\x9f"},',
    'response_bytes_override={case_id: b\'{"speech":"\\xf0\\x9f\'},',
)

print("RMA-133 Ruff formatting applied")
