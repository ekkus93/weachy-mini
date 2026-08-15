import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Diagnostics"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RENDERING = ROOT / "Assets/ReachyMini/Runtime/Rendering"
RUNTIME_DIAGNOSTICS = ROOT / "Assets/ReachyMini/Runtime/Diagnostics/ReachyRuntimeDiagnostics.cs"


class Rma170StructuredLoggingTests(unittest.TestCase):
    def test_structured_identity_and_monotonic_context_contract(self) -> None:
        contracts = (CORE / "ReachyDiagnosticContracts.cs").read_text(encoding="utf-8")
        formatter = (CORE / "ReachyDiagnosticJsonFormatter.cs").read_text(encoding="utf-8")
        for token in (
            "ReachyDiagnosticSeverity",
            "ReachyDiagnosticErrorCategory",
            "ReachyDiagnosticEventDescriptor",
            "IReachyMonotonicClock",
            "Stopwatch.StartNew()",
            "SessionId",
            "TurnId",
            "EventId",
            "Component",
        ):
            self.assertIn(token, contracts)
        for json_key in (
            '"component"',
            '"severity"',
            '"event_id"',
            '"error_category"',
            '"monotonic_ms"',
            '"session_id"',
            '"turn_id"',
        ):
            self.assertIn(json_key, formatter)

    def test_redaction_and_bundle_policy_are_default_deny(self) -> None:
        redactor = (CORE / "ReachyDiagnosticRedactor.cs").read_text(encoding="utf-8")
        manifest = (CORE / "ReachyDiagnosticBundleManifest.cs").read_text(encoding="utf-8")
        contracts = (CORE / "ReachyDiagnosticContracts.cs").read_text(encoding="utf-8")
        for token in (
            "Secret",
            "PrivateText",
            "RawAudio",
            "RawImage",
            "RawMedia",
        ):
            self.assertIn(token, contracts)
            self.assertIn(f"ReachyDiagnosticDataClass.{token}", manifest)
        self.assertIn('RedactedValue = "[redacted]"', redactor)
        self.assertIn('"authorization"', redactor)
        self.assertIn('"cookie"', redactor)
        self.assertIn("RedactUrl", redactor)

    def test_rate_limiter_preserves_first_and_final_counts(self) -> None:
        logger = (CORE / "ReachyDiagnosticLogger.cs").read_text(encoding="utf-8")
        for token in (
            "DefaultRepeatWindowMilliseconds = 5000L",
            "OccurrenceCount",
            "SuppressedCount",
            "isRateLimitSummary: false",
            "isRateLimitSummary: true",
            "EmitSummary",
            "Flush()",
        ):
            self.assertIn(token, logger)
        for discriminator in (
            '"provider"',
            '"status"',
            '"exception_type"',
            '"operation"',
            '"code"',
            '"error_code"',
            '"http_error_category"',
        ):
            self.assertIn(discriminator, logger)

    def test_high_risk_runtime_paths_use_structured_boundary(self) -> None:
        files = (
            APP / "ReachyApplicationHostBehaviour.cs",
            APP / "ReachyCameraAcquisitionBootstrap.cs",
            APP / "ReachyMainScreenBootstrap.cs",
            APP / "ReachyAndroidUiThreadCameraAcquisitionPlatform.cs",
            RENDERING / "ReachyAuthoritativeRenderer.cs",
            RENDERING / "ReachyProductionAuthoritativeRuntime.cs",
        )
        for path in files:
            source = path.read_text(encoding="utf-8")
            self.assertIn("ReachyRuntimeDiagnostics.Emit", source, path.name)

        application = (APP / "ReachyApplicationHostBehaviour.cs").read_text(encoding="utf-8")
        camera = (APP / "ReachyAndroidUiThreadCameraAcquisitionPlatform.cs").read_text(
            encoding="utf-8"
        )
        runtime = (RENDERING / "ReachyProductionAuthoritativeRuntime.cs").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("disposal failed: {exception.Message}", application)
        self.assertNotIn("UI thread: " + '" +\n                    exception.Message', camera)
        self.assertNotIn("shutdown failed: {exception.Message}", runtime)

    def test_unity_sink_serializes_structured_json(self) -> None:
        source = RUNTIME_DIAGNOSTICS.read_text(encoding="utf-8")
        self.assertIn("ReachyDiagnosticJsonFormatter.Format(record)", source)
        self.assertIn("Debug.LogError(json)", source)
        self.assertIn("ReachyDiagnosticLogger", source)
        self.assertNotIn("exception.Message", source)

    def test_provider_http_errors_have_stable_categories(self) -> None:
        source = (
            ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderDiagnosticMapping.cs"
        ).read_text(encoding="utf-8")
        for token in (
            "Authentication",
            "RateLimited",
            "Client",
            "Server",
            "Transport",
            "statusCode",
        ):
            self.assertIn(token, source)


if __name__ == "__main__":
    unittest.main()
