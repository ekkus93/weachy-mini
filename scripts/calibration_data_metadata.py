"""RMA-070 calibration dataset metadata validation: schema/robot/environment/clocks."""

from __future__ import annotations

import hashlib
import importlib.util
import sys
from pathlib import Path
from typing import Any

# --- Sibling-module bootstrap -------------------------------------------------
#
# This module depends on calibration_data_contracts (for the schema constants,
# `_require_*`/`_validate_*` primitives, and `canonical_json_bytes`). It is
# loaded either as part of the calibration_data.py facade's ordered bootstrap
# (in which case the sibling is already in sys.modules) or standalone /
# directly by path, in which case scripts/ is not necessarily on sys.path. To
# be self-sufficient in both cases, check sys.modules first and only fall back
# to loading the sibling by a path relative to this file if it isn't already
# registered.
if "calibration_data_contracts" in sys.modules:
    calibration_data_contracts = sys.modules["calibration_data_contracts"]
else:
    _contracts_spec = importlib.util.spec_from_file_location(
        "calibration_data_contracts",
        Path(__file__).with_name("calibration_data_contracts.py"),
    )
    if _contracts_spec is None or _contracts_spec.loader is None:
        raise RuntimeError("cannot load sibling calibration_data_contracts.py")
    calibration_data_contracts = importlib.util.module_from_spec(_contracts_spec)
    sys.modules["calibration_data_contracts"] = calibration_data_contracts
    _contracts_spec.loader.exec_module(calibration_data_contracts)

CONTRACT_ID = calibration_data_contracts.CONTRACT_ID
SCHEMA_VERSION = calibration_data_contracts.SCHEMA_VERSION
COLUMN_MANIFEST_ID = calibration_data_contracts.COLUMN_MANIFEST_ID
EXPECTED_SCHEMA_SHA256 = calibration_data_contracts.EXPECTED_SCHEMA_SHA256
EXPECTED_COLUMN_MANIFEST_SHA256 = calibration_data_contracts.EXPECTED_COLUMN_MANIFEST_SHA256
CLOCK_TYPES = calibration_data_contracts.CLOCK_TYPES
ALIGNMENT_METHODS = calibration_data_contracts.ALIGNMENT_METHODS
SYNC_STATES = calibration_data_contracts.SYNC_STATES
ImportLimits = calibration_data_contracts.ImportLimits
canonical_json_bytes = calibration_data_contracts.canonical_json_bytes
_error = calibration_data_contracts._error
_require_dict = calibration_data_contracts._require_dict
_require_exact_keys = calibration_data_contracts._require_exact_keys
_require_string = calibration_data_contracts._require_string
_require_id = calibration_data_contracts._require_id
_require_integer = calibration_data_contracts._require_integer
_require_number = calibration_data_contracts._require_number
_require_nullable_number = calibration_data_contracts._require_nullable_number
_require_bool = calibration_data_contracts._require_bool
_require_list = calibration_data_contracts._require_list
_validate_iso_utc = calibration_data_contracts._validate_iso_utc
_validate_hash = calibration_data_contracts._validate_hash


def _validate_schema(schema: Any, limits: ImportLimits) -> None:
    value = _require_dict(schema, "schema")
    _require_exact_keys(
        value,
        {
            "contract_id",
            "schema_version",
            "schema_sha256",
            "column_manifest_id",
            "column_manifest_sha256",
        },
        set(),
        "schema",
    )
    if _require_string(value["contract_id"], "schema.contract_id", limits) != CONTRACT_ID:
        raise _error("schema.contract_id", f"must equal {CONTRACT_ID}")
    if _require_integer(value["schema_version"], "schema.schema_version") != SCHEMA_VERSION:
        raise _error("schema.schema_version", f"must equal {SCHEMA_VERSION}")
    schema_sha256 = _validate_hash(value["schema_sha256"], "schema.schema_sha256")
    if schema_sha256 != EXPECTED_SCHEMA_SHA256:
        raise _error("schema.schema_sha256", "does not match the pinned v1 schema")
    if (
        _require_string(value["column_manifest_id"], "schema.column_manifest_id", limits)
        != COLUMN_MANIFEST_ID
    ):
        raise _error("schema.column_manifest_id", f"must equal {COLUMN_MANIFEST_ID}")
    column_manifest_sha256 = _validate_hash(
        value["column_manifest_sha256"], "schema.column_manifest_sha256"
    )
    if column_manifest_sha256 != EXPECTED_COLUMN_MANIFEST_SHA256:
        raise _error(
            "schema.column_manifest_sha256",
            "does not match the pinned v1 column manifest",
        )


