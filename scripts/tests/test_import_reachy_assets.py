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
    """Exercise provenance, determinism, and visible failure behavior."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.source = self.root / "source"
        self.output = self.root / "output"
        self.lock_path = self.root / "source.lock.json"
        model_directory = self.source / "src/reachy_mini/descriptions/reachy_mini/mjcf"
        asset_directory = model_directory / "assets" / "collision/coarse"
        asset_directory.mkdir(parents=True)
        (self.source / "LICENSE").write_text("test license\n", encoding="utf-8")
        (model_directory / "assets" / "head.stl").write_bytes(b"head-mesh")
        (asset_directory / "body.stl").write_bytes(b"body-mesh")
        (model_directory / "reachy_mini.xml").write_text(
            """<?xml version="1.0"?>
<mujoco model="fixture">
  <compiler meshdir="assets"/>
  <asset>
    <mesh file="head.stl"/>
    <mesh file="collision/coarse/body.stl"/>
  </asset>
</mujoco>
""",
            encoding="utf-8",
        )
        self.run_git("init", "--quiet")
        self.run_git("config", "user.email", "test@example.invalid")
        self.run_git("config", "user.name", "Reachy Import Test")
        self.run_git("add", ".")
        self.run_git("commit", "--quiet", "-m", "fixture")
        self.commit = self.run_git("rev-parse", "HEAD").stdout.strip()
        self.write_lock(self.commit)

    def run_git(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        """Run Git in the temporary fixture checkout."""
        return subprocess.run(
            ["git", "-C", str(self.source), *arguments],
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def write_lock(self, commit: str) -> None:
        """Write a source lock for the fixture checkout."""
        lock = {
            "schema_version": 1,
            "repository": "https://example.invalid/reachy_mini.git",
            "commit": commit,
            "license_file": "LICENSE",
            "model_file": "src/reachy_mini/descriptions/reachy_mini/mjcf/reachy_mini.xml",
            "output_subdirectory": "ReachyMini/Source",
            "asset_license": "CC-BY-NC-SA",
            "software_license": "Apache-2.0",
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
        """Repeated imports must produce identical byte content and provenance."""
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
        self.assertIn("PROVENANCE.json", second_files)

    def test_dirty_checkout_fails_without_replacing_previous_output(self) -> None:
        """Modified source assets must be rejected visibly."""
        first = self.run_import()
        self.assertEqual(0, first.returncode, first.stderr)
        provenance = self.output / "ReachyMini/Source/PROVENANCE.json"
        previous = provenance.read_bytes()
        model = self.source / "src/reachy_mini/descriptions/reachy_mini/mjcf/reachy_mini.xml"
        model.write_text("<mujoco/>\n", encoding="utf-8")

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


if __name__ == "__main__":
    unittest.main()
