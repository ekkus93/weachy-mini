import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"


class Rma163ImportedContentSecurityTests(unittest.TestCase):
    def test_imported_json_reads_are_bounded_and_strict_utf8(self) -> None:
        policy = (CORE / "Security/ReachyImportedContentSecurity.cs").read_text(encoding="utf-8")
        for token in (
            "MaximumCameraCalibrationBytes",
            "MaximumLocalModelManifestBytes",
            "MaximumLocalVlmManifestBytes",
            "MaximumDurableSettingsBytes",
            "MaximumProviderProfilesBytes",
            "MaximumFallbackPoliciesBytes",
            "MaximumLocalModelMetadataBytes",
            "throwOnInvalidBytes: true",
            "ReadBoundedUtf8File",
            "RequireBoundedUtf8Text",
        ):
            self.assertIn(token, policy)
        expected = {
            "ReachyCameraCalibrationPersistence.cs": "CameraCalibration",
            "ReachySettingsPersistence.cs": "DurableSettings",
            "ReachyProviderProfilePersistence.cs": "ProviderProfiles",
            "ReachyFallbackPolicyPersistence.cs": "FallbackPolicies",
        }
        for filename, document_kind in expected.items():
            source = (APP / filename).read_text(encoding="utf-8")
            self.assertIn("ReachyImportedContentPolicy.ReadBoundedUtf8File", source)
            self.assertIn("ReachyImportedContentPolicy.RequireBoundedUtf8Text", source)
            self.assertIn(f"ReachyImportedDocumentKind.{document_kind}", source)
            self.assertNotIn("File.ReadAllText(persistencePath)", source)
        settings = (APP / "ReachySettingsPersistence.cs").read_text(encoding="utf-8")
        self.assertIn("RequireBoundedUtf8Text", settings)
        self.assertNotIn("File.ReadAllText(backupPath)", settings)
        readiness = (CORE / "LocalModels/ReachyLocalModelPackageManager.Readiness.cs").read_text(
            encoding="utf-8"
        )
        package_contracts = (CORE / "LocalModels/ReachyLocalModelPackageContracts.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("DefaultMaximumArtifactBytes", package_contracts)
        self.assertIn("manifest.Artifact.FileSizeBytes > options.MaximumArtifactBytes", readiness)
        self.assertIn("ReachyImportedDocumentKind.LocalModelMetadata", readiness)
        self.assertNotIn("File.ReadAllText(markerPath", readiness)
        self.assertNotIn("File.ReadAllText(markerTemporaryPath", readiness)

    def test_calibration_schema_and_numeric_ranges_are_bounded(self) -> None:
        persistence = (APP / "ReachyCameraCalibrationPersistence.cs").read_text(
            encoding="utf-8"
        )
        profile = (CORE / "Application/ReachyCameraCalibrationProfile.cs").read_text(
            encoding="utf-8"
        )
        intrinsics = (CORE / "Application/ReachyCameraIntrinsics.cs").read_text(
            encoding="utf-8"
        )
        provider_persistence = (
            APP / "ReachyProviderProfilePersistence.cs"
        ).read_text(encoding="utf-8")
        state = (CORE / "Application/ReachyCameraCalibrationStateStore.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn(
            "MaximumProfiles = ReachyCameraCalibrationStateStore.MaximumProfiles",
            persistence,
        )
        self.assertIn("profileDtos.Length > MaximumProfiles", persistence)
        self.assertIn("MaximumProfiles = 64", state)
        self.assertIn("profiles.Count > MaximumProfiles", state)
        self.assertIn("next.Count >= MaximumProfiles", state)
        self.assertIn("MaximumTextCharacters = 512", profile)
        self.assertIn(
            "Enum.IsDefined(typeof(ReachyCameraCalibrationProvenance)", profile
        )
        self.assertIn("MaximumImageDimension = 16384", intrinsics)
        self.assertIn("(long)cropLeft + cropWidth", intrinsics)
        self.assertIn("(long)cropTop + cropHeight", intrinsics)
        self.assertIn("MaximumProfiles = 64", provider_persistence)
        self.assertIn("stored.Length > MaximumProfiles", provider_persistence)

    def test_model_download_and_provider_urls_use_central_host_policy(self) -> None:
        security = (CORE / "Security/ReachyImportedContentSecurity.cs").read_text(
            encoding="utf-8"
        )
        provider = (CORE / "Providers/ReachyProviderConfiguration.cs").read_text(
            encoding="utf-8"
        )
        manifest = (CORE / "LocalModels/ReachyLocalModelManifest.cs").read_text(
            encoding="utf-8"
        )
        download = (
            CORE / "LocalModels/ReachyLocalModelPackageManager.Download.cs"
        ).read_text(encoding="utf-8")
        transport = (
            CORE / "LocalModels/HttpLocalModelDownloadTransport.cs"
        ).read_text(encoding="utf-8")
        vlm = (CORE / "Perception/ReachyLocalVlmManifestContracts.cs").read_text(
            encoding="utf-8"
        )
        for token in (
            "RequirePublicHttpsUri",
            "IsTrustedLocalDevelopmentHost",
            'host.EndsWith(".local"',
            "IPAddress.IsLoopback",
            "bytes[0] == 169 && bytes[1] == 254",
            "bytes[0] == 10",
            "bytes[0] == 192 && bytes[1] == 168",
        ):
            self.assertIn(token, security)
        self.assertIn("RequireValidHost(baseUri", provider)
        self.assertIn("IsTrustedLocalDevelopmentHost(baseUri)", provider)
        self.assertIn("RequirePublicHttpsUri", manifest)
        self.assertIn("RequirePublicHttpsUri", download)
        self.assertIn("RequirePublicHttpsUri", transport)
        self.assertIn("RequirePublicHttpsUri", vlm)

    def test_path_traversal_and_arbitrary_model_overwrite_stay_denied(self) -> None:
        manifest = (CORE / "LocalModels/ReachyLocalModelManifest.cs").read_text(
            encoding="utf-8"
        )
        paths = (
            CORE / "LocalModels/ReachyLocalModelPackageManager.Paths.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("RequireSafeRelativeGgufPath", manifest)
        self.assertIn('string.Equals(segment, ".."', manifest)
        self.assertIn("RequireContainedPath", paths)
        self.assertIn("Path.GetFullPath", paths)
        self.assertIn("escaped the managed store root", paths)

    def test_diagnostic_bundle_gate_denies_secret_and_private_media(self) -> None:
        security = (CORE / "Security/ReachyImportedContentSecurity.cs").read_text(
            encoding="utf-8"
        )
        for token in (
            "IncludeSecretsByDefault = false",
            "IncludePrivateMediaByDefault = false",
            "Secret = 1",
            "PrivateMedia = 2",
            "kind == ReachyDiagnosticArtifactKind.RedactedText",
        ):
            self.assertIn(token, security)


if __name__ == "__main__":
    unittest.main()
