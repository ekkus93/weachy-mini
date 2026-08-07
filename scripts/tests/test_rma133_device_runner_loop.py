from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts" / "run_rma133_device_benchmark.sh"


class Rma133DeviceRunnerLoopTests(unittest.TestCase):
    def test_candidate_rows_are_materialized_before_adb_commands(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        self.assertIn(
            'mapfile -t candidate_rows < "${RESULTS_DIR}/candidate_rows.tsv"',
            source,
        )
        self.assertIn('for candidate_row in "${candidate_rows[@]}"; do', source)
        self.assertNotIn('done < "${RESULTS_DIR}/candidate_rows.tsv"', source)

    def test_adb_shell_commands_cannot_consume_runner_stdin(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        self.assertIn(
            'DEVICE_ABI="$("${ADB[@]}" shell getprop ro.product.cpu.abi </dev/null',
            source,
        )
        self.assertIn(
            '"${ADB[@]}" shell \\\n        "cd \'${REMOTE_ROOT}\' && LD_LIBRARY_PATH=.',
            source,
        )
        self.assertIn("</dev/null | tr -d '\\r' > \"${raw_path}\"", source)

    def test_all_frozen_candidate_reports_are_required_before_selection(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        self.assertIn("if (( ${#report_args[@]} != ${#candidate_rows[@]} * 2 )); then", source)
        self.assertIn("RMA-133 report count mismatch", source)


if __name__ == "__main__":
    unittest.main()
