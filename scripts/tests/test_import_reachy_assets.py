"""Tests for the deterministic Reachy asset import pipeline."""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IMPORT_SCRIPT = ROOT / "scripts" / "import_reachy_assets.py"


class ReachyAssetImportTests(unittest.TestCase):
    """Exercise provenance, topology, determinism, and visible failure behavior."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.source = self.root / "source"
        self.output = self.root / "output"
        self.lock_path = self.root / "source.lock.json"
        self.model_directory = self.source / "src/reachy_mini/descriptions/reachy_mini/mjcf"
        asset_directory = self.model_directory / "assets" / "collision/coarse"
        asset_directory.mkdir(parents=True)
        (self.source / "LICENSE").write_text("test license\n", encoding="utf-8")
        (self.model_directory / "assets" / "head.stl").write_bytes(b"head-mesh")
        (asset_directory / "body.stl").write_bytes(b"body-mesh")
        self.model_path = self.model_directory / "reachy_mini.xml"
        self.model_path.write_text(self.fixture_model(), encoding="utf-8")
        self.run_git("init", "--quiet")
        self.run_git("config", "user.email", "test@example.invalid")
        self.run_git("config", "user.name", "Reachy Import Test")
        self.commit = self.commit_source("fixture")
        self.write_lock(self.commit)

    @staticmethod
    def fixture_model() -> str:
        """Return an MJCF containing every topology category used by the map."""
        return """<?xml version="1.0"?>
<mujoco model="fixture">
  <compiler meshdir="assets"/>
  <worldbody>
    <camera name="studio_close" mode="targetbodycom" target="head"/>
    <body name="base">
      <joint name="yaw_body" type="hinge"/>
      <body name="stewart_arm">
        <joint name="stewart_1" type="hinge"/>
        <body name="rod">
          <joint name="passive_1" type="ball"/>
          <site name="closing_1_1"/>
          <body name="head">
            <site name="closing_1_2"/>
            <site name="camera_optical"/>
            <site name="head"/>
            <body>
              <site name="camera"/>
              <camera name="eye_camera" mode="fixed"/>
            </body>
            <body name="right_antenna_body">
              <joint name="right_antenna" type="hinge"/>
            </body>
            <body name="left_antenna_body">
              <joint name="left_antenna" type="hinge"/>
            </body>
          </body>
        </body>
      </body>
    </body>
  </worldbody>
  <asset>
    <mesh file="head.stl"/>
    <mesh file="collision/coarse/body.stl"/>
  </asset>
  <actuator>
    <position name="yaw_body" joint="yaw_body"/>
    <position name="stewart_1" joint="stewart_1"/>
    <position name="right_antenna" joint="right_antenna"/>
    <position name="left_antenna" joint="left_antenna"/>
  </actuator>
  <equality>
    <connect site1="closing_1_1" site2="closing_1_2"/>
  </equality>
