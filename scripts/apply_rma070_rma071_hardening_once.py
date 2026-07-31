#!/usr/bin/env python3
"""One-shot source patch for RMA-070/RMA-071 fail-closed hardening."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one replacement target, found {count}")
    target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


def insert_before(path: str, marker: str, addition: str) -> None:
    replace_once(path, marker, addition + marker)


replace_once(
    "scripts/calibration_data.py",
    'HASH_ALGORITHM = "sha256"\n',
    'HASH_ALGORITHM = "sha256"\n'
    'EXPECTED_SCHEMA_SHA256 = "5268d353bf98f26df840bf3950e02e0dfdd420b575f1595c26c4fcc602548a28"\n'
    'EXPECTED_COLUMN_MANIFEST_SHA256 = "f3f851734455f2e79408825d58ff641a8469e0dbf1f5ae41c4866b5d6a4f4dc9"\n',
)
replace_once(
    "scripts/calibration_data.py",
    "    maximum_streams: int = 64\n    maximum_samples_per_stream: int = 1_000_000\n",
    "    maximum_streams: int = 64\n"
    "    maximum_clocks: int = 64\n"
    "    maximum_clock_alignments: int = 63\n"
    "    maximum_samples_per_stream: int = 1_000_000\n",
)
replace_once(
    "scripts/calibration_data.py",
    '    _validate_hash(value["schema_sha256"], "schema.schema_sha256")\n',
    '    schema_sha256 = _validate_hash(value["schema_sha256"], "schema.schema_sha256")\n'
    '    if schema_sha256 != EXPECTED_SCHEMA_SHA256:\n'
    '        raise _error("schema.schema_sha256", "does not match the pinned v1 schema")\n',
)
replace_once(
    "scripts/calibration_data.py",
    '    _validate_hash(value["column_manifest_sha256"], "schema.column_manifest_sha256")\n',
    '    column_manifest_sha256 = _validate_hash(\n'
    '        value["column_manifest_sha256"], "schema.column_manifest_sha256"\n'
    '    )\n'
    '    if column_manifest_sha256 != EXPECTED_COLUMN_MANIFEST_SHA256:\n'
    '        raise _error(\n'
    '            "schema.column_manifest_sha256",\n'
    '            "does not match the pinned v1 column manifest",\n'
    '        )\n',
)
replace_once(
    "scripts/calibration_data.py",
    "def _validate_environment(environment: Any) -> None:\n",
    "def _validate_environment(environment: Any, limits: ImportLimits) -> None:\n",
)
replace_once(
    "scripts/calibration_data.py",
    '    if "notes" in value and value["notes"] is not None and not isinstance(value["notes"], str):\n'
    '        raise _error("environment.notes", "must be a string or null")\n',
    '    if "notes" in value and value["notes"] is not None:\n'
    '        _require_string(value["notes"], "environment.notes", limits)\n',
)
replace_once(
    "scripts/calibration_data.py",
    '    values = _require_list(clocks, "clocks")\n'
    '    if not values:\n',
    '    values = _require_list(clocks, "clocks")\n'
    '    if len(values) > limits.maximum_clocks:\n'
    '        raise _error("clocks", "contains too many clocks")\n'
    '    if not values:\n',
)
replace_once(
    "scripts/calibration_data.py",
    '    values = _require_list(alignments, "clock_alignments")\n'
    '    by_source: dict[str, dict[str, Any]] = {}\n',
    '    values = _require_list(alignments, "clock_alignments")\n'
    '    if len(values) > limits.maximum_clock_alignments:\n'
    '        raise _error("clock_alignments", "contains too many alignments")\n'
    '    by_source: dict[str, dict[str, Any]] = {}\n',
)
replace_once(
    "scripts/calibration_data.py",
    '    _validate_environment(value["environment"])\n',
    '    _validate_environment(value["environment"], limits)\n',
)
replace_once(
    "scripts/calibration_data.py",
    '''def _reject_json_constant(value: str) -> None:\n    raise CalibrationValidationError(f"JSON contains non-finite constant {value}")\n\n\ndef load_json_file(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> Any:\n    """Load bounded strict JSON, rejecting NaN and Infinity."""\n\n    size = path.stat().st_size\n    if size > limits.maximum_file_bytes:\n        raise _error(str(path), f"file size {size} exceeds {limits.maximum_file_bytes}")\n    return json.loads(path.read_text(encoding="utf-8"), parse_constant=_reject_json_constant)\n''',
    '''def load_json_text(text: str, *, source: str = "JSON") -> Any:\n    """Load strict JSON text, rejecting duplicate keys and non-finite constants."""\n\n    def reject_constant(value: str) -> None:\n        raise CalibrationValidationError(f"{source}: contains non-finite constant {value}")\n\n    def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:\n        result: dict[str, Any] = {}\n        for key, value in pairs:\n            if key in result:\n                raise CalibrationValidationError(\n                    f"{source}: JSON object contains duplicate key {key!r}"\n                )\n            result[key] = value\n        return result\n\n    try:\n        return json.loads(\n            text,\n            parse_constant=reject_constant,\n            object_pairs_hook=reject_duplicate_keys,\n        )\n    except json.JSONDecodeError as exc:\n        raise CalibrationValidationError(\n            f"{source}: invalid JSON at line {exc.lineno} column {exc.colno}: {exc.msg}"\n        ) from exc\n\n\ndef load_json_file(path: Path, *, limits: ImportLimits = DEFAULT_LIMITS) -> Any:\n    """Load bounded strict UTF-8 JSON without a stat/read race."""\n\n    data = path.read_bytes()\n    size = len(data)\n    if size > limits.maximum_file_bytes:\n        raise _error(str(path), f"file size {size} exceeds {limits.maximum_file_bytes}")\n    try:\n        text = data.decode("utf-8")\n    except UnicodeDecodeError as exc:\n        raise CalibrationValidationError(f"{path}: is not valid UTF-8") from exc\n    return load_json_text(text, source=str(path))\n''',
)
replace_once(
    "scripts/calibration_data.py",
    '''    return {\n        "contract_id": CONTRACT_ID,\n        "schema_version": SCHEMA_VERSION,\n        "schema_sha256": hashlib.sha256(schema_path.read_bytes()).hexdigest(),\n        "column_manifest_id": COLUMN_MANIFEST_ID,\n        "column_manifest_sha256": hashlib.sha256(columns_path.read_bytes()).hexdigest(),\n    }\n''',
    '''    descriptor = {\n        "contract_id": CONTRACT_ID,\n        "schema_version": SCHEMA_VERSION,\n        "schema_sha256": hashlib.sha256(schema_path.read_bytes()).hexdigest(),\n        "column_manifest_id": COLUMN_MANIFEST_ID,\n        "column_manifest_sha256": hashlib.sha256(columns_path.read_bytes()).hexdigest(),\n    }\n    if descriptor["schema_sha256"] != EXPECTED_SCHEMA_SHA256:\n        raise CalibrationValidationError(\n            "calibration-dataset-v1.schema.json does not match the pinned v1 hash"\n        )\n    if descriptor["column_manifest_sha256"] != EXPECTED_COLUMN_MANIFEST_SHA256:\n        raise CalibrationValidationError(\n            "calibration-stream-columns-v1.json does not match the pinned v1 hash"\n        )\n    return descriptor\n''',
)

replace_once(
    "scripts/capture_reachy_calibration.py",
    '''from calibration_data import (\n    DEFAULT_LIMITS,\n    canonical_json_bytes,\n    finalize_dataset,\n    load_json_file,\n    schema_descriptor,\n    validate_dataset,\n)\n''',
    '''from calibration_data import (\n    DEFAULT_LIMITS,\n    CalibrationValidationError,\n    canonical_json_bytes,\n    finalize_dataset,\n    load_json_file,\n    load_json_text,\n    schema_descriptor,\n    validate_dataset,\n)\n''',
)
replace_once(
    "scripts/capture_reachy_calibration.py",
    '''def _strict_json_line(line: str, line_number: int) -> dict[str, Any]:\n    def reject_constant(value: str) -> None:\n        raise ValueError(f"line {line_number} contains non-finite constant {value}")\n\n    try:\n        value = json.loads(line, parse_constant=reject_constant)\n    except json.JSONDecodeError as exc:\n        raise ValueError(f"line {line_number} is invalid JSON: {exc.msg}") from exc\n    if not isinstance(value, dict):\n        raise ValueError(f"line {line_number} must contain a JSON object")\n    return value\n''',
    '''def _strict_json_line(line: str, line_number: int) -> dict[str, Any]:\n    try:\n        value = load_json_text(line, source=f"telemetry line {line_number}")\n    except CalibrationValidationError as exc:\n        raise ValueError(str(exc)) from exc\n    if not isinstance(value, dict):\n        raise ValueError(f"line {line_number} must contain a JSON object")\n    return value\n''',
)

replace_once(
    "scripts/estimate_calibration_clock_offset.py",
    ''') -> dict[str, object]:\n    if len(rows) < 3:\n''',
    ''') -> dict[str, object]:\n    if from_clock_id == to_clock_id:\n        raise ValueError("source and primary clock IDs must differ")\n    if maximum_uncertainty_ns < 0 or maximum_uncertainty_ns > 10**18:\n        raise ValueError("maximum uncertainty must be between 0 and 10^18 ns")\n    if len(rows) < 3:\n''',
)
replace_once(
    "scripts/estimate_calibration_clock_offset.py",
    '''    offsets = [target - source for target, source in rows]\n    median_offset = round(statistics.median(offsets))\n    uncertainty = max(abs(offset - median_offset) for offset in offsets)\n''',
    '''    for target, source in rows:\n        if target < 0 or source < 0 or target > 10**19 or source > 10**19:\n            raise ValueError("paired timestamps must be between 0 and 10^19 ns")\n    offsets = [target - source for target, source in rows]\n    median_offset = round(statistics.median(offsets))\n    uncertainty = max(abs(offset - median_offset) for offset in offsets)\n    if abs(median_offset) > 10**18 or uncertainty > 10**18:\n        raise ValueError("estimated alignment exceeds the v1 import bounds")\n''',
)
replace_once(
    "scripts/estimate_calibration_clock_offset.py",
    '''        expected = {"primary_timestamp_ns", "source_timestamp_ns"}\n        if set(reader.fieldnames or []) != expected:\n            raise ValueError(f"synchronization CSV columns must be exactly {sorted(expected)}")\n''',
    '''        expected = ["primary_timestamp_ns", "source_timestamp_ns"]\n        if reader.fieldnames != expected:\n            raise ValueError(f"synchronization CSV columns must be exactly {expected}")\n''',
)
replace_once(
    "scripts/estimate_calibration_clock_offset.py",
    '''    if args.maximum_uncertainty_ns < 0:\n        raise ValueError("maximum uncertainty must be non-negative")\n''',
    '''    if args.maximum_rows <= 0:\n        raise ValueError("maximum rows must be positive")\n''',
)

insert_before(
    "scripts/tests/test_calibration_data.py",
    '\n\nif __name__ == "__main__":\n',
    '''\n    def test_schema_hashes_are_pinned_during_validation(self) -> None:\n        changed = copy.deepcopy(self.fixture)\n        changed["schema"]["schema_sha256"] = "0" * 64\n        changed = calibration_data.finalize_dataset(changed)\n        with self.assertRaisesRegex(\n            calibration_data.CalibrationValidationError, "pinned v1 schema"\n        ):\n            calibration_data.validate_dataset(changed)\n\n    def test_duplicate_json_object_keys_are_rejected(self) -> None:\n        with tempfile.TemporaryDirectory() as temp_text:\n            path = Path(temp_text) / "duplicate.json"\n            path.write_text('{"value": 1, "value": 2}\\n', encoding="utf-8")\n            with self.assertRaisesRegex(\n                calibration_data.CalibrationValidationError, "duplicate key"\n            ):\n                calibration_data.load_json_file(path)\n\n    def test_environment_notes_obey_general_string_limit(self) -> None:\n        changed = copy.deepcopy(self.fixture)\n        changed["environment"]["notes"] = "12345"\n        changed = calibration_data.finalize_dataset(changed)\n        limits = calibration_data.ImportLimits(maximum_string_length=4)\n        with self.assertRaisesRegex(\n            calibration_data.CalibrationValidationError, "maximum string length"\n        ):\n            calibration_data.validate_dataset(changed, limits=limits)\n\n    def test_clock_collection_limit_is_enforced(self) -> None:\n        limits = calibration_data.ImportLimits(maximum_clocks=2)\n        with self.assertRaisesRegex(\n            calibration_data.CalibrationValidationError, "too many clocks"\n        ):\n            calibration_data.validate_dataset(self.fixture, limits=limits)\n\n    def test_schema_descriptor_rejects_unversioned_file_drift(self) -> None:\n        with tempfile.TemporaryDirectory() as temp_text:\n            root = Path(temp_text)\n            source = ROOT / "calibration/schemas"\n            (root / "calibration-dataset-v1.schema.json").write_bytes(\n                (source / "calibration-dataset-v1.schema.json").read_bytes() + b"\\n"\n            )\n            (root / "calibration-stream-columns-v1.json").write_bytes(\n                (source / "calibration-stream-columns-v1.json").read_bytes()\n            )\n            with self.assertRaisesRegex(\n                calibration_data.CalibrationValidationError, "pinned v1 hash"\n            ):\n                calibration_data.schema_descriptor(root)\n''',
)

insert_before(
    "scripts/tests/test_calibration_capture.py",
    '\n\nif __name__ == "__main__":\n',
    '''\n    def test_jsonl_duplicate_keys_are_rejected(self) -> None:\n        text = (\n            '{"stream_id":"first","stream_id":"second",'\n            '"sample_type":"joint","clock_id":"clock","sample":{}}\\n'\n        )\n        with self.assertRaisesRegex(ValueError, "duplicate key"):\n            capture.read_telemetry_jsonl(\n                io.StringIO(text),\n                maximum_records=10,\n                maximum_bytes=10_000,\n            )\n\n    def test_clock_pair_csv_header_order_and_duplicates_fail_closed(self) -> None:\n        with tempfile.TemporaryDirectory() as temp_text:\n            path = Path(temp_text) / "pairs.csv"\n            path.write_text(\n                "source_timestamp_ns,primary_timestamp_ns\\n1,2\\n3,4\\n5,6\\n",\n                encoding="utf-8",\n            )\n            with self.assertRaisesRegex(ValueError, "columns must be exactly"):\n                clock_offset.read_pairs(path, 10)\n\n    def test_clock_alignment_rejects_same_source_and_target(self) -> None:\n        with self.assertRaisesRegex(ValueError, "must differ"):\n            clock_offset.estimate_alignment(\n                [(1, 1), (2, 2), (3, 3)],\n                from_clock_id="same",\n                to_clock_id="same",\n                maximum_uncertainty_ns=10,\n                allow_unsynchronized=False,\n            )\n''',
)

replace_once(
    "docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md",
    "- exact schema and column-manifest hashes;\n",
    "- exact schema and column-manifest hashes pinned by the v1 validator;\n",
)
replace_once(
    "docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md",
    "- 64 streams;\n",
    "- 64 streams;\n- 64 clocks and 63 source-to-primary alignments;\n",
)
replace_once(
    "docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md",
    "The validator rejects unknown object members, duplicate identifiers,\n",
    "The validator rejects duplicate JSON object keys, unknown object members, duplicate identifiers,\n",
)
replace_once(
    "docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md",
    "CSV headers must match exactly. Unknown columns, missing columns, empty data,\n",
    "CSV headers, including order and uniqueness, must match exactly. Unknown columns, missing columns, empty data,\n",
)

print("RMA-070/RMA-071 hardening patch applied")
