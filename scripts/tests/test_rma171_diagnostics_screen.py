import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Diagnostics"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RENDERING = ROOT / "Assets/ReachyMini/Runtime/Rendering"


class Rma171DiagnosticsScreenTests(unittest.TestCase):
    def test_typed_snapshot_requires_explicit_unavailable_reasons(self) -> None:
        contracts = (CORE / "ReachyDiagnosticsScreenContracts.cs").read_text(encoding="utf-8")
        for token in (
            "ReachyDiagnosticsAvailability",
            "Available = 0",
            "Degraded = 1",
            "Unavailable = 2",
            "A diagnostics section requires 1-",
            'Value = "unavailable"',
            "Reason = RequireText(reason",
        ):
            self.assertIn(token, contracts)

    def test_all_required_sections_and_metrics_are_rendered(self) -> None:
        source = (APP / "ReachyDiagnosticsScreenSource.cs").read_text(encoding="utf-8")
        for section in (
            '"Simulation"',
            '"Rendering"',
            '"Camera"',
            '"Providers"',
            '"Versions"',
            '"Device"',
        ):
            self.assertIn(section, source)
        for metric in (
            '"Observed physics frequency"',
            '"Last / max step time"',
            '"Missed deadlines"',
            '"Accumulated lag"',
            '"Constraint health"',
            '"Fault"',
            '"Render FPS"',
            '"Allocated memory"',
            '"Thermal state"',
            '"Device profile"',
            '"Camera FPS"',
            '"Reprojection time"',
            '"Valid coverage"',
            '"Active camera"',
            '"Simulation model"',
            '"Calibration"',
            '"Native ABI"',
            '"MuJoCo"',
            '"Reachy asset"',
            '"App"',
        ):
            self.assertIn(metric, source)

    def test_provider_locality_comes_from_durable_selection(self) -> None:
        source = (APP / "ReachyDiagnosticsScreenSource.cs").read_text(encoding="utf-8")
        self.assertIn("currentSettings.GetProvider(kind)", source)
        self.assertIn("provider.Execution", source)
        self.assertIn("provider.Connectivity", source)
        self.assertIn("provider.Available", source)
        self.assertNotIn("api.openai.com", source)

    def test_screen_uses_typed_diagnostics_with_legacy_adapter_only(self) -> None:
        screen = (APP / "ReachyMainScreen.cs").read_text(encoding="utf-8")
        hud = (APP / "ReachyMainScreen.Hud.cs").read_text(encoding="utf-8")
        composition = (APP / "ReachySettingsApplicationCompositionProvider.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("Func<ReachyDiagnosticsScreenSnapshot>? diagnosticsProvider", screen)
        self.assertIn("Func<ReachyDiagnosticsScreenSnapshot> currentDiagnostics", screen)
        self.assertIn("ReachyDiagnosticsScreenSnapshot.FromLegacyText", screen)
        self.assertIn("diagnostics.ToDisplayText()", hud)
        self.assertIn("BuildDiagnosticsSnapshot", composition)
        self.assertIn("diagnosticsSource.Capture()", composition)

    def test_missing_camera_pipeline_metrics_fail_visible(self) -> None:
        source = (APP / "ReachyDiagnosticsScreenSource.cs").read_text(encoding="utf-8")
        self.assertIn(
            "The production camera path does not yet publish a reprojection timing snapshot.",
            source,
        )
        self.assertIn(
            "No production homography-coverage snapshot is bound to the application shell.",
            source,
        )
        self.assertNotIn('"0.0 ms"', source)
        self.assertNotIn('"100.0%"', source)

    def test_version_identity_is_not_duplicated_magic_data(self) -> None:
        source = (APP / "ReachyDiagnosticsScreenSource.cs").read_text(encoding="utf-8")
        runtime = (RENDERING / "ReachyProductionAuthoritativeRuntime.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("ProjectMetadata.NativeAbiVersion", source)
        self.assertIn("ReachyProductionAuthoritativeRuntime.RequiredMujocoVersion", source)
        self.assertIn("runtime.ReachyAssetSourceHash", source)
        self.assertIn('public const string RequiredMujocoVersion = "3.9.0";', runtime)
        self.assertIn("manifest.mujoco_version, RequiredMujocoVersion", runtime)


if __name__ == "__main__":
    unittest.main()
