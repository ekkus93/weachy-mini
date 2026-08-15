#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MATRIX = ROOT / "models/reachy-mini/android-device-matrix.json"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Performance/ReachyRepresentativeDeviceMatrix.cs"


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> int:
    data = json.loads(MATRIX.read_text(encoding="utf-8"))
    if data.get("schema_version") != 1:
        fail("RMA-184 matrix schema_version must be 1")
    if data.get("contract_id") != "rma184_representative_device_matrix_v1":
        fail("RMA-184 matrix contract_id drifted")

    classes = {"low", "mid", "high"}
    profiles = data.get("default_profiles")
    if not isinstance(profiles, list) or len(profiles) != 3:
        fail("RMA-184 requires exactly three default class profiles")
    by_class = {entry.get("performance_class"): entry for entry in profiles}
    if set(by_class) != classes:
        fail("RMA-184 default profiles must cover low, mid, and high")

    expected = {
        "low": (30, "Conservative", 134217728),
        "mid": (30, "Balanced", 201326592),
        "high": (60, "Performance", 268435456),
    }
    for name, (fps, llm, memory) in expected.items():
        profile = by_class[name]
        if profile.get("target_fps") != fps:
            fail(f"RMA-184 {name} target_fps drifted")
        if profile.get("local_llm_profile") != llm:
            fail(f"RMA-184 {name} local LLM profile drifted")
        if profile.get("maximum_memory_growth_bytes") != memory:
            fail(f"RMA-184 {name} memory-growth budget drifted")
        expected_render = (1000.0 / fps) * 1.15
        actual_render = profile.get("maximum_render_p95_ms")
        if not isinstance(actual_render, int | float) or (
            abs(actual_render - expected_render) > 1e-9
        ):
            fail(f"RMA-184 {name} render p95 budget drifted")

    policy = data.get("support_policy", {})
    required_policy = {
        "minimum_android_api": 26,
        "minimum_ram_bytes": 3221225472,
        "minimum_logical_processors": 4,
        "minimum_explicit_on_device_asr_api": 31,
    }
    for key, value in required_policy.items():
        if policy.get(key) != value:
            fail(f"RMA-184 support policy drifted: {key}")
    if set(policy.get("supported_graphics_apis", [])) != {"Vulkan", "OpenGLES3"}:
        fail("RMA-184 graphics support policy drifted")

    measurement = data.get("measurement_policy", {})
    if measurement.get("minimum_long_run_seconds") != 1800:
        fail("RMA-184 long-run duration drifted")
    if measurement.get("maximum_physics_p95_ms") != 2.0:
        fail("RMA-184 physics target drifted")
    if measurement.get("maximum_state_lag_growth_seconds") != 0.002:
        fail("RMA-184 state-lag target drifted")
    if measurement.get("minimum_local_llm_decode_tokens_per_second") != 1.0:
        fail("RMA-184 local LLM decode target drifted")
    if measurement.get("thermal_degradation_contract") != "rma181_priority_degradation_v1":
        fail("RMA-184 thermal policy must bind to RMA-181")

    devices = data.get("representative_devices")
    if not isinstance(devices, list) or len(devices) < 3:
        fail("RMA-184 requires at least three representative devices")
    covered = {entry.get("performance_class") for entry in devices}
    if not classes.issubset(covered):
        fail("RMA-184 representative devices must cover low, mid, and high")

    required_device_fields = {
        "id",
        "performance_class",
        "manufacturer",
        "model",
        "soc",
        "android_version",
        "ram_configuration_gib",
        "ram_observation_status",
        "graphics_api",
        "gpu",
        "camera_capability",
        "on_device_asr",
        "offline_tts",
        "support_status",
        "measurement_status",
        "evidence",
    }
    ids: set[str] = set()
    for device in devices:
        missing = required_device_fields - set(device)
        if missing:
            fail(f"RMA-184 device record missing fields: {sorted(missing)}")
        device_id = device["id"]
        if not isinstance(device_id, str) or not device_id or device_id in ids:
            fail("RMA-184 device ids must be non-empty and unique")
        ids.add(device_id)
        if device["performance_class"] not in classes:
            fail(f"RMA-184 invalid performance class for {device_id}")
        if not device["ram_configuration_gib"]:
            fail(f"RMA-184 RAM configuration missing for {device_id}")
        if device["graphics_api"] not in policy["supported_graphics_apis"]:
            fail(f"RMA-184 representative graphics API unsupported for {device_id}")
        if device["measurement_status"].startswith("partial") and not device["evidence"]:
            fail(f"RMA-184 measured/partial device needs evidence: {device_id}")

    core = CORE.read_text(encoding="utf-8")
    for token in (
        "MinimumLongRunSeconds = 1800",
        "MaximumPhysicsP95Milliseconds = 2.0",
        "MaximumStateLagGrowthSeconds = 0.002",
        "MinimumLocalLlmDecodeTokensPerSecond = 1.0",
        "MinimumAndroidApiLevel = 26",
        "MinimumOfflineAsrApiLevel = 31",
        "MinimumMemoryBytes = 3L * 1024L * 1024L * 1024L",
        "LocalLlmDeviceProfileKind.Conservative",
        "LocalLlmDeviceProfileKind.Balanced",
        "LocalLlmDeviceProfileKind.Performance",
    ):
        if token not in core:
            fail(f"RMA-184 core contract missing token: {token}")

    print("RMA-184 representative-device matrix contract passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
