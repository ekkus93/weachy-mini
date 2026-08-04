"""Focused tests for the RMA-065 collision and hard-stop generator."""

from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/generate_reachy_collision_model.py"
PROFILE = ROOT / "models/reachy-mini/collision-hard-stop-baseline.json"
spec = importlib.util.spec_from_file_location("rma065_generator", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load RMA-065 generator")
generator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = generator
spec.loader.exec_module(generator)


def fixture_xml() -> bytes:
    arms = ["dc15_a01_horn_dummy"] + [f"dc15_a01_horn_dummy_{i}" for i in range(2, 9)]
    rods = ["stewart_link_rod"] + [f"stewart_link_rod_{i}" for i in range(2, 7)]
    body_xml = [
        '<body name="body_foot_3dprint"><geom name="visual_base" type="sphere" size="0.01" contype="0" conaffinity="0"/></body>',
        '<body name="body_down_3dprint"><joint name="yaw_body" type="hinge" range="-2.8 2.8"/><geom name="source_shell" type="sphere" size="0.02" class="collision"/></body>',
    ]
    for index in range(6):
        arm = arms[index]
        rod = rods[index]
        joint = f"stewart_{index + 1}"
        if index == 5:
            endpoint = (
                '<body name="xl_330" pos="-0.085 0 0">'
                '<joint name="passive_7" type="ball"/>'
                '<geom name="source_head" type="sphere" size="0.02" class="collision"/>'
                "</body>"
            )
        else:
            endpoint = f'<site name="closing_{index + 1}_1" pos="0.085 0 0"/>'
        body_xml.append(
            f'<body name="{arm}"><joint name="{joint}" type="hinge" range="-1 1"/>'
            f'<body name="{rod}" pos="0.04 0 0.007">'
            f'<joint name="passive_{index + 1}" type="ball"/>'
            f"{endpoint}</body></body>"
        )
    body_xml.append(
        '<body name="dc15_a01_horn_dummy_7"><joint name="right_antenna" type="hinge"/></body>'
    )
    body_xml.append(
        '<body name="dc15_a01_horn_dummy_8"><joint name="left_antenna" type="hinge"/></body>'
    )
    actuators = (
        ["yaw_body"] + [f"stewart_{i}" for i in range(1, 7)] + ["right_antenna", "left_antenna"]
    )
    actuator_xml = "".join(
        f'<position name="{name}" joint="{name}" kp="1" inheritrange="1"/>' for name in actuators
    )
    return (
        '<?xml version="1.0"?><mujoco model="fixture"><compiler angle="radian" autolimits="true"/>'
        '<default><default class="collision"><geom contype="1" conaffinity="1"/></default></default>'
        f"<worldbody>{''.join(body_xml)}</worldbody><actuator>{actuator_xml}</actuator></mujoco>\n"
    ).encode()


class CollisionGeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        self.fixture = fixture_xml()
        self.profile["source"]["model_sha256"] = hashlib.sha256(self.fixture).hexdigest()

    def write_case(
        self, directory: Path, profile: dict | None = None, fixture: bytes | None = None
    ) -> tuple[Path, Path]:
        source = directory / "source.xml"
        profile_path = directory / "profile.json"
        source.write_bytes(self.fixture if fixture is None else fixture)
        profile_path.write_text(
            json.dumps(self.profile if profile is None else profile, indent=2) + "\n",
            encoding="utf-8",
        )
        return source, profile_path

    def test_generation_adds_exact_shapes_masks_and_hard_stops(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile = self.write_case(temp)
            output = temp / "generated.xml"
            metadata = temp / "metadata.json"
            result = generator.generate(profile, source, output, metadata, check=False)
            root = ET.parse(output).getroot()
            generated = [
                geom
                for geom in root.findall(".//geom")
                if (geom.get("name") or "").startswith("rma065_")
            ]
            self.assertEqual(len(generated), len(self.profile["shapes"]))
            self.assertEqual(
                {geom.get("name") for geom in generated},
                {shape["name"] for shape in self.profile["shapes"]},
            )
            source_shell = root.find(".//geom[@name='source_shell']")
            self.assertIsNotNone(source_shell)
            assert source_shell is not None
            self.assertEqual(source_shell.get("contype"), "1")
            self.assertEqual(source_shell.get("conaffinity"), "6")
            right = root.find(".//joint[@name='right_antenna']")
            self.assertIsNotNone(right)
            assert right is not None
            self.assertEqual(
                [float(value) for value in right.get("range", "").split()], [-3.12, 3.12]
            )
            actuator = root.find(".//position[@name='right_antenna']")
            self.assertIsNotNone(actuator)
            assert actuator is not None
            self.assertEqual(
                [float(value) for value in actuator.get("ctrlrange", "").split()], [-3.05, 3.05]
            )
            self.assertIsNone(actuator.get("inheritrange"))
            arm = root.find(".//geom[@name='rma065_arm_1']")
            rod = root.find(".//geom[@name='rma065_rod_1']")
            rod_six = root.find(".//geom[@name='rma065_rod_6']")
            self.assertIsNotNone(arm)
            self.assertIsNotNone(rod)
            self.assertIsNotNone(rod_six)
            assert arm is not None and rod is not None and rod_six is not None
            arm_fromto = [float(value) for value in arm.get("fromto", "").split()]
            rod_fromto = [float(value) for value in rod.get("fromto", "").split()]
            rod_six_fromto = [float(value) for value in rod_six.get("fromto", "").split()]
            self.assertEqual(len(arm_fromto), 6)
            self.assertEqual(len(rod_fromto), 6)
            self.assertGreater(arm_fromto[3], arm_fromto[0])
            self.assertGreater(rod_fromto[3], rod_fromto[0])
            self.assertAlmostEqual(rod_fromto[1], 0.0)
            self.assertAlmostEqual(rod_fromto[2], 0.0)
            self.assertAlmostEqual(rod_fromto[4], 0.0)
            self.assertAlmostEqual(rod_fromto[5], 0.0)
            self.assertLess(rod_six_fromto[3], rod_six_fromto[0])
            self.assertAlmostEqual(rod_six_fromto[1], 0.0)
            self.assertAlmostEqual(rod_six_fromto[2], 0.0)
            numerics = {item.get("name") for item in root.findall(".//custom/numeric")}
            self.assertIn("rma065_contact_overload_newtons", numerics)
            self.assertFalse(result["calibrated"])
            self.assertEqual(len(result["hard_stops"]), 9)

    def test_check_detects_stale_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile = self.write_case(temp)
            output = temp / "generated.xml"
            metadata = temp / "metadata.json"
            generator.generate(profile, source, output, metadata, check=False)
            generator.generate(profile, source, output, metadata, check=True)
            output.write_text("stale\n", encoding="utf-8")
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(profile, source, output, metadata, check=True)

    def test_calibrated_claim_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["fidelity"]["calibrated"] = True
        with self.assertRaises(generator.CollisionProfileError):
            generator.validate_profile(profile)

    def test_missing_shape_evidence_is_rejected(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["shapes"][0]["evidence_id"] = ""
        with self.assertRaises(generator.CollisionProfileError):
            generator.validate_profile(profile)

    def test_missing_required_body_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile = self.write_case(
                temp,
                fixture=self.fixture.replace(b'name="stewart_link_rod_6"', b'name="missing_rod"'),
            )
            updated = json.loads(profile.read_text())
            updated["source"]["model_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
            profile.write_text(json.dumps(updated), encoding="utf-8")
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(
                    profile, source, temp / "out.xml", temp / "meta.json", check=False
                )

    def test_source_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile = self.write_case(temp)
            source.write_bytes(source.read_bytes() + b"<!-- drift -->")
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(
                    profile, source, temp / "out.xml", temp / "meta.json", check=False
                )

    def test_soft_limit_must_be_inside_hard_limit(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["hard_stops"][-1]["soft_range_radians"] = [-3.2, 3.05]
        with self.assertRaises(generator.CollisionProfileError):
            generator.validate_profile(profile)

    def test_moving_masks_cannot_self_collide(self) -> None:
        profile = copy.deepcopy(self.profile)
        profile["collision_masks"]["moving"]["conaffinity"] = 2
        with self.assertRaises(generator.CollisionProfileError):
            generator.validate_profile(profile)

    def test_segment_child_must_be_a_direct_body_child(self) -> None:
        profile = copy.deepcopy(self.profile)
        arm = next(shape for shape in profile["shapes"] if shape["name"] == "rma065_arm_1")
        arm["segment_child_body"] = "missing_endpoint"
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile_path = self.write_case(temp, profile=profile)
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(
                    profile_path,
                    source,
                    temp / "out.xml",
                    temp / "meta.json",
                    check=False,
                )

    def test_segment_site_must_be_directly_owned_by_the_body(self) -> None:
        profile = copy.deepcopy(self.profile)
        rod = next(shape for shape in profile["shapes"] if shape["name"] == "rma065_rod_1")
        rod["segment_site"] = "missing_endpoint"
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile_path = self.write_case(temp, profile=profile)
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(
                    profile_path,
                    source,
                    temp / "out.xml",
                    temp / "meta.json",
                    check=False,
                )

    def test_segment_insets_cannot_consume_the_segment(self) -> None:
        profile = copy.deepcopy(self.profile)
        arm = next(shape for shape in profile["shapes"] if shape["name"] == "rma065_arm_1")
        arm["segment_start_inset_metres"] = 0.04
        arm["segment_end_inset_metres"] = 0.01
        with tempfile.TemporaryDirectory() as temp_text:
            temp = Path(temp_text)
            source, profile_path = self.write_case(temp, profile=profile)
            with self.assertRaises(generator.CollisionProfileError):
                generator.generate(
                    profile_path,
                    source,
                    temp / "out.xml",
                    temp / "meta.json",
                    check=False,
                )


class CollisionRuntimeRegressionTests(unittest.TestCase):
    def test_contact_force_uses_writable_numpy_float64_buffers(self):
        for relative in (
            "scripts/audit_reachy_collision_model.py",
            "scripts/run_reachy_collision_hard_stop_validation.py",
        ):
            source = (ROOT / relative).read_text(encoding="utf-8")
            if "mj_contactForce" not in source:
                continue
            self.assertIn("import numpy as np", source)
            self.assertIn("np.zeros(6, dtype=np.float64)", source)
            self.assertNotIn("[0.0] * 6", source)


if __name__ == "__main__":
    unittest.main()


class CollisionFixtureAssetRegressionTests(unittest.TestCase):
    def test_contact_fixture_workspace_links_model_assets(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        self.assertIn(
            '(args.enhanced_model.parent / "assets").resolve()',
            source,
        )
        self.assertIn(
            '(temp / "assets").symlink_to(',
            source,
        )


class CollisionInternalFixtureRegressionTests(unittest.TestCase):
    def test_internal_shell_probe_is_world_attached(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        self.assertIn('worldbody = root.find("worldbody")', source)
        self.assertIn(
            '"rma065_fixture_internal_shell",\n        shell_point,\n        1,\n        6,',
            source,
        )
        function = source[
            source.index("def internal_contact_fixture") : source.index(
                "\ndef external_contact_fixture"
            )
        ]
        self.assertNotIn("shell_id = object_id(", function)


class CollisionInternalFixtureIsolationRegressionTests(unittest.TestCase):
    def test_internal_fixture_disables_preexisting_collision_geoms(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        function = source[
            source.index("def internal_contact_fixture") : source.index(
                "\ndef external_contact_fixture"
            )
        ]
        self.assertIn(
            'for geom in root.findall(".//geom"):',
            function,
        )
        self.assertIn('geom.set("contype", "0")', function)
        self.assertIn('geom.set("conaffinity", "0")', function)


class CollisionInternalPenetrationRegressionTests(unittest.TestCase):
    def test_fixture_penetration_is_derived_below_model_limit(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        self.assertIn(
            "target_penetration = maximum_penetration / 16.0",
            source,
        )
        self.assertIn(
            "centre_separation = 2.0 * probe_radius - target_penetration",
            source,
        )
        self.assertNotIn("direction = [0.012, 0.0, 0.0]", source)


class CollisionPenetrationParsingRegressionTests(unittest.TestCase):
    def test_penetration_limit_is_parsed_locally_and_strictly(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        self.assertIn(
            "penetration_parts = penetration_data.split()",
            source,
        )
        self.assertIn(
            '0.0 < maximum_penetration < float("inf")',
            source,
        )
        self.assertNotIn(
            "parse_vector(penetration_data",
            source,
        )


class CollisionExternalFixtureRegressionTests(unittest.TestCase):
    def test_external_fixture_uses_isolated_controlled_probe_pair(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        function = source[
            source.index("def external_contact_fixture") : source.index(
                "\ndef ", source.index("def external_contact_fixture") + 1
            )
        ]
        self.assertIn(
            '"rma065_fixture_external_shell"',
            function,
        )
        self.assertIn(
            "target_penetration = maximum_penetration / 16.0",
            function,
        )
        self.assertIn(
            'for geom in root.findall(".//geom"):',
            function,
        )
        self.assertNotIn('"size": "0.025"', function)


class CollisionValidationReportStatusTests(unittest.TestCase):
    def test_success_status_is_emitted_with_validation_contract(self):
        source = Path("scripts/run_reachy_collision_hard_stop_validation.py").read_text(
            encoding="utf-8"
        )
        report_start = source.index("    report = {")
        report_end = source.index("    args.output.parent", report_start)
        report_source = source[report_start:report_end]
        self.assertIn('"status": "ok"', report_source)
        self.assertIn(
            '"contract": "rma065_collision_hard_stop_validation_v1"',
            report_source,
        )
