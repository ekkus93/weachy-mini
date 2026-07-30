"""Tests for the machine-readable Reachy model parameter audit."""

from __future__ import annotations

import copy
import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = ROOT / "scripts" / "validate_model_parameter_audit.py"
AUDIT_PATH = ROOT / "models" / "reachy-mini" / "model-parameter-audit.json"
LOCK_PATH = ROOT / "third_party" / "reachy-mini-source.lock.json"
BASELINE_PATH = ROOT / "models" / "reachy-mini" / "model-baseline.json"


class ModelParameterAuditTests(unittest.TestCase):
    """Exercise audit policy, source matching, and visible drift failures."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.audit = json.loads(AUDIT_PATH.read_text(encoding="utf-8"))
        self.lock = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
        self.baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))

    @staticmethod
    def numeric_attribute(values: list[float]) -> str:
        """Serialize numeric source values without changing their meaning."""
        return " ".join(str(value) for value in values)

    def fixture_model(self, audit: dict[str, Any]) -> bytes:
        """Build a compact MJCF carrying every audited source parameter."""
        uncertainties = {
            (entry["scope"], entry["id"]): entry["comment"]
            for entry in audit["source_uncertainties"]
        }
        lines = [
            '<?xml version="1.0"?>',
            f"<!-- {uncertainties[('collision_mesh_selection', 'collision_meshes')]} -->",
        ]
        lines.extend(
            [
                '<mujoco model="audit_fixture">',
                "  <default>",
            ]
        )
        for model in audit["actuator_models"]:
            source_class = model["source_default_class"]
            lines.append(f'    <default class="{source_class}">')
            source_comment = uncertainties.get(("actuator_model", model["id"]))
            if source_comment is not None:
                lines.append(f"      <!-- {source_comment} -->")
            joint = model["joint"]
            lines.append(
                "      <joint "
                f'damping="{joint["damping"]}" '
                f'frictionloss="{joint["frictionloss"]}" '
                f'armature="{joint["armature"]}"/>'
            )
            position = model["position"]
            lines.append(
                "      <position "
                f'kp="{position["kp"]}" '
                f'kv="{position["kv"]}" '
                f'forcerange="{self.numeric_attribute(position["forcerange"])}"/>'
            )
            if model["id"] == "chosen_actuator":
                lines.append('      <default class="chosen_actuator"/>')
            lines.append("    </default>")
        equality = audit["equality_solver"]
        lines.extend(
            [
                "  </default>",
                "  <default>",
                "    <equality "
                f'solref="{self.numeric_attribute(equality["solref"])}" '
                f'solimp="{self.numeric_attribute(equality["solimp"])}"/>',
                "  </default>",
                "  <worldbody>",
                '    <body name="fixture_body">',
            ]
        )
        for joint in audit["joints"]:
            attributes = [
                f'name="{joint["name"]}"',
                f'type="{joint["type"]}"',
            ]
            if joint["range_radians"] is not None:
                attributes.append(f'range="{self.numeric_attribute(joint["range_radians"])}"')
            lines.append(f"      <joint {' '.join(attributes)}/>")
        lines.extend(
            [
                "    </body>",
                "  </worldbody>",
                "  <actuator>",
            ]
        )
        for actuator in audit["actuators"]:
            lines.append(
                "    <position "
                f'class="{actuator["source_class"]}" '
                f'name="{actuator["name"]}" '
                f'joint="{actuator["joint"]}"/>'
            )
        lines.extend(["  </actuator>", "  <equality>"])
        for index in range(equality["count"]):
            lines.append(f'    <connect site1="left_{index}" site2="right_{index}"/>')
        lines.extend(["  </equality>", "</mujoco>"])
        return ("\n".join(lines) + "\n").encode()

    def write_json(self, name: str, value: dict[str, Any]) -> Path:
        """Write deterministic temporary JSON."""
        path = self.root / name
        path.write_text(
            json.dumps(value, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        return path

    def run_validator(
        self,
        audit: dict[str, Any],
        baseline: dict[str, Any],
        model_bytes: bytes | None = None,
    ) -> subprocess.CompletedProcess[str]:
        """Run the public CLI against temporary evidence files."""
        if model_bytes is not None:
            model_sha = hashlib.sha256(model_bytes).hexdigest()
            audit["source"]["model_sha256"] = model_sha
            audit["diagnostics"]["source_model_sha256"] = model_sha
            audit["joint_limit_provenance"]["source_model_sha256"] = model_sha
            baseline["source"]["model_sha256"] = model_sha
        audit_path = self.write_json("audit.json", audit)
        baseline_path = self.write_json("baseline.json", baseline)
        arguments = [
            "python3",
            str(VALIDATOR),
            "--audit",
            str(audit_path),
            "--lock",
            str(LOCK_PATH),
            "--baseline",
            str(baseline_path),
        ]
        if model_bytes is not None:
            model_path = self.root / "model.xml"
            model_path.write_bytes(model_bytes)
            arguments.extend(["--model", str(model_path)])
        return subprocess.run(
            arguments,
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )

    def test_repository_static_audit_passes(self) -> None:
        """The committed audit must match the lock and existing model baseline."""
        result = subprocess.run(
            [
                "python3",
                str(VALIDATOR),
                "--audit",
                str(AUDIT_PATH),
                "--lock",
                str(LOCK_PATH),
                "--baseline",
                str(BASELINE_PATH),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr)

    def test_placeholder_parameters_cannot_be_labeled_calibrated(self) -> None:
        """A calibrated label must fail while active placeholder dynamics remain."""
        audit = copy.deepcopy(self.audit)
        audit["fidelity"]["calibrated"] = True
        audit["fidelity"]["may_be_labeled_calibrated"] = True
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("placeholder parameters", result.stderr)

    def test_complete_model_fixture_matches_audit(self) -> None:
        """The model validator must accept all recorded ranges, defaults, and notes."""
        audit = copy.deepcopy(self.audit)
        model_bytes = self.fixture_model(audit)
        result = self.run_validator(audit, copy.deepcopy(self.baseline), model_bytes)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_unrecorded_antenna_range_is_rejected(self) -> None:
        """A new source range must fail until its provenance classification is updated."""
        audit = copy.deepcopy(self.audit)
        model_text = (
            self.fixture_model(audit)
            .decode()
            .replace(
                '<joint name="right_antenna" type="hinge"/>',
                '<joint name="right_antenna" type="hinge" range="-1 1"/>',
                1,
            )
        )
        result = self.run_validator(
            audit,
            copy.deepcopy(self.baseline),
            model_text.encode(),
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("gained a range", result.stderr)

    def test_missing_parameter_group_classification_fails(self) -> None:
        """Every audited parameter group must carry its evidence classification."""
        audit = copy.deepcopy(self.audit)
        del audit["parameter_groups"][0]["classification"]
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("classification must be", result.stderr)

    def test_absent_measured_evidence_cannot_be_claimed(self) -> None:
        """An empty measured-evidence category cannot support a measured label."""
        audit = copy.deepcopy(self.audit)
        audit["parameter_groups"][0]["classification"] = "measured"
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("classification must be cad_derived", result.stderr)

    def test_joint_limit_provenance_drift_fails(self) -> None:
        """Joint ranges must stay bound to the exact pinned source identity."""
        audit = copy.deepcopy(self.audit)
        audit["joint_limit_provenance"]["source_commit"] = "0" * 40
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Joint-limit provenance", result.stderr)

    def test_joint_range_provenance_id_is_required(self) -> None:
        """Each joint must point to the policy that explains its encoded range."""
        audit = copy.deepcopy(self.audit)
        del audit["joints"][0]["range_provenance_id"]
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("range provenance", result.stderr)

    def test_uncertainty_comment_cannot_be_reassigned(self) -> None:
        """Upstream uncertainty text must remain bound to its exact actuator class."""
        audit = copy.deepcopy(self.audit)
        first = audit["source_uncertainties"][0]
        second = audit["source_uncertainties"][1]
        first["comment"], second["comment"] = second["comment"], first["comment"]
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Source uncertainty comment differs", result.stderr)

    def test_diagnostics_payload_must_match_fidelity(self) -> None:
        """Future diagnostics UI data cannot silently contradict the audit."""
        audit = copy.deepcopy(self.audit)
        audit["diagnostics"]["calibrated"] = True
        result = self.run_validator(audit, copy.deepcopy(self.baseline))
        self.assertNotEqual(0, result.returncode)
        self.assertIn("diagnostics payload differs", result.stderr)

    def test_source_comment_must_remain_inside_matching_actuator_class(self) -> None:
        """A comment elsewhere in the MJCF cannot satisfy model-specific evidence."""
        audit = copy.deepcopy(self.audit)
        model_text = self.fixture_model(audit).decode()
        comment = audit["actuator_models"][1]["source_comment"]
        model_text = model_text.replace(f"      <!-- {comment} -->\n", "", 1)
        model_text = model_text.replace("<mujoco model=", f"<!-- {comment} -->\n<mujoco model=", 1)
        result = self.run_validator(
            audit,
            copy.deepcopy(self.baseline),
            model_text.encode(),
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("not bound to actuator class", result.stderr)


if __name__ == "__main__":
    unittest.main()
