#!/usr/bin/env python3
"""Fail-closed validation of one freshly captured RMA-184 Android probe report."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def validate(report: object) -> dict[str, object]:
    if not isinstance(report, dict):
        raise ValueError("report must be a JSON object")
    if type(report.get("schema_version")) is not int or report["schema_version"] != 1:
        raise ValueError("schema_version must be integer 1")
    if report.get("status") != "passed" or report.get("error"):
        raise ValueError("probe did not report an unambiguous pass")

    for field in (
        "manufacturer",
        "model",
        "soc",
        "processor",
        "operating_system",
        "graphics_api",
        "graphics_device",
        "camera_permission",
        "on_device_asr",
        "offline_tts",
        "support_status",
        "support_diagnostic",
    ):
        if not isinstance(report.get(field), str) or not report[field].strip():
            raise ValueError(f"{field} must be a nonempty string")

    for field in (
        "logical_processor_count",
        "system_memory_mib",
        "android_api_level",
        "camera_count",
        "available_camera_count",
    ):
        if type(report.get(field)) is not int or report[field] < 0:
            raise ValueError(f"{field} must be a nonnegative integer")

    for field in ("rear_camera_available", "front_camera_available"):
        if type(report.get(field)) is not bool:
            raise ValueError(f"{field} must be a boolean")

    if report["android_api_level"] == 0 or report["logical_processor_count"] == 0:
        raise ValueError("Android API and processor count must be observed")
    if report["system_memory_mib"] == 0:
        raise ValueError("physical memory must be observed")
    if report["logical_processor_count"] > 1024:
        raise ValueError("logical processor count exceeds runtime contract")
    if report["available_camera_count"] > report["camera_count"]:
        raise ValueError("available cameras exceed discovered cameras")
    if (
        int(report["rear_camera_available"]) + int(report["front_camera_available"])
        > report["available_camera_count"]
    ):
        raise ValueError("camera-facing availability contradicts camera count")

    core_supported = (
        report["android_api_level"] >= 26
        and report["system_memory_mib"] >= 3072
        and report["logical_processor_count"] >= 4
        and report["graphics_api"].casefold() in {"vulkan", "opengles3", "opengl es 3"}
        and report["rear_camera_available"]
    )
    full_offline = (
        report["front_camera_available"]
        and report["android_api_level"] >= 31
        and report["on_device_asr"] == "Available"
        and report["offline_tts"] == "Available"
    )
    expected = (
        "Unsupported"
        if not core_supported
        else "Supported"
        if full_offline
        else "SupportedWithLimitations"
    )
    if report["support_status"] != expected:
        raise ValueError(
            "support_status contradicts observed capabilities: "
            f"expected {expected}, got {report['support_status']}"
        )
    return report


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: validate_rma184_device_probe.py REPORT.json", file=sys.stderr)
        return 2
    try:
        report = validate(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")))
    except (OSError, ValueError) as exc:
        print(f"RMA-184 probe validation failed: {exc}", file=sys.stderr)
        return 1
    print(
        "RMA-184 device probe passed: "
        f"{report['model']} api={report['android_api_level']} "
        f"ram_mib={report['system_memory_mib']} "
        f"graphics={report['graphics_api']} support={report['support_status']}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
