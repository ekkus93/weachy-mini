from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


benchmark = ROOT / "native" / "llama_runtime" / "benchmark" / "rma133_benchmark.c"
scorer = ROOT / "scripts" / "score_rma133_benchmark.py"
tests = ROOT / "scripts" / "tests" / "test_rma133_benchmark_contract.py"

hex_helper = r'''static void json_hex_bytes(FILE * output, const char * bytes, size_t byte_count)
{
    static const char hex_digits[] = "0123456789abcdef";
    fputc('"', output);
    for (size_t index = 0U; index < byte_count; ++index)
    {
        const unsigned int value = (unsigned int)(unsigned char)bytes[index];
        fputc((int)hex_digits[value >> 4U], output);
        fputc((int)hex_digits[value & 0x0fU], output);
    }
    fputc('"', output);
}

'''
replace_once(benchmark, "static void init_error(", hex_helper + "static void init_error(")
replace_once(
    benchmark,
    ',\\"battery_temp_before_c\\":%.3f,\\"battery_temp_c\\":%.3f,\\"response\\\":",',
    ',\\"battery_temp_before_c\\":%.3f,\\"battery_temp_c\\":%.3f,\\"response_bytes_hex\\\":",',
)
replace_once(
    benchmark,
    "    json_string(stdout, response);\n",
    "    json_hex_bytes(stdout, response, response_length);\n",
)

response_decoder = r'''
def _response_text_from_record(record: dict[str, Any]) -> tuple[str | None, list[str]]:
    response_hex = record.get("response_bytes_hex")
    if response_hex is not None:
        if not isinstance(response_hex, str) or len(response_hex) % 2 != 0:
            return None, ["benchmark response byte encoding is invalid"]
        try:
            response_bytes = bytes.fromhex(response_hex)
        except ValueError:
            return None, ["benchmark response byte encoding is invalid"]
        try:
            return response_bytes.decode("utf-8"), []
        except UnicodeDecodeError:
            return None, ["response is not valid UTF-8"]

    response = record.get("response")
    if not isinstance(response, str):
        return None, ["benchmark record has no response bytes"]
    return response, []


'''
replace_once(
    scorer,
    "def _score_case(expectation: CaseExpectation, record: dict[str, Any]) -> dict[str, Any]:\n",
    response_decoder
    + "def _score_case(expectation: CaseExpectation, record: dict[str, Any]) -> dict[str, Any]:\n",
)
replace_once(
    scorer,
    '''    response = record.get("response")
    if not isinstance(response, str):
        return {
            "case_id": expectation.case_id,
            "schema_valid": False,
            "semantic_score": 0.0,
            "reasons": ["benchmark record has no response string"],
        }

''',
    '''    response, response_reasons = _response_text_from_record(record)
    if response is None:
        return {
            "case_id": expectation.case_id,
            "schema_valid": False,
            "semantic_score": 0.0,
            "reasons": response_reasons,
        }

''',
)

replace_once(
    tests,
    "        response_override: dict[str, str] | None = None,\n",
    "        response_override: dict[str, str] | None = None,\n"
    "        response_bytes_override: dict[str, bytes] | None = None,\n",
)
replace_once(
    tests,
    '''            if response_override and case.case_id in response_override:
                response = response_override[case.case_id]
            records.append(
''',
    '''            if response_override and case.case_id in response_override:
                response = response_override[case.case_id]
            response_bytes = response.encode("utf-8")
            if response_bytes_override and case.case_id in response_bytes_override:
                response_bytes = response_bytes_override[case.case_id]
            records.append(
''',
)
replace_once(
    tests,
    '                    "response": response,\n',
    '                    "response_bytes_hex": response_bytes.hex(),\n',
)

invalid_utf8_test = r'''    def test_invalid_utf8_response_fails_schema_gate_without_crashing_scorer(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            candidate_id = self.config["candidates"][0]["candidate_id"]
            case_id = self.cases[0].case_id
            raw = self._write_raw(
                directory,
                candidate_id,
                response_bytes_override={case_id: b"{\"speech\":\"\xf0\x9f"},
            )
            report = scorer.score_candidate(
                config_path=CONFIG,
                cases_path=CASES,
                raw_path=raw,
                candidate_id=candidate_id,
                output_path=directory / "report.json",
            )
        score = next(item for item in report["case_scores"] if item["case_id"] == case_id)
        self.assertFalse(score["schema_valid"])
        self.assertEqual(score["semantic_score"], 0.0)
        self.assertIn("response is not valid UTF-8", score["reasons"])
        self.assertFalse(report["eligible"])

'''
replace_once(
    tests,
    "    def test_invented_gaze_entity_reduces_quality(self) -> None:\n",
    invalid_utf8_test + "    def test_invented_gaze_entity_reduces_quality(self) -> None:\n",
)
replace_once(
    tests,
    '        self.assertIn("thermal safety", source.casefold())\n',
    '        self.assertIn("thermal safety", source.casefold())\n'
    '        self.assertIn("response_bytes_hex", source)\n'
    '        self.assertIn("json_hex_bytes(stdout, response, response_length);", source)\n',
)

print("RMA-133 UTF-8 evidence repair applied")
