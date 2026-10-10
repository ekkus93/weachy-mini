#!/usr/bin/env python3
"""Fail closed when the RMA-134/135 Android acceptance steps are skipped."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = (
    ROOT / ".github/workflows/rma134-local-llm-android.yml",
    ROOT / ".github/workflows/rma135-resource-governor-android.yml",
)


class PhysicalStatusTests(unittest.TestCase):
    def test_rma135_baseline_requires_actual_evidence(self) -> None:
        text = WORKFLOWS[1].read_text(encoding="utf-8")
        self.assertIn("--json databaseId", text)
        self.assertIn("/actions/runs/${run_id}/artifacts", text)
        self.assertIn("select(.expired == false)", text)
        self.assertIn("rma135-resource-governor-android-", text)
        self.assertIn('[[ "${candidate}" =~ ^[0-9a-f]{40}$', text)
        self.assertIn('git merge-base --is-ancestor "${candidate}" "${SHA}"', text)
        self.assertNotIn("--json headSha", text)

    def test_rma134_can_rebuild_when_artifact_transport_is_unavailable(self) -> None:
        text = WORKFLOWS[0].read_text(encoding="utf-8")
        self.assertIn("Exact-SHA APK artifact unavailable", text)
        self.assertIn("prepare_reachy_unity_presentation.sh", text)
        self.assertIn("stage_reachy_unity_android_runtime.sh", text)
        self.assertIn("build_unity_android.sh device-feasibility", text)
        upload = text.split("- name: Upload RMA-134 physical evidence", 1)[1]
        upload_header = upload.split("uses: actions/upload-artifact@v4", 1)[0]
        self.assertIn("continue-on-error: true", upload_header)

    def test_rma135_hashed_paths_are_real_files(self) -> None:
        source = (
            ROOT / ".github/workflows/rma135-resource-thermal-governor.yml"
        ).read_text(encoding="utf-8")
        section = source.split("- name: Create exact-SHA evidence", 1)[1]
        section = section.split('> "$OUT/source-sha256.txt"', 1)[0]
        paths = [
            line.strip().removesuffix("\\").strip()
            for line in section.splitlines()
            if line.strip().startswith(
                ("Assets/", "managed/", "scripts/", "docs/", ".github/")
            )
        ]
        self.assertGreaterEqual(len(paths), 25, "RMA-135 evidence sources truncated")
        self.assertEqual(len(paths), len(set(paths)), "duplicate evidence paths")
        missing = [path for path in paths if not (ROOT / path).is_file()]
        self.assertEqual([], missing, "RMA-135 evidence includes missing files")

    def test_skip_never_marks_physical_accepted(self) -> None:
        for workflow in WORKFLOWS:
            with self.subTest(workflow=workflow.name):
                source = workflow.read_text(encoding="utf-8")
                self.assertIn("const scope = process.env.RELEVANT;", source)
                self.assertIn("const finalState =", source)
                self.assertIn('!relevant ? "pending" : succeeded ? "success" : "failure"', source)
                self.assertIn("Physical acceptance skipped; exact-head evidence pending", source)
                self.assertIn("Source-scope check unavailable", source)
                self.assertIn("state: finalState,", source)
                self.assertNotIn('state: succeeded ? "success" : "failure"', source)


if __name__ == "__main__":
    unittest.main()
