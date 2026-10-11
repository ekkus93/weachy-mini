#!/usr/bin/env python3
"""Deterministic fail-closed RMA-184 representative-device classification tests."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SELECTOR = ROOT / "scripts/rma184_select_performance_class.py"
SPEC = importlib.util.spec_from_file_location("rma184_selector", SELECTOR)
assert SPEC is not None and SPEC.loader is not None
selector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(selector)


class Rma184ClassSelectorTests(unittest.TestCase):
    def test_registered_classes_are_exact(self) -> None:
        for index, name in enumerate(("low", "mid", "high")):
            with self.subTest(performance_class=name):
                matrix = {"representative_devices": [{"model": "device", "performance_class": name}]}
                self.assertEqual(selector.resolve(matrix, "device"), index)

    def test_missing_or_ambiguous_model_fails_closed(self) -> None:
        matrix = {"representative_devices": [
            {"model": "phone", "performance_class": "mid"},
            {"model": "phone", "performance_class": "high"},
        ]}
        for model in ("phone", "unregistered"):
            with self.subTest(model=model), self.assertRaises(ValueError):
                selector.resolve(matrix, model)

    def test_unrecognized_class_fails_closed(self) -> None:
        matrix = {"representative_devices": [{"model": "phone", "performance_class": "ultra"}]}
        with self.assertRaises(ValueError):
            selector.resolve(matrix, "phone")

    def test_malformed_matrix_fails_with_actionable_validation(self) -> None:
        for matrix in (
            None,
            [],
            {},
            {"representative_devices": None},
            {"representative_devices": "phone"},
            {"representative_devices": [None]},
            {"representative_devices": ["phone"]},
        ):
            with self.subTest(matrix=matrix), self.assertRaises(ValueError):
                selector.resolve(matrix, "phone")

    def test_cli_does_not_print_a_class_for_malformed_input(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "matrix.json"
            path.write_text(json.dumps({"representative_devices": [None]}), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(SELECTOR), "--matrix", str(path), "--model", "phone"],
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertEqual(result.returncode, 2)
            self.assertEqual(result.stdout, "")
            self.assertIn("representative_devices entries must be JSON objects", result.stderr)
            self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
