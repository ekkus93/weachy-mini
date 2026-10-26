# RMA-173 Silent-Failure Regression Contracts — Local Validation 2026-09-25

## Scope

This note records local sandbox validation for the RMA-173 silent-failure regression contract branch.

Branch under validation:

- Repository: `ekkus93/weachy-mini`
- Branch: `ralph/rma173-silent-failure-contracts`
- Validated branch head before this note: `832ce7bf5b84121ddfddd2b7daacb79b5d1ab4ce`
- Base `master` observed in the Ralph loop: `b188ce3b18635d24da79cb762e245c52e361ab5e`

## Local command

The current uploaded master snapshot was unpacked into the sandbox, and the branch's added test file was applied exactly as inspected from the Ralph diff. The focused command was:

```bash
python3 -m pytest -q scripts/tests/test_rma173_silent_failure_contracts.py
```

Result:

```text
........                                                                 [100%]
8 passed in 0.07s
```

## Contract coverage

The test file `scripts/tests/test_rma173_silent_failure_contracts.py` covers all eight RMA-173 roadmap bullets:

1. on-device ASR failure does not invoke cloud ASR;
2. offline TTS rejects network voices before synthesis;
3. local LLM failure does not invoke the cloud LLM path;
4. invalid camera pixels require validity-mask evidence and stale texture identities are rejected;
5. solver failure remains visible and does not switch to Unity animation fallback;
6. calibration mismatch does not load placeholders as calibrated;
7. dropped command/state buffers produce visible diagnostics;
8. malformed LLM/VLM provider responses cannot become fake successful intents.

## CI status boundary

The branch's exact-head repository CI run `36184903053` failed with the same zero-step `JOB_NOT_FOUND` infrastructure pattern affecting other ready branches in this repository. Ralph reported no failed steps and no log evidence for the `reachy-model`, `static`, `native`, `managed`, or `android` jobs.

This local validation therefore supports the branch's focused static contract content, but it does not claim repository CI passed. Merge remains blocked by the repository clean-mergeability infrastructure gate until the zero-step CI issue is resolved or the branch is requalified successfully.
