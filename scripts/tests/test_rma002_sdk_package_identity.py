#!/usr/bin/env python3
"""Fail-closed tests for RMA-002 installed Android SDK package identity."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts import verify_toolchain


class AndroidSdkPackageIdentityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.manifest = {
            "android": {
                "compile_sdk": 37,
                "compile_sdk_package": "37.0",
                "build_tools": "36.0.0",
                "ndk": "28.2.13676358",
                "cmake": "3.31.6",
            }
        }
        self.directories = {
            "platform": self.root / "platforms" / "android-37.0",
            "build_tools": self.root / "build-tools" / "36.0.0",
            "ndk": self.root / "ndk" / "28.2.13676358",
            "cmake": self.root / "cmake" / "3.31.6",
        }
        versions = {
            "platform": "Pkg.Revision=1\nAndroidVersion.ApiLevel=37\n",
            "build_tools": "Pkg.Revision=36.0.0\n",
            "ndk": "Pkg.Revision=28.2.13676358\n",
            "cmake": "Pkg.Revision=3.31.6\n",
        }
        for name, directory in self.directories.items():
            directory.mkdir(parents=True)
            (directory / "source.properties").write_text(versions[name], encoding="utf-8")

    def test_missing_build_tools_pin_fails_manifest_preflight(self) -> None:
        manifest = json.loads(verify_toolchain.MANIFEST_PATH.read_text(encoding="utf-8"))
        del manifest["android"]["build_tools"]
        manifest_path = self.root / "invalid-toolchain.lock.json"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        with mock.patch.object(verify_toolchain, "MANIFEST_PATH", manifest_path):
            with self.assertRaisesRegex(verify_toolchain.ToolchainError, "build_tools"):
                verify_toolchain.load_manifest()

    def test_unity_build_invokes_sdk_metadata_preflight(self) -> None:
        build_script = (verify_toolchain.ROOT / "scripts" / "build_unity_android.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("verify_toolchain.py\" --android-sdk-only", build_script)

    def test_valid_installed_metadata_passes(self) -> None:
        verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_wrong_revision_in_pinned_directory_fails(self) -> None:
        for name in ("build_tools", "ndk", "cmake"):
            with self.subTest(package=name):
                properties = self.directories[name] / "source.properties"
                expected_content = properties.read_text(encoding="utf-8")
                properties.write_text("Pkg.Revision=0.0.0\n", encoding="utf-8")
                with self.assertRaisesRegex(verify_toolchain.ToolchainError, "mismatch"):
                    verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)
                properties.write_text(expected_content, encoding="utf-8")

    def test_platform_package_api_suffix_passes(self) -> None:
        path = self.directories["platform"] / "source.properties"
        path.write_text(
            "Pkg.Revision=1\nAndroidVersion.ApiLevel=37.0\n",
            encoding="utf-8",
        )
        verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_wrong_platform_api_extension_fails(self) -> None:
        path = self.directories["platform"] / "source.properties"
        path.write_text(
            "Pkg.Revision=1\nAndroidVersion.ApiLevel=37.1\n",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "platform.*mismatch"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_incorrect_platform_api_fails(self) -> None:
        path = self.directories["platform"] / "source.properties"
        path.write_text("Pkg.Revision=1\nAndroidVersion.ApiLevel=36\n", encoding="utf-8")
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "platform.*mismatch"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_optional_empty_java_properties_value_is_accepted(self) -> None:
        path = self.directories["platform"] / "source.properties"
        path.write_text(
            "Pkg.Revision=1\nAndroidVersion.ApiLevel=37\nAndroidVersion.CodeName=\n",
            encoding="utf-8",
        )
        verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_empty_required_version_fails(self) -> None:
        path = self.directories["ndk"] / "source.properties"
        path.write_text("Pkg.Revision=\n", encoding="utf-8")
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "mismatch"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_missing_package_metadata_fails(self) -> None:
        (self.directories["ndk"] / "source.properties").unlink()
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "metadata"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_duplicate_revision_metadata_fails(self) -> None:
        path = self.directories["build_tools"] / "source.properties"
        path.write_text("Pkg.Revision=36.0.0\nPkg.Revision=36.0.0\n", encoding="utf-8")
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "Duplicate"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)

    def test_missing_package_directory_fails(self) -> None:
        (self.directories["cmake"] / "source.properties").unlink()
        self.directories["cmake"].rmdir()
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "directory is missing"):
            verify_toolchain.verify_android_sdk_packages(self.manifest, self.root)


if __name__ == "__main__":
    unittest.main()
