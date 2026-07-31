from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/generate_reachy_power_thermal_baseline.py"
PROFILE = ROOT / "models/reachy-mini/power-thermal-baseline.json"
ELECTRICAL = ROOT / "models/reachy-mini/electrical-controller-baseline.json"
OUTPUT = ROOT / "native/reachy_sim/src/reachy_power_thermal_baseline.generated.hpp"
SPEC = importlib.util.spec_from_file_location("generator", SCRIPT)
GEN = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(GEN)


class PowerThermalBaselineTests(unittest.TestCase):
    def setUp(self) -> None:
        self.profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        self.electrical = json.loads(ELECTRICAL.read_text(encoding="utf-8"))

    def validate(self, profile: dict) -> None:
        GEN.validate(profile, self.electrical)

    def test_committed_header_is_exactly_regenerated(self) -> None:
        self.validate(self.profile)
        self.assertEqual(OUTPUT.read_text(encoding="utf-8"), GEN.render(self.profile))

    def test_calibrated_claim_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["shared_supply"]["overall_evidence_class"] = "calibrated"
        with self.assertRaisesRegex(ValueError, "calibrated"):
            self.validate(profile)

    def test_missing_evidence_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["thermal_baselines"][0]["winding_resistance_ohms"]["evidence_id"] = ""
        with self.assertRaisesRegex(ValueError, "missing evidence"):
            self.validate(profile)

    def test_temperature_order_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["thermal_baselines"][0]["recovery_temperature_celsius"]["value"] = 66.0
        with self.assertRaisesRegex(ValueError, "temperature order"):
            self.validate(profile)

    def test_cross_role_parameter_copy_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        for key in GEN.EXPECTED_UNITS["thermal"]:
            profile["thermal_baselines"][2][key] = copy.deepcopy(profile["thermal_baselines"][1][key])
        with self.assertRaisesRegex(ValueError, "cross-role parameter copy"):
            self.validate(profile)

    def test_cross_role_binding_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["actuator_bindings"][1]["baseline_id"] = profile["thermal_baselines"][2]["id"]
        with self.assertRaisesRegex(ValueError, "cross-role"):
            self.validate(profile)

    def test_current_budget_must_be_shared(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["shared_supply"]["current_limit_amperes"]["value"] = 20.0
        with self.assertRaisesRegex(ValueError, "current budget"):
            self.validate(profile)

    def test_stale_header_is_detected_by_cli(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            fake_root = Path(directory)
            (fake_root / "scripts").mkdir()
            (fake_root / "models/reachy-mini").mkdir(parents=True)
            (fake_root / "native/reachy_sim/src").mkdir(parents=True)
            copied = fake_root / "scripts/generate_reachy_power_thermal_baseline.py"
            copied.write_text(SCRIPT.read_text(encoding="utf-8"), encoding="utf-8")
            (fake_root / "models/reachy-mini/power-thermal-baseline.json").write_text(PROFILE.read_text(encoding="utf-8"), encoding="utf-8")
            (fake_root / "models/reachy-mini/electrical-controller-baseline.json").write_text(ELECTRICAL.read_text(encoding="utf-8"), encoding="utf-8")
            (fake_root / "native/reachy_sim/src/reachy_power_thermal_baseline.generated.hpp").write_text("stale\n", encoding="utf-8")
            result = subprocess.run(["python3", str(copied), "--check"], text=True, capture_output=True, check=False)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("stale", result.stderr + result.stdout)


if __name__ == "__main__":
    unittest.main()