def _validate_register_value(value: Any, path: str, limits: ImportLimits) -> None:
    if isinstance(value, bool) or value is None:
        return
    if isinstance(value, int):
        return
    if isinstance(value, float):
        _require_number(value, path)
        return
    if isinstance(value, str):
        _require_string(value, path, limits)
        return
    raise _error(path, "must be null, boolean, integer, finite number, or string")


def _validate_robot(robot: Any, limits: ImportLimits) -> None:
    value = _require_dict(robot, "robot")
    _require_exact_keys(
        value,
        {
            "robot_id",
            "hardware_revision",
            "firmware_version",
            "register_configuration",
            "register_configuration_sha256",
        },
        {"serial_number"},
        "robot",
    )
    _require_id(value["robot_id"], "robot.robot_id", limits)
    _require_string(value["hardware_revision"], "robot.hardware_revision", limits)
    _require_string(value["firmware_version"], "robot.firmware_version", limits)
    if "serial_number" in value and value["serial_number"] is not None:
        _require_string(value["serial_number"], "robot.serial_number", limits)
    registers = _require_dict(value["register_configuration"], "robot.register_configuration")
    if len(registers) > limits.maximum_register_entries:
        raise _error("robot.register_configuration", "contains too many entries")
    for key, entry in registers.items():
        _require_id(key, f"robot.register_configuration key {key!r}", limits)
        _validate_register_value(entry, f"robot.register_configuration.{key}", limits)
    expected = _validate_hash(
        value["register_configuration_sha256"],
        "robot.register_configuration_sha256",
    )
    actual = hashlib.sha256(canonical_json_bytes(registers)).hexdigest()
    if expected != actual:
        raise _error("robot.register_configuration_sha256", "does not match register_configuration")


def _validate_environment(environment: Any, limits: ImportLimits) -> None:
    value = _require_dict(environment, "environment")
    _require_exact_keys(
        value,
        set(),
        {"ambient_temperature_c", "relative_humidity_percent", "pressure_kpa", "notes"},
        "environment",
    )
    if "ambient_temperature_c" in value:
        _require_nullable_number(
            value["ambient_temperature_c"],
            "environment.ambient_temperature_c",
            minimum=-100.0,
            maximum=200.0,
        )
    if "relative_humidity_percent" in value:
        _require_nullable_number(
            value["relative_humidity_percent"],
            "environment.relative_humidity_percent",
            minimum=0.0,
            maximum=100.0,
        )
    if "pressure_kpa" in value:
        _require_nullable_number(
            value["pressure_kpa"],
            "environment.pressure_kpa",
            minimum=0.0,
            maximum=200.0,
        )
    if "notes" in value and value["notes"] is not None:
        _require_string(value["notes"], "environment.notes", limits)


def _validate_capture(capture: Any, clock_ids: set[str], limits: ImportLimits) -> tuple[str, str]:
    value = _require_dict(capture, "capture")
    _require_exact_keys(
        value,
        {"tool", "tool_version", "primary_clock_id", "synchronization_state"},
        {"operator_notes"},
        "capture",
    )
    _require_id(value["tool"], "capture.tool", limits)
    _require_string(value["tool_version"], "capture.tool_version", limits)
    primary = _require_id(value["primary_clock_id"], "capture.primary_clock_id", limits)
    if primary not in clock_ids:
        raise _error("capture.primary_clock_id", "does not identify a declared clock")
    sync_state = _require_string(
        value["synchronization_state"], "capture.synchronization_state", limits
    )
    if sync_state not in SYNC_STATES:
        raise _error("capture.synchronization_state", f"must be one of {sorted(SYNC_STATES)}")
    if "operator_notes" in value and value["operator_notes"] is not None:
        _require_string(value["operator_notes"], "capture.operator_notes", limits)
    return primary, sync_state


