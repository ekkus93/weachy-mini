"""Static integrity tests for the real MuJoCo feasibility fixtures."""

from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALID_MODEL = ROOT / "native/reachy_sim/tests/fixtures/closed_loop_probe.xml"
MALFORMED_MODEL = ROOT / "native/reachy_sim/tests/fixtures/malformed_probe.xml"


class ProbeFixtureTests(unittest.TestCase):
    """Ensure the committed probe fixtures express the intended gate."""

    def test_valid_model_has_fixed_timestep_and_loop_closure(self) -> None:
        root = ET.parse(VALID_MODEL).getroot()
        option = root.find("option")
        self.assertIsNotNone(option)
        self.assertEqual("0.002", option.attrib.get("timestep"))
        connections = root.findall("./equality/connect")
        self.assertEqual(1, len(connections))
        self.assertEqual("left_tip", connections[0].attrib.get("site1"))
        self.assertEqual("right_tip", connections[0].attrib.get("site2"))

    def test_malformed_model_is_not_well_formed_xml(self) -> None:
        with self.assertRaises(ET.ParseError):
            ET.parse(MALFORMED_MODEL)


if __name__ == "__main__":
    unittest.main()
