#!/usr/bin/env python3
"""Guard RMA-125 A54 speech-capability evidence against stale blockers/overclaims."""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
REVALIDATION = (
    ROOT
    / "docs"
    / "validation"
    / "RMA_125_A54_SPEECH_CAPABILITY_REVALIDATION_2026-09-30.md"
)
BLOCKER = ROOT / "docs" / "blockers" / "RMA-125_PHYSICAL_OFFLINE_SPEECH_BLOCKER.md"
WORKFLOW = ROOT / ".github" / "workflows" / "rma125-speech-audio-focus.yml"


def read(path: pathlib.Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except FileNotFoundError as exc:
        raise AssertionError(f"required RMA-125 file is missing: {path}") from exc


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"{label} is missing required text: {needle!r}")


def require_unchecked(todo: str, label: str) -> None:
    unchecked = f"- [ ] {label}"
    checked = f"- [x] {label}"
    if unchecked not in todo:
        raise AssertionError(f"RMA-125 TODO criterion is not visibly open: {label!r}")
    if checked in todo:
        raise AssertionError(
            "RMA-125 TODO criterion was checked without full "
            f"spoken-conversation evidence: {label!r}"
        )


def main() -> int:
    todo = read(TODO)
    revalidation = read(REVALIDATION)
    blocker = read(BLOCKER)
    workflow = read(WORKFLOW)

    for label in (
        "On a device with installed services, the user can converse using Android "
        "on-device ASR and offline TTS with networking disabled.",
        "Missing services produce visible setup guidance.",
        "No audio is sent to a cloud provider unless explicitly selected.",
    ):
        require_unchecked(todo, label)

    require(revalidation, "SM-A546E", "A54 revalidation report")
    require(
        revalidation,
        "Exact source SHA: `35928d183fb48d12d228fdd4b8a2f1515db6bb0e`",
        "A54 revalidation report",
    )
    require(revalidation, "Run: `36752801658`", "A54 revalidation report")
    require(revalidation, "Job: `110015411312`", "A54 revalidation report")
    require(revalidation, "RMA-125 A54 speech revalidation contract", "A54 revalidation report")
    require(revalidation, "on_device_asr_state == \"Available\"", "A54 revalidation report")
    require(revalidation, "selected_tts_voice_may_use_network == false", "A54 revalidation report")
    require(revalidation, "does **not** close RMA-125", "A54 revalidation report")
    require(revalidation, "do not inject a transcript", "A54 revalidation report")
    require(revalidation, "network-disabled evidence", "A54 revalidation report")

    require(
        blocker,
        "Hardware/API prerequisite cleared on A54; "
        "network-disabled spoken-conversation acceptance still open",
        "RMA-125 blocker",
    )
    if "**Status:** Blocked on an API-31+ physical Android device" in blocker:
        raise AssertionError("RMA-125 blocker still has the stale API-31+ hardware-blocker status")
    require(
        blocker,
        "This updated evidence clears only the device/API/service-readiness prerequisite.",
        "RMA-125 blocker",
    )
    require(
        blocker,
        "claiming A54 service-capability probes as a real network-disabled "
        "spoken conversation",
        "RMA-125 blocker",
    )
    require(blocker, "Use the connected physical Samsung A54", "RMA-125 blocker")

    require(workflow, "scripts/tests/test_rma125_a54_speech_revalidation.py", "RMA-125 workflow")
    require(
        workflow,
        "docs/validation/RMA_125_A54_SPEECH_CAPABILITY_REVALIDATION_2026-09-30.md",
        "RMA-125 workflow",
    )
    pattern = (
        r"Run A54 speech revalidation contract\s*\n\s*run: "
        r"python3 scripts/tests/test_rma125_a54_speech_revalidation\.py"
    )
    if not re.search(pattern, workflow):
        raise AssertionError("RMA-125 workflow does not execute the A54 revalidation contract")

    print("RMA-125 A54 speech revalidation contract passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