def _validate_clocks(clocks: Any, limits: ImportLimits) -> set[str]:
    values = _require_list(clocks, "clocks")
    if len(values) > limits.maximum_clocks:
        raise _error("clocks", "contains too many clocks")
    if not values:
        raise _error("clocks", "must contain at least one clock")
    clock_ids: set[str] = set()
    for index, raw in enumerate(values):
        path = f"clocks[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {"clock_id", "clock_type", "tick_unit", "epoch_description", "source"},
            {"nominal_hz"},
            path,
        )
        clock_id = _require_id(value["clock_id"], f"{path}.clock_id", limits)
        if clock_id in clock_ids:
            raise _error(f"{path}.clock_id", "is duplicated")
        clock_ids.add(clock_id)
        clock_type = _require_string(value["clock_type"], f"{path}.clock_type", limits)
        if clock_type not in CLOCK_TYPES:
            raise _error(f"{path}.clock_type", f"must be one of {sorted(CLOCK_TYPES)}")
        if value["tick_unit"] != "nanosecond":
            raise _error(f"{path}.tick_unit", "must equal nanosecond")
        _require_string(value["epoch_description"], f"{path}.epoch_description", limits)
        _require_string(value["source"], f"{path}.source", limits)
        if "nominal_hz" in value and value["nominal_hz"] is not None:
            _require_number(
                value["nominal_hz"], f"{path}.nominal_hz", minimum=0.000001, maximum=1e12
            )
    return clock_ids


def _validate_alignments(
    alignments: Any,
    clock_ids: set[str],
    primary_clock_id: str,
    limits: ImportLimits,
) -> tuple[dict[str, dict[str, Any]], str]:
    values = _require_list(alignments, "clock_alignments")
    if len(values) > limits.maximum_clock_alignments:
        raise _error("clock_alignments", "contains too many alignments")
    by_source: dict[str, dict[str, Any]] = {}
    synchronized_count = 0
    unsynchronized_count = 0
    for index, raw in enumerate(values):
        path = f"clock_alignments[{index}]"
        value = _require_dict(raw, path)
        _require_exact_keys(
            value,
            {
                "from_clock_id",
                "to_clock_id",
                "offset_ns",
                "uncertainty_ns",
                "method",
                "sample_count",
                "synchronized",
            },
            set(),
            path,
        )
        source = _require_id(value["from_clock_id"], f"{path}.from_clock_id", limits)
        target = _require_id(value["to_clock_id"], f"{path}.to_clock_id", limits)
        if source not in clock_ids or target not in clock_ids:
            raise _error(path, "references an undeclared clock")
        if source == target:
            raise _error(path, "cannot align a clock to itself")
        if target != primary_clock_id:
            raise _error(f"{path}.to_clock_id", "must target the primary clock")
        if source in by_source:
            raise _error(f"{path}.from_clock_id", "has more than one alignment")
        _require_integer(value["offset_ns"], f"{path}.offset_ns", minimum=-(10**18), maximum=10**18)
        uncertainty = _require_integer(
            value["uncertainty_ns"],
            f"{path}.uncertainty_ns",
            minimum=0,
            maximum=10**18,
        )
        method = _require_string(value["method"], f"{path}.method", limits)
        if method not in ALIGNMENT_METHODS:
            raise _error(f"{path}.method", f"must be one of {sorted(ALIGNMENT_METHODS)}")
        _require_integer(value["sample_count"], f"{path}.sample_count", minimum=0, maximum=10**9)
        synchronized = _require_bool(value["synchronized"], f"{path}.synchronized")
        if method == "unsynchronized" and synchronized:
            raise _error(path, "unsynchronized method cannot claim synchronization")
        if not synchronized and uncertainty == 0:
            raise _error(path, "unsynchronized alignment must report non-zero uncertainty")
        if synchronized:
            synchronized_count += 1
        else:
            unsynchronized_count += 1
        by_source[source] = value
    non_primary = clock_ids - {primary_clock_id}
    missing = non_primary - by_source.keys()
    if missing:
        raise _error(
            "clock_alignments", f"missing alignment to primary clock for {sorted(missing)}"
        )
    if unsynchronized_count and synchronized_count:
        derived = "partially_synchronized"
    elif unsynchronized_count:
        derived = "unsynchronized"
    else:
        derived = "synchronized"
    return by_source, derived