</mujoco>
"""

    def run_git(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        """Run Git in the temporary fixture checkout."""
        return subprocess.run(
            ["git", "-C", str(self.source), *arguments],
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def commit_source(self, message: str) -> str:
        """Commit fixture changes and return the exact revision."""
        self.run_git("add", ".")
        self.run_git("commit", "--quiet", "-m", message)
        return self.run_git("rev-parse", "HEAD").stdout.strip()

    def write_lock(self, commit: str) -> None:
        """Write a source lock and model-topology contract for the fixture."""
        lock = {
            "schema_version": 1,
            "repository": "https://example.invalid/reachy_mini.git",
            "commit": commit,
            "license_file": "LICENSE",
            "model_file": ("src/reachy_mini/descriptions/reachy_mini/mjcf/reachy_mini.xml"),
            "output_subdirectory": "ReachyMini/Source",
            "asset_license": "CC-BY-NC-SA",
            "software_license": "Apache-2.0",
            "model_requirements": {
                "model_name": "fixture",
                "exact_counts": {
                    "actuators": 4,
                    "bodies": 7,
                    "cameras": 2,
                    "equalities": 1,
                    "joints": 5,
                    "named_bodies": 6,
                    "sites": 5,
                },
                "required_names": {
                    "bodies": ["base", "head"],
                    "joints": [
                        "yaw_body",
                        "stewart_1",
                        "passive_1",
                        "right_antenna",
                        "left_antenna",
                    ],
                    "actuators": [
                        "yaw_body",
                        "stewart_1",
                        "right_antenna",
                        "left_antenna",
                    ],
                    "sites": [
                        "closing_1_1",
                        "closing_1_2",
                        "camera_optical",
                        "camera",
                        "head",
                    ],
                    "cameras": ["studio_close", "eye_camera"],
                },
                "required_joint_types": {
                    "yaw_body": "hinge",
                    "stewart_1": "hinge",
                    "passive_1": "ball",
                    "right_antenna": "hinge",
                    "left_antenna": "hinge",
                },
                "required_actuator_joints": {
                    "yaw_body": "yaw_body",
                    "stewart_1": "stewart_1",
                    "right_antenna": "right_antenna",
                    "left_antenna": "left_antenna",
                },
                "required_equalities": [
                    {
                        "type": "connect",
                        "attributes": {
                            "site1": "closing_1_1",
                            "site2": "closing_1_2",
                        },
                    }
                ],
                "required_camera_attributes": {
                    "studio_close": {
                        "mode": "targetbodycom",
                        "target": "head",
                    },
                    "eye_camera": {"mode": "fixed"},
                },
            },
        }
        self.lock_path.write_text(
            json.dumps(lock, indent=2) + "\n",
            encoding="utf-8",
        )

    def run_import(self) -> subprocess.CompletedProcess[str]:
        """Run the importer as users and CI will run it."""
        return subprocess.run(
            [
                "python3",
                str(IMPORT_SCRIPT),
                "--source",
                str(self.source),
                "--lock",
                str(self.lock_path),
                "--output-root",
                str(self.output),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def test_import_is_deterministic_and_records_every_file(self) -> None:
        """Repeated imports must produce identical bytes, topology, and provenance."""
        first = self.run_import()
        self.assertEqual(0, first.returncode, first.stderr)
        destination = self.output / "ReachyMini/Source"
        first_files = {
            path.relative_to(destination).as_posix(): path.read_bytes()
            for path in destination.rglob("*")
            if path.is_file()
        }

        second = self.run_import()
        self.assertEqual(0, second.returncode, second.stderr)
        second_files = {
            path.relative_to(destination).as_posix(): path.read_bytes()
            for path in destination.rglob("*")
            if path.is_file()
        }
        self.assertEqual(first_files, second_files)
        self.assertIn("reachy_mini.xml", second_files)
        self.assertIn("assets/head.stl", second_files)
        self.assertIn("assets/collision/coarse/body.stl", second_files)
        self.assertIn("UPSTREAM_LICENSE", second_files)
        self.assertIn("ATTRIBUTION.md", second_files)
        self.assertIn("MODEL_MAP.json", second_files)
        self.assertIn("PROVENANCE.json", second_files)

        model_map = json.loads(second_files["MODEL_MAP.json"])
        self.assertEqual("fixture", model_map["model"])
        self.assertEqual(
            {
                "actuators": 4,
                "bodies": 7,
                "cameras": 2,
                "equalities": 1,
                "joints": 5,
                "named_bodies": 6,
                "sites": 5,
            },
            model_map["counts"],
        )
        eye_camera = next(
            camera for camera in model_map["cameras"] if camera["name"] == "eye_camera"
        )
        self.assertEqual(
            "/world/base/stewart_arm/rod/head/@body[0]",
            eye_camera["body_path"],
        )
        self.assertEqual(
            "passive_1",
            next(joint["name"] for joint in model_map["joints"] if joint["type"] == "ball"),
        )
        provenance = json.loads(second_files["PROVENANCE.json"])
        provenance_paths = {entry["path"] for entry in provenance["files"]}
        self.assertIn("MODEL_MAP.json", provenance_paths)

    def test_dirty_checkout_fails_without_replacing_previous_output(self) -> None:
        """Modified source assets must be rejected visibly."""
        first = self.run_import()
        self.assertEqual(0, first.returncode, first.stderr)
        provenance = self.output / "ReachyMini/Source/PROVENANCE.json"
        previous = provenance.read_bytes()
        self.model_path.write_text("<mujoco/>\n", encoding="utf-8")

        result = self.run_import()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("modified or untracked files", result.stderr)
        self.assertEqual(previous, provenance.read_bytes())

    def test_revision_mismatch_fails(self) -> None:
        """The importer must not accept a checkout at another revision."""
        self.write_lock("0" * 40)
        result = self.run_import()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("revision mismatch", result.stderr)

    def test_topology_drift_fails_without_replacing_previous_output(self) -> None:
        """A changed required transform must fail before replacing known-good output."""
        first = self.run_import()
        self.assertEqual(0, first.returncode, first.stderr)
        model_map_path = self.output / "ReachyMini/Source/MODEL_MAP.json"
        previous = model_map_path.read_bytes()

        changed = self.fixture_model().replace(
            '<camera name="eye_camera" mode="fixed"/>',
            "",
        )
        self.model_path.write_text(changed, encoding="utf-8")
        new_commit = self.commit_source("remove required camera")
        self.write_lock(new_commit)

        result = self.run_import()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("cameras count mismatch", result.stderr)
        self.assertEqual(previous, model_map_path.read_bytes())

    def test_duplicate_named_joint_fails_visibly(self) -> None:
        """Duplicate model names must not create an ambiguous runtime map."""
        changed = self.fixture_model().replace(
            'name="left_antenna" type="hinge"',
            'name="right_antenna" type="hinge"',
            1,
        )
        self.model_path.write_text(changed, encoding="utf-8")
        new_commit = self.commit_source("duplicate joint name")
        self.write_lock(new_commit)

        result = self.run_import()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Duplicate joint name 'right_antenna'", result.stderr)


if __name__ == "__main__":
    unittest.main()
