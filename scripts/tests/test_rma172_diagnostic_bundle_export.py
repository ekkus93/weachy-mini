import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Diagnostics"
RUNTIME = ROOT / "Assets/ReachyMini/Runtime/Diagnostics/ReachyRuntimeDiagnostics.cs"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma172DiagnosticBundleExportTests(unittest.TestCase):
    def test_bundle_is_bounded_atomic_and_contains_required_entries(self) -> None:
        exporter = (CORE / "ReachyDiagnosticBundleExporter.cs").read_text(encoding="utf-8")
        for token in (
            "MaximumEntryBytes",
            "MaximumBundleBytes",
            "ZipArchiveMode.Create",
            "FileMode.CreateNew",
            '"manifest.json"',
            '"version-configuration.json"',
            '"performance-health.json"',
            '"logs.jsonl"',
            "File.Move(temporaryPath, fullPath)",
            "File.Exists(fullPath)",
            "will not overwrite an existing file",
        ):
            self.assertIn(token, exporter)

    def test_export_redacts_again_and_sensitive_selections_fail_closed(self) -> None:
        exporter = (CORE / "ReachyDiagnosticBundleExporter.cs").read_text(encoding="utf-8")
        contracts = (CORE / "ReachyDiagnosticBundleContracts.cs").read_text(encoding="utf-8")
        security = (
            ROOT / "Assets/ReachyMini/Runtime/Core/Security/ReachyImportedContentSecurity.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ReachyDiagnosticRedactor.Redact(record.Fields[index])", exporter)
        self.assertIn('SanitizeIdentifier("session_id", record.Context.SessionId)', exporter)
        self.assertIn('SanitizeText("metric_value", metric.Value)', exporter)
        self.assertIn('SanitizeText("metric_reason", metric.Reason)', exporter)
        self.assertIn("RequireRedactedOnlySelection", exporter)
        self.assertIn("ReachyDiagnosticBundleUserSelection.RedactedOnly", exporter)
        self.assertIn("Sensitive diagnostic export is not implemented", exporter)
        self.assertIn("IncludePrivateText", contracts)
        self.assertIn("IncludeRawMedia", contracts)
        self.assertIn("IncludeCredentials", contracts)
        self.assertIn("ReachyDiagnosticBundleSecurityPolicy.RequireExportable", exporter)
        self.assertIn("kind == ReachyDiagnosticArtifactKind.RedactedText", security)

    def test_manifest_describes_redactions_and_exclusions(self) -> None:
        exporter = (CORE / "ReachyDiagnosticBundleExporter.cs").read_text(encoding="utf-8")
        for token in (
            "redaction_policy",
            "structured_log_redaction",
            "default_exclusions",
            "credentials, raw audio, raw images, raw media, transcripts, conversation text",
            "sensitive_export",
            "denied_data_classes",
            "DefaultDeniedDataClasses",
            "sha256",
            "classification",
            "dropped_log_records",
        ):
            self.assertIn(token, exporter)

    def test_runtime_retains_only_a_bounded_recent_log_window(self) -> None:
        buffer_source = (CORE / "ReachyDiagnosticRecordBuffer.cs").read_text(encoding="utf-8")
        runtime = RUNTIME.read_text(encoding="utf-8")
        for token in (
            "DefaultCapacity = 512",
            "MaximumCapacity = 4096",
            "DroppedCount",
            "Snapshot()",
            "ReachyCompositeDiagnosticSink",
        ):
            self.assertIn(token, buffer_source)
        self.assertIn("ReachyDiagnosticRecordBuffer recordBuffer", runtime)
        self.assertIn("CaptureRecentRecords()", runtime)
        self.assertIn("DroppedCapturedRecordCount", runtime)
        self.assertIn("new UnityDiagnosticSink()", runtime)
        self.assertIn("recordBuffer", runtime)

    def test_diagnostics_panel_exposes_redacted_only_user_action(self) -> None:
        screen = (APP / "ReachyMainScreen.cs").read_text(encoding="utf-8")
        hud = (APP / "ReachyMainScreen.Hud.cs").read_text(encoding="utf-8")
        coordinator = (APP / "ReachyDiagnosticBundleExportCoordinator.cs").read_text(
            encoding="utf-8"
        )
        composition = (APP / "ReachySettingsApplicationCompositionProvider.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("ConfigureDiagnosticBundleExport", screen)
        self.assertIn("ExportDiagnosticBundle()", screen)
        self.assertIn("EXPORT REDACTED", hud)
        self.assertIn("Sensitive content is excluded by policy", screen)
        self.assertIn("ExportRedactedBundle", coordinator)
        self.assertIn("ReachyRuntimeDiagnostics.Flush()", coordinator)
        self.assertIn("CaptureRecentRecords()", coordinator)
        self.assertIn("Application.persistentDataPath", composition)
        self.assertIn("ConfigureDiagnosticBundleExport", composition)

    def test_rma172_roadmap_is_closed(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        start = todo.index("## RMA-172 — Implement diagnostic bundle export")
        end = todo.index("## RMA-173", start)
        block = todo[start:end]
        self.assertNotIn("- [ ]", block)
        self.assertEqual(4, block.count("- [x]"))


if __name__ == "__main__":
    unittest.main()
