#!/usr/bin/env python3
"""Contract for the artifact-independent Local Unity Android device workflow."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/local-unity-android-device-no-artifacts.yml"


class LocalUnityDeviceNoArtifactsWorkflowTests(unittest.TestCase):
    def test_workflow_does_not_depend_on_artifact_transport(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertNotIn("actions/upload-artifact", workflow)
        self.assertNotIn("continue-on-error: true", workflow)

    def test_workflow_exercises_unity_apk_and_physical_acceptance_paths(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        for token in (
            "scripts/run_unity_tests.sh",
            "scripts/build_unity_android.sh device-feasibility",
            "Builds/Android/weachy-mini-device-arm64-api26.apk",
            "REACHY_ANDROID_SERIAL",
            "scripts/run_rma190_core_end_to_end_android.sh",
            "scripts/run_rma090_camera_discovery_acceptance_android.sh",
            "scripts/run_rma091_camera_acquisition_acceptance_android.sh",
            "scripts/run_rma092_camera_texture_acceptance_android.sh",
            "scripts/run_rma111_lightweight_tracking_acceptance_android.sh",
            "scripts/run_rma154_visual_servo_acceptance_android.sh",
            "scripts/run_unity_native_lifecycle_acceptance_android.sh",
            "scripts/run_rma195_application_composition_acceptance_android.sh",
            "scripts/run_unity_authoritative_rendering_acceptance_android.sh",
        ):
            self.assertIn(token, workflow)


if __name__ == "__main__":
    unittest.main()
