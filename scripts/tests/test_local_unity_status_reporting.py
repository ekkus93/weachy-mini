import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/local-unity-android-validation.yml"


def workflow_step(workflow: str, name: str) -> str:
    marker = f"      - name: {name}\n"
    start = workflow.index(marker)
    remainder = workflow[start + len(marker) :]
    next_step = remainder.find("\n      - name: ")
    if next_step == -1:
        return workflow[start:]
    return workflow[start : start + len(marker) + next_step]


class LocalUnityStatusReportingContracts(unittest.TestCase):
    def test_status_reporting_retries_connectivity_without_hiding_http_failures(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")

        self.assertEqual(2, workflow.count("local max_attempts=3"))
        self.assertEqual(2, workflow.count("--connect-timeout 5"))
        self.assertEqual(2, workflow.count("--max-time 15"))
        self.assertEqual(2, workflow.count("5|6|7|28|35|52|55|56)"))
        self.assertEqual(2, workflow.count('return "${curl_status}"'))
        self.assertEqual(
            2,
            workflow.count("GitHub commit status publication unavailable"),
        )
        self.assertEqual(1, workflow.count("publish_commit_status pending"))
        self.assertEqual(1, workflow.count("publish_commit_status final"))

        for name in ("Publish pending commit status", "Publish final commit status"):
            with self.subTest(step=name):
                block = workflow_step(workflow, name)
                self.assertNotIn("continue-on-error: true", block)
                self.assertIn("curl --fail-with-body", block)
                self.assertIn("Commit-status publication failed", block)

        upload_blocks = [
            block
            for block in workflow.split("\n      - name: ")[1:]
            if "uses: actions/upload-artifact@v4" in block
        ]
        self.assertGreaterEqual(len(upload_blocks), 10)
        for block in upload_blocks:
            with self.subTest(step=block.splitlines()[0]):
                self.assertIn("continue-on-error: true", block)

        for name in (
            "Pin one physical Android device",
            "Build ARM64 API-26 device APK",
            "Verify ARM64 API-26 device APK",
            "Install and run RMA-090 camera discovery acceptance",
            "Install and run RMA-091 camera acquisition acceptance",
            "Install and run RMA-092 camera texture acceptance",
            "Install and run RMA-111 lightweight tracking acceptance",
            "Install and run RMA-154 visual-servo acceptance",
            "Install and run RMA-022 lifecycle acceptance",
            "Install and run RMA-195 application-composition acceptance",
            "Install and run authoritative rendering acceptance",
        ):
            with self.subTest(acceptance=name):
                self.assertNotIn(
                    "continue-on-error: true",
                    workflow_step(workflow, name),
                )


if __name__ == "__main__":
    unittest.main()
