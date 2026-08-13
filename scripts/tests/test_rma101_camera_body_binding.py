import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachyCameraAuthoritativeRotation.cs"
UNITY_TEST = ROOT / "Assets/ReachyMini/Tests/Editor/ReachyAuthoritativeCameraRotationTests.cs"
BINDING = ROOT / "models/reachy-mini/camera-reprojection-binding.json"
WORKFLOW = ROOT / ".github/workflows/rma101-authoritative-camera-rotation.yml"


class Rma101CameraBodyBindingContracts(unittest.TestCase):
    def test_presentation_index_and_native_body_id_are_distinct(self) -> None:
        core = CORE.read_text(encoding="utf-8")
        self.assertIn("CanonicalCameraPresentationIndex = 15", core)
        self.assertIn("CanonicalCameraBodyId = 16U", core)

        camera = json.loads(BINDING.read_text(encoding="utf-8"))["authoritative_camera"]
        self.assertEqual("__body_15", camera["canonical_body_name"])
        self.assertEqual(15, camera["canonical_body_index"])
        self.assertEqual(16, camera["body_id"])

    def test_unity_and_provenance_gates_use_the_correct_identifier_namespace(self) -> None:
        unity = UNITY_TEST.read_text(encoding="utf-8")
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn(".CanonicalCameraPresentationIndex", unity)
        self.assertNotIn("body.BodyIndex != (int)binding.CameraBodyId", unity)
        self.assertIn("presentation_index = non_world_bodies.index(camera_body)", workflow)
        self.assertIn("mujoco_body_id = presentation_index + 1", workflow)
        self.assertIn("presentation_index != 15 or mujoco_body_id != 16", workflow)


if __name__ == "__main__":
    unittest.main()
