import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/local-unity-android-validation.yml"


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
        self.assertNotIn("continue-on-error: true", workflow)


if __name__ == "__main__":
    unittest.main()
