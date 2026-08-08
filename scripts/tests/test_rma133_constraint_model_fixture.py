from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LOCK = ROOT / "third_party/llama-cpp-test-model.lock.json"


class Rma133ConstraintModelFixtureContracts(unittest.TestCase):
    def test_fixture_is_small_revision_pinned_and_not_a_product_model(self) -> None:
        lock = json.loads(LOCK.read_text(encoding="utf-8"))
        self.assertEqual(lock["schema_version"], 1)
        self.assertEqual(
            lock["source_revision"],
            "def3e2dd70df35ecbf6403ea347de4c5977220c1",
        )
        self.assertEqual(lock["filename"], "stories260K.gguf")
        self.assertEqual(lock["file_size_bytes"], 1_185_376)
        self.assertEqual(
            lock["sha256"],
            "047bf46455a544931cff6fef14d7910154c56afbc23ab1c5e56a72e69912c04b",
        )
        self.assertIn(lock["source_revision"], lock["url"])
        self.assertIn(lock["filename"], lock["url"])
        self.assertTrue(lock["url"].startswith("https://"))
        self.assertIn("not a product model", lock["purpose"])
        self.assertNotIn("models/manifests", lock["url"])

    def test_hosted_runner_is_hash_gated_and_has_no_fallback(self) -> None:
        script = (
            ROOT / "scripts/run_rma133_hosted_constraint_model_tests.sh"
        ).read_text(encoding="utf-8")
        self.assertIn("sha256sum", script)
        self.assertIn("expected_size", script)
        self.assertIn("--fail-with-body", script)
        self.assertIn("--proto '=https'", script)
        self.assertIn("reachy_llama_constraint_model_tests", script)
        lowered = script.lower()
        self.assertNotIn("fallback", lowered)
        self.assertNotIn("|| true", lowered)


if __name__ == "__main__":
    unittest.main()
