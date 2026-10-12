#!/usr/bin/env python3
"""Fail-closed tests for RMA-002 installed Android SDK package identity."""

import tempfile
import unittest
from pathlib import Path

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

    def test_incorrect_platform_api_fails(self) -> None:
        path = self.directories["platform"] / "source.properties"
        path.write_text("Pkg.Revision=1\nAndroidVersion.ApiLevel=36\n", encoding="utf-8")
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "platform.*mismatch"):
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
