#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


class Rma183MemoryStoragePressureTests(unittest.TestCase):
    def test_low_memory_ingress_reclaims_recreatable_resources(self) -> None:
        host = read("Assets/ReachyMini/Runtime/Application/ReachyApplicationHostBehaviour.cs")
        camera = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyAndroidCameraTextureBridge.MemoryPressure.cs"
        )
        diagnostics = read(
            "Assets/ReachyMini/Runtime/Core/Diagnostics/ReachyDiagnosticContracts.cs"
        )

        self.assertEqual(host.count("Application.lowMemory += OnLowMemory;"), 1)
        self.assertEqual(host.count("Application.lowMemory -= OnLowMemory;"), 1)
        self.assertEqual(host.count("private void OnLowMemory()"), 1)
        self.assertIn("textureBridge?.ReleaseForMemoryPressure();", host)
        self.assertIn("ReachyMemoryPressureRegistry.ReleaseRegisteredResources()", host)
        self.assertIn("Resources.UnloadUnusedAssets()", host)
        self.assertIn("ReachyDiagnosticEventIds.ApplicationLowMemoryHandled", host)
        self.assertIn('ApplicationLowMemoryHandled = "application.memory.low_handled"', diagnostics)
        self.assertIn("public void ReleaseForMemoryPressure()", camera)
        self.assertIn("InvalidateOutput(", camera)

    def test_local_llm_pressure_preserves_active_state_and_releases_idle_model(self) -> None:
        core = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalLlmProvider.Core.cs"
        )
        pressure = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/"
            "ReachyLocalLlmProvider.MemoryPressure.cs"
        )
        registry = read("Assets/ReachyMini/Runtime/Core/Application/ReachyMemoryPressure.cs")

        self.assertIn("ReachyMemoryPressureRegistry.Register(this)", core)
        self.assertIn("memoryPressureRegistration.Dispose();", core)
        self.assertIn("IReachyMemoryPressureParticipant", pressure)
        self.assertIn("LocalLlmProviderState.Loading", pressure)
        self.assertIn("LocalLlmProviderState.Generating", pressure)
        self.assertIn("ReachyMemoryPressureReleaseStatus.RetainedActiveState", pressure)
        self.assertIn("runtime.UnloadModel(modelHandle)", pressure)
        self.assertIn("modelHandle = 0UL;", pressure)
        self.assertIn("state = LocalLlmProviderState.Unavailable;", pressure)
        self.assertIn("public static IDisposable Register", registry)
        self.assertIn("Participants.Remove(registered)", registry)

    def test_model_acquisition_rechecks_storage_without_destroying_resume_state(self) -> None:
        contracts = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelPackageContracts.cs"
        )
        paths = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelPackageManager.Paths.cs"
        )
        download = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelPackageManager.Download.cs"
        )

        self.assertIn("StorageRecheckIntervalBytes = 4L * 1024L * 1024L", contracts)
        self.assertIn("written >= nextStorageCheck", paths)
        self.assertIn("CheckStorage(maximumBytes - written)", paths)
        self.assertIn("StoragePressureIOException", paths)
        self.assertIn("return pressure.Failure;", paths)
        self.assertIn(
            '"The model download ended early; the manifest-bound partial remains resumable."',
            download,
        )
        self.assertIn("Writing the resumable model download failed", download)
        self.assertNotIn(
            'DeleteFileIfPresent(partPath);\n                    return IoFailure(\n'
            '                        "Writing the resumable model download failed"',
            download,
        )

    def test_diagnostic_export_preflights_and_classifies_storage_pressure(self) -> None:
        storage = read(
            "Assets/ReachyMini/Runtime/Core/Diagnostics/ReachyDiagnosticBundleStorage.cs"
        )
        coordinator = read(
            "Assets/ReachyMini/Runtime/Application/ReachyDiagnosticBundleExportCoordinator.cs"
        )

        self.assertIn("DefaultSafetyReserveBytes = 16L * 1024L * 1024L", storage)
        self.assertIn("IReachyDiagnosticBundleStorageProbe", storage)
        self.assertIn("RequireStorageCapacity(directory);", storage)
        self.assertIn("ReachyDiagnosticBundleExporter.MaximumBundleBytes", storage)
        self.assertIn("ReachyDiagnosticBundleInsufficientStorageException", storage)
        self.assertIn("ReachyStorageAwareDiagnosticBundleExporter", coordinator)
        self.assertIn("catch (ReachyDiagnosticBundleInsufficientStorageException)", coordinator)
        self.assertIn('"insufficient_storage"', coordinator)
        self.assertIn("Use Recoverable Storage Cleanup and retry", coordinator)

    def test_cleanup_ui_is_narrow_and_preserves_durable_state(self) -> None:
        settings = read(
            "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.SettingsSections.cs"
        )
        main_screen = read(
            "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.StorageCleanup.cs"
        )
        cleanup = read(
            "Assets/ReachyMini/Runtime/Application/ReachyStorageCleanupCoordinator.cs"
        )

        self.assertIn("CLEAN UP RECOVERABLE STORAGE", settings)
        self.assertIn("CleanupRecoverableStorage();", settings)
        self.assertIn("Application.persistentDataPath", main_screen)
        self.assertIn('"diagnostics"', main_screen)
        self.assertIn('DiagnosticFilePrefix = "reachy-diagnostics-"', cleanup)
        self.assertIn("SearchOption.TopDirectoryOnly", cleanup)
        self.assertIn("IsOwnedDiagnosticArtifact(file)", cleanup)
        self.assertIn("IsReparsePoint(file)", cleanup)
        self.assertIn("Caching.ClearCache()", cleanup)
        preservation = "Installed models, settings, credentials, and user state were retained."
        self.assertIn(preservation, cleanup)

    def test_managed_rma183_contracts_are_registered(self) -> None:
        program = read("managed/ReachyMini.Core.Tests/Program.cs")
        core_tests = read(
            "managed/ReachyMini.Core.Tests/Rma183MemoryStoragePressureContractTests.cs"
        )
        llm_program = read("managed/ReachyMini.LocalLlm.Tests/Program.cs")
        llm_tests = read("managed/ReachyMini.LocalLlm.Tests/Program.MemoryPressureTests.cs")

        self.assertIn("Rma183MemoryStoragePressureContractTests.RunAll();", program)
        self.assertIn("MemoryPressureRegistryCountsReleaseAndActiveRetention();", core_tests)
        self.assertIn("DiagnosticExportFailsBeforeWriteWhenStorageIsLow();", core_tests)
        self.assertIn("TestMemoryPressureReleasesOnlyIdleModelAsync()", llm_program)
        self.assertIn("TestMemoryPressureRetainsActiveGenerationAsync()", llm_program)
        self.assertIn("TestMemoryPressureRetainsActiveReloadAsync()", llm_program)
        self.assertIn("ReachyMemoryPressureReleaseStatus.Released", llm_tests)
        self.assertIn("ReachyMemoryPressureReleaseStatus.RetainedActiveState", llm_tests)

    def test_roadmap_closes_every_rma183_item(self) -> None:
        roadmap = read("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
        block = roadmap.split("## RMA-183 — Handle memory and storage pressure", 1)[1]
        block = block.split("## RMA-184", 1)[0]
        self.assertIn("**Status:** Complete (2026-08-15)", block)
        self.assertEqual(block.count("- [x]"), 4)
        self.assertNotIn("- [ ]", block)
        self.assertIn("RMA_183_MEMORY_STORAGE_PRESSURE_SPEC_2026-08-15.md", block)
        self.assertIn(
            "RMA_183_MEMORY_STORAGE_PRESSURE_LOCAL_VALIDATION_2026-08-15.md",
            block,
        )
        self.assertIn("scripts/tests/test_rma183_memory_storage_pressure.py", block)


if __name__ == "__main__":
    unittest.main()
