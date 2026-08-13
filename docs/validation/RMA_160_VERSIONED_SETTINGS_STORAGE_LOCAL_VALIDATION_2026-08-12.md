# RMA-160 Versioned Settings Storage — Validation

**Task:** RMA-160 — Implement versioned settings storage  
**Implementation date:** 2026-08-12  
**Accepted:** 2026-08-13  
**Status:** Complete

## Scope implemented

RMA-160 adds:

- schema-2 general durable settings while retaining the historical settings filename for in-place migration;
- deterministic schema-1 to schema-2 migration;
- strict persisted-value validation instead of silent sanitization;
- stable selected provider-profile references for ASR/TTS/LLM/VLM;
- selected camera-calibration profile reference;
- selected model-manifest ID plus SHA-256 binding;
- selected device-profile reference without inventing device tuning constants;
- explicit corruption state (`RecoveryRequired`);
- timestamped quarantine without writing replacement defaults;
- persistence blocking while recovery is unresolved;
- explicit quarantine export, validated recovery import, and deliberate reset APIs;
- validated atomic-write backup recovery;
- a permanent Python architecture regression and Unity EditMode behavioral tests.

Existing specialized stores remain authoritative for full provider-profile metadata and camera-calibration content. Provider credential bytes remain outside settings persistence behind `IReachyProviderSecretStore`.

## Requirement audit

### Store the required non-secret settings

Accepted.

The schema preserves ASR/TTS/LLM/VLM execution selections, camera facing, selected speech language and voice, local-model memory/context settings, simulation fidelity, and bounded history/retention settings. Schema 2 additionally stores stable references for selected provider profiles, camera calibration, local-model manifest plus SHA-256, and device profile.

Full provider profiles, camera calibration payloads, and model manifests retain their existing specialized versioned stores. RMA-160 stores stable selection/binding identifiers instead of duplicating those documents.

### Add migrations with tests

Accepted.

Schema 1 is parsed and strictly validated, converted to the schema-2 representation, and rewritten atomically as schema 2. Schema 2 loads directly. Unsupported future schemas fail closed.

`ReachyVersionedSettingsPersistenceTests.SchemaOneMigratesInPlaceWithoutLosingDurableSettings` exercises preservation and rewrite behavior.

### Detect corruption and offer explicit recovery/export instead of silent reset

Accepted.

Invalid persistent state is quarantined and sets `RecoveryRequired`. The service does not create a replacement defaults file while recovery remains unresolved, and ordinary settings changes cannot bypass the persistence gate.

Recovery is explicit through quarantine export, validated recovery import, or deliberate reset. A validated atomic-write backup may be restored deterministically; an invalid backup is quarantined instead of trusted.

Malformed JSON, unsupported schemas, semantically invalid settings, quarantine/export, deliberate reset, and recovery import are covered by the managed test suite.

### Keep secret values separate

Accepted.

The general settings schema contains provider-profile identifiers and no API-key, access-token, credential-value, or secret-value fields. Provider credential bytes remain behind `IReachyProviderSecretStore`; provider-profile persistence may retain only validated secret references where required.

The permanent Python RMA-160 regression locks this separation and the continued ownership of provider profiles, camera calibration, and model manifests by their specialized stores.

## Local checks performed during implementation

The implementation sandbox did not provide `dotnet`, `csc`, `mcs`, `msbuild`, Unity, or Ruff, so those exact tools were not claimed as local evidence.

The new Python regression was checked locally with:

```text
python3 -m py_compile scripts/tests/test_rma160_versioned_settings_storage.py
python3 -m compileall -q scripts
```

Both completed successfully. A 100-column scan of the new Python regression was also clean before hosted Ruff validation became available.

No native simulation code changed in RMA-160, so native MuJoCo behavior was outside the changed-code validation surface.

## Managed tests

`Assets/ReachyMini/Tests/Editor/ReachyVersionedSettingsPersistenceTests.cs` covers:

1. schema-1 migration preserves settings and rewrites schema 2;
2. schema-2 stable reference round trip;
3. absence of secret-value fields in general settings export;
4. corrupt JSON requires explicit recovery and does not recreate defaults;
5. ordinary settings changes cannot bypass the recovery gate;
6. quarantined source export;
7. explicit reset after corruption;
8. unsupported future schema rejection;
9. semantically invalid persisted values fail closed instead of being sanitized;
10. recovery import is validated before persistent state is recreated.

The permanent static regression additionally locks the separation between general settings, provider-profile storage, provider secret references, camera-calibration storage, and the selected local-model manifest.

## Accepted hosted CI evidence

Source SHA before this documentation-only closeout: `1f2027fe2fed03efb406ec487cc69ddf9248b6d5`.

Hosted CI run `31671453979` completed successfully on that exact SHA. Its relevant jobs all passed:

- `static`: actionlint, Ruff lint, Ruff format, ShellCheck, and static repository checks;
- `managed`: warnings-as-errors build and native lifecycle tests;
- `android`: Android lint, Java warnings, and tests;
- `native`: warnings-as-errors and sanitizer builds/tests;
- `reachy-model`: pinned model/source validation.

This establishes that RMA-160 introduced no unresolved formatter, static-analysis, or managed compiler/analyzer failures.

## Accepted Unity evidence

Self-hosted Local Unity Android Validation run `31671453974`, job `94356741247`, executed the same exact source SHA `1f2027fe2fed03efb406ec487cc69ddf9248b6d5`.

The `Run Unity tests` step completed successfully before the workflow later encountered an unrelated device-lifecycle/evidence-stage failure.

Artifact `9169933024`, `local-unity-test-results-1f2027fe2fed03efb406ec487cc69ddf9248b6d5`, has digest:

`sha256:c6b172472f6af93d5890d74551bdea860420cd39b161e9df565a195aeb29e51a`

The artifact records:

- EditMode: **135/135 passed**, 0 failed, 0 skipped;
- PlayMode: **1/1 passed**, 0 failed, 0 skipped.

All six RMA-160 fixture methods passed:

- `CorruptionRequiresExplicitRecoveryAndNeverPersistsDefaults`;
- `RecoveryImportValidatesBeforeReplacingQuarantine`;
- `SchemaOneMigratesInPlaceWithoutLosingDurableSettings`;
- `SemanticallyInvalidSettingsFailClosedInsteadOfBeingSanitized`;
- `UnsupportedFutureSchemaFailsClosedIntoRecovery`;
- `VersionTwoRoundTripsOnlyStableNonSecretReferences`.

The workflow's overall failure does not block RMA-160 because it occurred after Unity tests and in an unrelated RMA-022 lifecycle/evidence stage. RMA-160 itself requires no physical-device acceptance gate.

## Closeout

All four RMA-160 TODO requirements are implemented and have passing failure-path, static, managed, and Unity evidence. No secret storage lifecycle claim is made here; Android Keystore credential lifecycle remains RMA-161.
