# RMA-160 Versioned Settings Storage — Local Validation

**Task:** RMA-160 — Implement versioned settings storage  
**Date:** 2026-08-12  
**Status:** Implementation checkpoint; managed/Unity execution pending

## Scope implemented

The RMA-160 checkpoint adds:

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

Existing specialized stores remain authoritative for full provider profile metadata and camera calibration content. Provider credential bytes remain outside settings persistence behind `IReachyProviderSecretStore`.

## Local checks available in this sandbox

The sandbox does not provide `dotnet`, `csc`, `mcs`, `msbuild`, or Unity, so the new C# EditMode tests cannot be executed locally here.

The newly added Python regression was copied verbatim into a local scratch tree and checked with:

```text
python3 -m py_compile scripts/tests/test_rma160_versioned_settings_storage.py
python3 -m compileall -q scripts
```

Both completed successfully.

Ruff is not installed in the sandbox and is not present in the local `uv` cache, so the exact repository Ruff executable could not be run offline. A 100-column scan of the new Python regression found no over-length lines.

No native simulation code changed in this checkpoint, so native MuJoCo behavior is outside the changed-code validation surface.

## Managed tests added

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

## Remaining validation before RMA-160 closeout

RMA-160 should remain open until the repository's managed/Unity and static CI gates execute these new tests successfully. No physical-device validation is required by the RMA-160 TODO itself; Android Keystore credential lifecycle remains RMA-161.
