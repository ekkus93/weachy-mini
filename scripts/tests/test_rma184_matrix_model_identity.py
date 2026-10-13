"""RMA-184: reject ambiguous physical-device model maps before Android qualification."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts import validate_rma184_device_matrix as validator


class MatrixModelIdentityContracts(unittest.TestCase):
    def setUp(self) -> None:
        self.source = json.loads(validator.MATRIX.read_text(encoding="utf-8"))

    def check_matrix(self, data: dict) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "device-matrix.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with patch.object(validator, "MATRIX", path):
                validator.main()

    def test_authoritative_matrix_is_unambiguous(self) -> None:
        self.check_matrix(self.source)

    def test_duplicate_device_model_is_rejected_before_soak(self) -> None:
        entries = self.source["representative_devices"]
        entries[1]["model"] = entries[0]["model"]
        with self.assertRaisesRegex(SystemExit, "duplicate representative device model"):
            self.check_matrix(self.source)

    def test_missing_or_noncanonical_model_is_rejected(self) -> None:
        for invalid in ("", "  ", " SM-A546E", "SM-A546E ", None, 25):
            with self.subTest(invalid=invalid):
                data = json.loads(validator.MATRIX.read_text(encoding="utf-8"))
                data["representative_devices"][1]["model"] = invalid
                with self.assertRaisesRegex(
                    SystemExit, "device model must be a canonical non-empty string"
                ):
                    self.check_matrix(data)

    def test_nonobject_device_record_is_rejected(self) -> None:
        self.source["representative_devices"][1] = "not a device object"
        with self.assertRaisesRegex(SystemExit, "entries must be JSON objects"):
            self.check_matrix(self.source)


if __name__ == "__main__":
    unittest.main()
