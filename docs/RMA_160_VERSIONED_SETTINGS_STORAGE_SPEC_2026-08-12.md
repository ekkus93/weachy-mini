# RMA-160 Versioned Settings Storage

## Status

**Complete (2026-08-13).** Accepted validation evidence is recorded in `docs/validation/RMA_160_VERSIONED_SETTINGS_STORAGE_LOCAL_VALIDATION_2026-08-12.md`.

This document extends the existing RMA-082 settings UI, RMA-100 camera-calibration persistence, RMA-131 model-manifest contract, and RMA-140 provider-profile/secret separation. It does not replace those specialized stores.

## Storage ownership

RMA-160 keeps persistent data separated by sensitivity and ownership:

- `reachy-settings-v1.json` is the general non-secret settings document. Its internal schema is now version 2; the historical filename is retained so existing installations are migrated in place instead of losing settings because a new filename was introduced.
- `reachy-provider-profiles-v1.json` remains the RMA-140 non-secret provider-profile store. Credential values are never stored there; only validated secret references may point to the Android-backed secret store.
- `reachy-camera-calibration-v1.json` remains the RMA-100 versioned calibration store. General settings retain only the selected calibration profile identifier.
- Local-model manifests remain immutable machine-readable artifacts under `models/manifests/`. General settings retain only the selected manifest identifier and SHA-256 binding.
- Device-profile ownership remains with the resource/thermal policy. General settings retain only a stable device-profile identifier; RMA-160 does not invent thermal or performance constants.

## General settings schema

Schema 2 preserves every schema-1 field:

- ASR/TTS/LLM/VLM execution selections;
- preferred camera facing;
- selected speech language and voice;
- local-model memory budget and context length;
- simulation fidelity mode;
- conversation-history enablement and retention period.

Schema 2 additionally stores stable references for:

- selected ASR/TTS/LLM/VLM provider profile IDs;
- selected camera-calibration profile ID;
- selected local-model manifest ID and SHA-256;
- selected device-profile ID.

The reference type accepts identifiers, not arbitrary opaque values. The manifest digest is either empty or exactly 64 hexadecimal characters. There are no API-key, access-token, credential-value, or secret-value fields in the general settings document.

## Migration contract

`ReachySettingsPersistenceApplicationService` reads a schema header before deserializing the corresponding document shape.

- Schema 1 is accepted, strictly validated, converted to the schema-2 in-memory representation, and atomically rewritten as schema 2.
- Schema 2 is accepted directly.
- Unknown/future schema versions fail closed.
- Persisted values are validated against the exact supported provider modes, camera/fidelity enums, language/voice choices, memory budgets, context lengths, and retention periods. Invalid values are corruption; they are not silently sanitized to defaults.

## Corruption and recovery contract

A corrupt general settings file is moved to a timestamped quarantine path. The application exposes `RecoveryRequired` and remains degraded.

While recovery is required:

- no replacement defaults file is automatically written;
- ordinary in-memory settings changes do not bypass the persistence gate;
- durable-reference changes are rejected;
- the quarantined source can be explicitly exported;
- a user-selected recovery document can be validated and imported;
- an explicit reset can create a fresh schema-2 defaults document.

A valid `.bak` created by the atomic-write path may be used automatically because it is fully parsed and validated before restoration. An invalid backup is quarantined rather than trusted.

The quarantined source is retained after explicit reset so recovery evidence is not destroyed.

## Secret boundary

RMA-160 never persists secret values. Provider profiles may persist a `credentialReference` or a `SecretReference` header binding, but the referenced bytes remain behind `IReachyProviderSecretStore`. Sensitive HTTP headers are already prohibited from using persisted literal values by the provider configuration contract.

RMA-161 owns credential lifecycle and Android Keystore behavior. RMA-160 only preserves the reference boundary required to select non-secret provider configuration without embedding credentials in settings JSON.

## Failure policy

There is no fallback from corrupt persistent state to an apparently healthy persisted default. Missing first-run storage may be initialized with defaults; corrupt existing storage requires visible recovery. This distinction is intentional and permanent.
