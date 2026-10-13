"""RMA-002: fail closed when Gradle wrapper diverges from the locked toolchain."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts import verify_toolchain


class GradleWrapperPinTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.manifest = json.loads(verify_toolchain.MANIFEST_PATH.read_text(encoding="utf-8"))
        self.manifest_path = self.root / "toolchain.lock.json"
        self.project_path = self.root / "ProjectSettings" / "ProjectVersion.txt"
        self.wrapper_path = (
            self.root / "android-plugin" / "gradle" / "wrapper" / "gradle-wrapper.properties"
        )
        self.project_path.parent.mkdir(parents=True)
        self.project_path.write_text(
            f"m_EditorVersion: {self.manifest['unity']['editor_version']}\n",
            encoding="utf-8",
        )
        self.wrapper_path.parent.mkdir(parents=True)
        self.write_wrapper()
        self.save_manifest()
        for name, value in (
            ("ROOT", self.root),
            ("MANIFEST_PATH", self.manifest_path),
            ("PROJECT_VERSION_PATH", self.project_path),
        ):
            context = patch.object(verify_toolchain, name, value)
            context.start()
            self.addCleanup(context.stop)

    def save_manifest(self) -> None:
        self.manifest_path.write_text(json.dumps(self.manifest), encoding="utf-8")

    def write_wrapper(self, *, version: str | None = None, digest: str | None = None) -> None:
        android = self.manifest["android"]
        version = version or android["gradle"]
        digest = digest or android["gradle_distribution_sha256"]
        self.wrapper_path.write_text(
            f"distributionUrl=https\\://services.gradle.org/distributions/gradle-{version}-bin.zip\n"
            f"distributionSha256Sum={digest}\n",
            encoding="utf-8",
        )

    def test_matching_wrapper_passes_manifest_only(self) -> None:
        verify_toolchain.load_manifest()

    def test_changed_wrapper_distribution_fails(self) -> None:
        self.write_wrapper(version="9.4.0")
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "distributionUrl mismatch"):
            verify_toolchain.load_manifest()

    def test_changed_wrapper_checksum_fails(self) -> None:
        self.write_wrapper(digest="0" * 64)
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "distributionSha256Sum"):
            verify_toolchain.load_manifest()

    def test_missing_wrapper_fails(self) -> None:
        self.wrapper_path.unlink()
        with self.assertRaisesRegex(
            verify_toolchain.ToolchainError, "Cannot read pinned Gradle wrapper"
        ):
            verify_toolchain.load_manifest()

    def test_duplicate_checksum_property_fails(self) -> None:
        with self.wrapper_path.open("a", encoding="utf-8") as stream:
            stream.write(
                "distributionSha256Sum="
                + self.manifest["android"]["gradle_distribution_sha256"]
                + "\n"
            )
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "Duplicate Gradle wrapper"):
            verify_toolchain.load_manifest()

    def test_malformed_manifest_digest_fails(self) -> None:
        self.manifest["android"]["gradle_distribution_sha256"] = "not-a-sha"
        self.save_manifest()
        with self.assertRaisesRegex(verify_toolchain.ToolchainError, "SHA-256"):
            verify_toolchain.load_manifest()


if __name__ == "__main__":
    unittest.main()
