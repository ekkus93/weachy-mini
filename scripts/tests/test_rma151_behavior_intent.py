import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOR = ROOT / "Assets/ReachyMini/Runtime/Core/Behavior"
LOCAL_MODELS = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels"
SCHEMA = ROOT / "models/behavior/behavior-intent-v1.schema.json"
CORE_TEST = ROOT / "managed/ReachyMini.Core.Tests/Rma151BehaviorIntentContractTests.cs"
LOCAL_TEST = ROOT / "managed/ReachyMini.LocalLlm.Tests/Program.IntentParserTests.cs"


class Rma151BehaviorIntentTests(unittest.TestCase):
    def test_versioned_schema_is_strict_and_bounded(self) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        self.assertEqual(schema["$schema"], "https://json-schema.org/draft/2020-12/schema")
        self.assertEqual(schema["$id"], "urn:weachy-mini:schema:behavior-intent:v1")
        self.assertEqual(schema["type"], "object")
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(schema["maxProperties"], 7)
        self.assertEqual(schema["required"], ["schema_version"])

        properties = schema["properties"]
        self.assertEqual(properties["schema_version"]["const"], 1)
        self.assertEqual(properties["speech"]["minLength"], 1)
        self.assertEqual(properties["speech"]["maxLength"], 160)
        self.assertEqual(
            properties["expression"]["enum"],
            ["neutral", "attentive", "curious", "pleased", "concerned", "surprised"],
        )
        self.assertEqual(
            properties["gesture"]["enum"],
            ["none", "nod", "small_head_tilt", "recoil"],
        )
        self.assertEqual(properties["urgency"]["enum"], ["low", "normal", "high"])

        gaze_object = properties["gaze_target"]["oneOf"][1]
        self.assertFalse(gaze_object["additionalProperties"])
        self.assertEqual(gaze_object["maxProperties"], 2)
        self.assertEqual(gaze_object["properties"]["kind"]["const"], "tracked_entity")
        self.assertEqual(gaze_object["properties"]["entity_id"]["maxLength"], 64)
        self.assertEqual(gaze_object["properties"]["entity_id"]["pattern"], "^entity-[0-9]+$")

        timing = properties["timing"]
        self.assertFalse(timing["additionalProperties"])
        self.assertEqual(timing["minProperties"], 1)
        self.assertEqual(timing["maxProperties"], 2)
        self.assertEqual(timing["properties"]["start_delay_ms"]["minimum"], 0)
        self.assertEqual(timing["properties"]["start_delay_ms"]["maximum"], 5000)
        self.assertEqual(timing["properties"]["maximum_duration_ms"]["minimum"], 1)
        self.assertEqual(timing["properties"]["maximum_duration_ms"]["maximum"], 30000)

    def test_unsafe_actuator_fields_are_not_schema_actions(self) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        properties = schema["properties"]
        for unsafe in (
            "joint_angle",
            "motor_command",
            "torque",
            "velocity",
            "position",
            "coordinates",
            "raw_actuator",
        ):
            self.assertNotIn(unsafe, properties)

    def test_provider_neutral_source_set_is_complete(self) -> None:
        required = {
            BEHAVIOR / "ReachyBehaviorIntentContracts.cs": "class ReachyBehaviorIntent",
            BEHAVIOR / "ReachyBehaviorIntentJsonReader.cs": "class ReachyBehaviorIntentJsonReader",
            BEHAVIOR / "ReachyBehaviorIntentJsonParser.cs": "class ReachyBehaviorIntentJsonParser",
            CORE_TEST: "RegenerationRequiresExplicitRma146Authorization",
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_local_llm_uses_provider_neutral_intent_without_changing_frozen_prompt(self) -> None:
        contract_path = LOCAL_MODELS / "ReachyLocalLlmBehaviorContract.cs"
        contract = contract_path.read_text(encoding="utf-8")
        local_contracts = (LOCAL_MODELS / "ReachyLocalLlmContracts.cs").read_text(encoding="utf-8")
        local_tests = LOCAL_TEST.read_text(encoding="utf-8")

        self.assertIn("using ReachyMini.Behavior;", contract)
        self.assertIn("ReachyBehaviorIntentJsonParser.Validate(response)", contract)
        self.assertIn("ReachyBehaviorIntent? Intent", local_contracts)
        self.assertIn("ReachyBehaviorIntent", local_tests)
        prompt_hash = "0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20"
        self.assertIn("SystemPromptSha256 =", contract)
        self.assertIn(prompt_hash, contract)
        grammar_hash = "2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734"
        self.assertIn("GrammarSha256 =", contract)
        self.assertIn(grammar_hash, contract)

        obsolete_symbols = (
            "LocalLlmBehaviorIntent",
            "LocalLlmGazeTarget",
            "LocalLlmExpression",
            "LocalLlmGesture",
            "LocalLlmUrgency",
        )
        for symbol in obsolete_symbols:
            self.assertNotIn(symbol, local_contracts)
            self.assertNotIn(symbol, local_tests)

    def test_recovery_policy_never_returns_a_synthetic_intent(self) -> None:
        contracts = (BEHAVIOR / "ReachyBehaviorIntentContracts.cs").read_text(encoding="utf-8")
        self.assertIn("MaximumRegenerationAttempts = 1", contracts)
        self.assertIn("ReachyBehaviorIntentRecoveryAction.Regenerate", contracts)
        self.assertNotIn("ReachyBehaviorIntent? Intent { get; }", contracts.split(
            "public sealed class ReachyBehaviorIntentRecoveryDecision", 1
        )[1])


if __name__ == "__main__":
    unittest.main()
