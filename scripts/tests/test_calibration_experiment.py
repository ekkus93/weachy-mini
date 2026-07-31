"""RMA-072 calibration experiment runner contract tests."""

from __future__ import annotations

import copy
import importlib.util
import json
import sys
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/calibration_experiment.py"
spec = importlib.util.spec_from_file_location("calibration_experiment", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load calibration_experiment")
experiment = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = experiment
spec.loader.exec_module(experiment)


class FakeAdapter:
    def __init__(self, robot_id: str = "synthetic-reachy-mini-ci") -> None:
        self._robot_id = robot_id
        self.events: list[tuple[str, object]] = []
        self.state = experiment.SafetyState(
            bus_voltage_v=5.0,
            maximum_temperature_c=25.0,
            total_current_a=0.2,
            emergency_stop_available=True,
            faulted=False,
        )
        self.fail_on_command = False

    def robot_id(self) -> str:
        return self._robot_id

    def begin_run(self, manifest: dict[str, object]) -> None:
        self.events.append(("begin", manifest))

    def read_safety_state(self) -> experiment.SafetyState:
        return self.state

    def record_marker(self, marker: str, experiment_id: str) -> None:
        self.events.append(("marker", (experiment_id, marker)))

    def set_torque(self, actuator_id: str, enabled: bool) -> None:
        self.events.append(("torque", (actuator_id, enabled)))

    def submit_command(self, actuator_id: str, command: dict[str, object]) -> None:
        if self.fail_on_command:
            raise RuntimeError("adapter command failure")
        self.events.append(("command", (actuator_id, command)))

    def emergency_stop(self, reason: str) -> None:
        self.events.append(("emergency_stop", reason))

    def end_run(self, outcome: str) -> None:
        self.events.append(("end", outcome))


class CalibrationExperimentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.fixture_path = ROOT / "calibration/experiments/rma072-smoke-plan.json"
        cls.plan = experiment.load_plan_file(cls.fixture_path)
        cls.schedule = experiment.compile_plan(cls.plan)

    def authorization(self, **overrides: object) -> experiment.ExecutionAuthorization:
        values: dict[str, object] = {
            "plan_sha256": self.schedule.plan_sha256,
            "robot_id": "synthetic-reachy-mini-ci",
            "acknowledgement": experiment.EXECUTION_ACKNOWLEDGEMENT,
            "operator_present": True,
            "emergency_stop_verified": True,
            "workspace_clear": True,
            "allow_physical_motion": True,
        }
        values.update(overrides)
        return experiment.ExecutionAuthorization(**values)

    def test_fixture_validates_and_covers_every_required_experiment(self) -> None:
        summary = experiment.validate_plan(self.plan)
        self.assertEqual(summary["status"], "ok")
        self.assertEqual(set(summary["experiment_types"]), experiment.EXPERIMENT_TYPES)
        self.assertEqual(summary["experiment_count"], 8)

    def test_compilation_is_deterministic(self) -> None:
        again = experiment.compile_plan(copy.deepcopy(self.plan))
        self.assertEqual(again.schedule_sha256, self.schedule.schedule_sha256)
        self.assertEqual(
            [action.to_document() for action in again.actions],
            [action.to_document() for action in self.schedule.actions],
        )

    def test_free_decay_disables_torque_and_run_ends_torque_disabled(self) -> None:
        free_decay_actions = [
            action
            for action in self.schedule.actions
            if action.experiment_id == "antenna-free-decay"
        ]
        self.assertTrue(
            any(
                action.action == "torque" and action.payload == {"enabled": False}
                for action in free_decay_actions
            )
        )
        final_torque = [
            action
            for action in self.schedule.actions
            if action.experiment_id == "run" and action.action == "torque"
        ]
        self.assertEqual(
            {action.actuator_id for action in final_torque},
            set(self.plan["actuators"]),
        )
        self.assertTrue(all(action.payload == {"enabled": False} for action in final_torque))

    def test_command_jsonl_is_monotonic_and_rma070_shaped(self) -> None:
        records = [
            json.loads(line)
            for line in experiment.command_jsonl_bytes(self.schedule).decode("utf-8").splitlines()
        ]
        self.assertGreater(len(records), 0)
        previous_timestamp = -1
        previous_sequence = 0
        for record in records:
            self.assertEqual(record["stream_id"], "experiment_commands")
            self.assertEqual(record["sample_type"], "command")
            self.assertEqual(record["clock_id"], "reachy_clock")
            sample = record["sample"]
            self.assertGreaterEqual(sample["timestamp_ns"], previous_timestamp)
            self.assertEqual(sample["sequence"], previous_sequence + 1)
            previous_timestamp = sample["timestamp_ns"]
            previous_sequence = sample["sequence"]

    def test_duplicate_json_keys_fail_closed(self) -> None:
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "duplicate key"):
            experiment.strict_json_loads('{"plan_id":"a","plan_id":"b"}')

    def test_schema_hash_tampering_fails_closed(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["schema"]["schema_sha256"] = "0" * 64
        changed = experiment.finalize_plan(changed)
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "pinned"):
            experiment.validate_plan(changed)

    def test_plan_content_tampering_is_detected(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["experiments"][0]["end_position_rad"] = 0.3
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "canonical plan"):
            experiment.validate_plan(changed)

    def test_out_of_limit_command_is_rejected(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["experiments"][0]["end_position_rad"] = 2.0
        changed = experiment.finalize_plan(changed)
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "outside soft limits"):
            experiment.validate_plan(changed)

    def test_frequency_above_sampling_contract_is_rejected(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["experiments"][3]["frequencies_hz"] = [6.0]
        changed = experiment.finalize_plan(changed)
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "must be <="):
            experiment.validate_plan(changed)

    def test_schedule_action_ceiling_is_enforced(self) -> None:
        changed = copy.deepcopy(self.plan)
        changed["safety_limits"]["maximum_schedule_actions"] = 10
        changed = experiment.finalize_plan(changed)
        with self.assertRaisesRegex(experiment.ExperimentValidationError, "action count"):
            experiment.validate_plan(changed)

    def test_physical_execution_requires_exact_authorization(self) -> None:
        adapter = FakeAdapter()
        with self.assertRaisesRegex(experiment.ExperimentExecutionError, "acknowledgement"):
            experiment.execute_schedule(
                self.plan,
                self.schedule,
                adapter,
                self.authorization(acknowledgement="yes"),
                now_ns=lambda: 0,
                sleep_until_ns=lambda _: None,
                created_utc="2026-07-31T21:00:00Z",
            )
        self.assertEqual(adapter.events, [])

    def test_physical_execution_requires_exact_robot_identity(self) -> None:
        adapter = FakeAdapter(robot_id="different-robot")
        with self.assertRaisesRegex(experiment.ExperimentExecutionError, "robot IDs"):
            experiment.execute_schedule(
                self.plan,
                self.schedule,
                adapter,
                self.authorization(),
                now_ns=lambda: 0,
                sleep_until_ns=lambda _: None,
                created_utc="2026-07-31T21:00:00Z",
            )
        self.assertEqual(adapter.events, [])

    def test_valid_fake_execution_completes_and_preserves_safe_shutdown(self) -> None:
        adapter = FakeAdapter()
        experiment.execute_schedule(
            self.plan,
            self.schedule,
            adapter,
            self.authorization(),
            now_ns=lambda: 0,
            sleep_until_ns=lambda _: None,
            created_utc="2026-07-31T21:00:00Z",
        )
        self.assertEqual(adapter.events[0][0], "begin")
        self.assertEqual(adapter.events[-1], ("end", "completed"))
        self.assertFalse(any(event[0] == "emergency_stop" for event in adapter.events))
        final_torque_events = [event for event in adapter.events if event[0] == "torque"][-3:]
        self.assertEqual(
            {payload[0] for _, payload in final_torque_events},
            set(self.plan["actuators"]),
        )
        self.assertTrue(all(payload[1] is False for _, payload in final_torque_events))

    def test_safety_violation_aborts_and_emergency_stops(self) -> None:
        adapter = FakeAdapter()
        adapter.state = replace(adapter.state, maximum_temperature_c=80.0)
        with self.assertRaisesRegex(experiment.ExperimentExecutionError, "temperature"):
            experiment.execute_schedule(
                self.plan,
                self.schedule,
                adapter,
                self.authorization(),
                now_ns=lambda: 0,
                sleep_until_ns=lambda _: None,
                created_utc="2026-07-31T21:00:00Z",
            )
        self.assertTrue(any(event[0] == "emergency_stop" for event in adapter.events))
        self.assertIn(("end", "aborted"), adapter.events)

    def test_adapter_failure_aborts_and_emergency_stops(self) -> None:
        adapter = FakeAdapter()
        adapter.fail_on_command = True
        with self.assertRaisesRegex(experiment.ExperimentExecutionError, "adapter command failure"):
            experiment.execute_schedule(
                self.plan,
                self.schedule,
                adapter,
                self.authorization(),
                now_ns=lambda: 0,
                sleep_until_ns=lambda _: None,
                created_utc="2026-07-31T21:00:00Z",
            )
        self.assertTrue(any(event[0] == "emergency_stop" for event in adapter.events))
        self.assertIn(("end", "aborted"), adapter.events)

    def test_schema_descriptor_rejects_unversioned_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            schema_root = Path(temporary)
            original = ROOT / "calibration/schemas/calibration-experiment-plan-v1.schema.json"
            (schema_root / original.name).write_bytes(original.read_bytes() + b" ")
            with self.assertRaisesRegex(experiment.ExperimentValidationError, "schema drifted"):
                experiment.schema_descriptor(schema_root)


if __name__ == "__main__":
    unittest.main()
