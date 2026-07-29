"""Tests for deterministic Reachy STL-to-Unity render preparation."""

from __future__ import annotations

import hashlib
import json
import struct
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "prepare_reachy_unity_assets.py"


class ReachyUnityAssetPreparationTests(unittest.TestCase):
    """Exercise conversion, coordinate, camera-isolation, and failure behavior."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.source = self.root / "source"
        self.output = self.root / "output" / "Presentation"
        (self.source / "assets").mkdir(parents=True)
        self.write_binary_stl(self.source / "assets" / "base.stl")
        self.write_ascii_stl(self.source / "assets" / "head.stl")
        model_path = self.source / "reachy_mini.xml"
        model_path.write_text(self.fixture_mjcf(), encoding="utf-8")
        model_sha256 = hashlib.sha256(model_path.read_bytes()).hexdigest()
        model_map = {
            "schema_version": 1,
            "model": "reachy_mini",
            "counts": {"bodies": 3},
            "source_model": {
                "path": "reachy_mini.xml",
                "sha256": model_sha256,
            },
        }
        (self.source / "MODEL_MAP.json").write_text(
            json.dumps(model_map, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        provenance = {
            "schema_version": 1,
            "source_commit": "0123456789abcdef0123456789abcdef01234567",
            "content_modified": False,
            "files": [],
        }
        (self.source / "PROVENANCE.json").write_text(
            json.dumps(provenance, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )

    @staticmethod
    def write_binary_stl(path: Path) -> None:
        """Write one exact binary STL triangle."""
        header = b"Reachy test binary STL".ljust(80, b"\0")
        triangle = struct.pack(
            "<12fH",
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0,
        )
        path.write_bytes(header + struct.pack("<I", 1) + triangle)

    @staticmethod
    def write_ascii_stl(path: Path) -> None:
        """Write one strict ASCII STL triangle."""
        path.write_text(
            """solid head
facet normal 0 1 0
  outer loop
    vertex 0 0 0
    vertex 0 1 0
    vertex 0 0 1
  endloop
endfacet
endsolid head
""",
            encoding="ascii",
        )

    @staticmethod
    def fixture_mjcf() -> str:
        """Return a compact render fixture with source cameras and two bodies."""
        return """<?xml version="1.0"?>
<mujoco model="reachy_mini">
  <compiler angle="radian" meshdir="assets"/>
  <worldbody>
    <camera name="studio_close" mode="targetbodycom" target="head"/>
    <body name="base" pos="1 2 3" quat="1 0 0 0">
      <geom class="visual" type="mesh" mesh="base" material="dark"/>
      <geom class="collision" type="mesh" mesh="base"/>
      <body name="head" pos="0 0 1" quat="0.7071067811865476 0 0 0.7071067811865475">
        <geom class="visual" type="mesh" mesh="head" material="light" pos="0 2 0"/>
        <body>
          <camera name="eye_camera" mode="fixed"/>
        </body>
      </body>
    </body>
  </worldbody>
  <asset>
    <mesh name="base" file="base.stl" scale="2 3 4"/>
    <mesh name="head" file="head.stl"/>
    <material name="dark" rgba="0.1 0.2 0.3 1"/>
    <material name="light" rgba="0.8 0.9 1 0.5"/>
  </asset>
</mujoco>
"""

    def run_conversion(self) -> subprocess.CompletedProcess[str]:
        """Run the public conversion CLI."""
        return subprocess.run(
            [
                "python3",
                str(SCRIPT),
                "--source",
                str(self.source),
                "--output",
                str(self.output),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def output_bytes(self) -> dict[str, bytes]:
        """Return every generated file by relative path."""
        return {
            path.relative_to(self.output).as_posix(): path.read_bytes()
            for path in self.output.rglob("*")
            if path.is_file()
        }

    def test_conversion_is_deterministic_and_preserves_source(self) -> None:
        """Repeated conversion must be byte-identical and leave input untouched."""
        source_hashes = {
            path.relative_to(self.source).as_posix(): hashlib.sha256(
                path.read_bytes()
            ).hexdigest()
            for path in self.source.rglob("*")
            if path.is_file()
        }
        first = self.run_conversion()
        self.assertEqual(0, first.returncode, first.stderr)
        first_output = self.output_bytes()
        second = self.run_conversion()
        self.assertEqual(0, second.returncode, second.stderr)
        self.assertEqual(first_output, self.output_bytes())
        self.assertEqual(
            source_hashes,
            {
                path.relative_to(self.source).as_posix(): hashlib.sha256(
                    path.read_bytes()
                ).hexdigest()
                for path in self.source.rglob("*")
                if path.is_file()
            },
        )

    def test_manifest_records_hierarchy_materials_and_camera_isolation(self) -> None:
        """The render contract must expose mapped poses and exclude source cameras."""
        result = self.run_conversion()
        self.assertEqual(0, result.returncode, result.stderr)
        manifest = json.loads(
            (self.output / "UNITY_RENDER_MAP.json").read_text(encoding="utf-8")
        )
        self.assertEqual(
            "reachy_stl_to_unity_obj_v1",
            manifest["transformation"]["id"],
        )
        self.assertTrue(
            manifest["transformation"]["coordinate_mapping"][
                "mesh_winding_reversed"
            ]
        )
        self.assertFalse(manifest["presentation"]["source_cameras_included"])
        self.assertEqual(
            {"studio_close", "eye_camera"},
            {camera["name"] for camera in manifest["source_cameras"]},
        )
        self.assertTrue(
            all(
                not camera["included_in_presentation"]
                for camera in manifest["source_cameras"]
            )
        )
        base = next(body for body in manifest["bodies"] if body["name"] == "base")
        self.assertEqual(
            [1.0, 3.0, 2.0],
            base["local_pose_unity"]["position_metres"],
        )
        head_geom = next(
            geom
            for geom in manifest["visual_geoms"]
            if geom["material"] == "light"
        )
        self.assertEqual(
            [0.0, 0.0, 2.0],
            head_geom["local_pose_unity"]["position_metres"],
        )
        self.assertEqual(2, len(manifest["visual_geoms"]))
        self.assertEqual(2, len(manifest["materials"]))
        base_mesh = next(
            mesh for mesh in manifest["meshes"] if mesh["name"] == "base"
        )
        self.assertEqual(1, base_mesh["triangle_count"])
        self.assertEqual(
            hashlib.sha256((self.source / "MODEL_MAP.json").read_bytes()).hexdigest(),
            manifest["source"]["model_map_sha256"],
        )

    def test_obj_applies_scale_basis_change_and_reversed_winding(self) -> None:
        """OBJ coordinates must follow the documented MuJoCo-to-Unity mapping."""
        result = self.run_conversion()
        self.assertEqual(0, result.returncode, result.stderr)
        obj = (self.output / "Meshes" / "base.obj").read_text(encoding="utf-8")
        self.assertIn("v 2 0 0", obj)
        self.assertIn("v 0 0 3", obj)
        self.assertIn("vn 0 -1 0", obj)
        self.assertIn("f 1//1 3//1 2//1", obj)

    def test_failure_preserves_previous_known_good_output(self) -> None:
        """Malformed replacement input must not destroy prior generated assets."""
        first = self.run_conversion()
        self.assertEqual(0, first.returncode, first.stderr)
        known_good = self.output_bytes()
        (self.source / "assets" / "head.stl").write_bytes(b"not a valid STL")
        failed = self.run_conversion()
        self.assertNotEqual(0, failed.returncode)
        self.assertIn("Unsupported ASCII STL syntax", failed.stderr)
        self.assertEqual(known_good, self.output_bytes())

    def test_output_cannot_be_inside_source_package(self) -> None:
        """Conversion must never replace or nest within the authoritative source."""
        result = subprocess.run(
            [
                "python3",
                str(SCRIPT),
                "--source",
                str(self.source),
                "--output",
                str(self.source / "Presentation"),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must not overwrite", result.stderr)


if __name__ == "__main__":
    unittest.main()
