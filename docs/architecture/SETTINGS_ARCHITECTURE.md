# Settings Architecture

## Scope

RMA-082 replaces the RMA-081 settings placeholder with a durable, truthful
settings system. It provides complete configuration surfaces for providers,
device-camera preferences, speech, local models, simulation, privacy, and
licenses while preserving the fixed robot presentation camera.

RMA-082 stores preferences and exposes unavailable actions. It does not claim
that later CameraX, speech, model-package, cloud-provider, perception, or
behavior runtimes are installed.

## Shared settings state

`ReachySettingsStateStore` is Unity-independent and publishes immutable,
revisioned `ReachySettingsSnapshot` values. The seven canonical sections are:

1. Providers
2. Camera
3. Speech
4. Local model
5. Simulation
6. Privacy
7. Licenses

The store owns user preferences and presentation-safe diagnostics. Runtime
services consume the store but do not bypass it with independent mutable
settings.

## Independent provider selection

ASR, TTS, LLM, and VLM use separate `ReachyProviderSelection` records. Each
record contains:

- provider kind;
- stable provider identifier;
- display name;
- execution class: unconfigured, on-device, Android service, or cloud;
- connectivity requirement;
- actual runtime availability;
- a human-readable status explanation.

A preference can be selected before its runtime is installed. Such a selection
remains explicitly unavailable and explains what is missing.

### Network-truthfulness invariant

`ReachyProviderSelection` rejects an Android-service or cloud selection unless
its connectivity is `NetworkRequired`. The UI therefore cannot represent a
network-backed Android service as offline. The privacy summary separately lists
every selected provider that may send data off device.

The top-level provider indicator is derived from all four selections:

- no configured preferences: unavailable;
- configured preferences that are entirely on-device: local;
- any Android-service or cloud preference: cloud/network-bound.

This is a location and privacy indicator, not a claim that the selected runtime
is available.

## Camera settings

Camera settings distinguish the observer-facing Reachy presentation camera
from future Android device cameras.

The Reachy presentation camera remains:

- fixed front/three-quarter;
- tagged as the active main camera;
- non-navigable;
- unaffected by every settings action.

RMA-082 stores a front/rear device-camera preference. Preview, calibration, and
reprojection-diagnostic controls are visible and actionable, but report their
later CameraX milestones until those capabilities are implemented.

## Speech settings

Speech settings store language and voice preferences. The speech network-status
summary is derived from:

- the selected ASR execution class;
- the selected TTS execution class;
- whether the selected voice preference requires a network service.

An Android-service or cloud speech preference always produces a visible
`Network required` status. On-device preferences remain unavailable until a
compatible local model is installed.

## Local-model settings

The local-model section exposes:

- installed model count;
- active model;
- install;
- import;
- select;
- delete;
- memory budget;
- context-token preference.

Resource preferences are durable. Package-management actions remain visible and
return explicit unavailable diagnostics until the Android installer/import
work is implemented.

## Simulation settings

The simulation section exposes:

- standard or high-fidelity preference;
- calibration-profile status;
- reset to neutral;
- authoritative runtime diagnostics.

Reset invokes `ReachyProductionAuthoritativeRuntime.ResetNeutral`; it does not
introduce a separate kinematic or presentation-layer reset. Diagnostics report
the authoritative runtime, simulation and renderer states, model hash, body
count, worker progress, and retained fault.

The calibration label remains `Uncalibrated` unless a later approved profile is
actually installed.

## Privacy settings

Privacy settings expose:

- a derived list of cloud/network-bound provider selections;
- history enabled/disabled;
- session-only or bounded retention preferences.

The summary is recomputed on every provider change. It cannot be manually set
to contradict the provider configuration.

## Licenses and attribution

The licenses section includes attribution references for:

- Weachy Mini project contributors;
- Pollen Robotics and the Reachy Mini model/identity;
- Google DeepMind and the MuJoCo runtime;
- Unity Technologies and Unity runtime notices.

The screen identifies the authoritative notice locations rather than embedding
or rewriting third-party license text.

## Durable persistence

`ReachySettingsPersistenceApplicationService` stores schema-versioned JSON at:

`Application.persistentDataPath/reachy-settings-v1.json`

Persistence behavior is fail visible:

- settings are sanitized when applied;
- unsupported enum values return to safe defaults;
- writes use temporary and backup files;
- unchanged durable state is not rewritten;
- malformed or unsupported files are moved to a timestamped `.corrupt-*`
  quarantine path;
- safe defaults are written after quarantine;
- the persistence service becomes `Degraded` with the source path, quarantine
  path, and error rather than silently discarding the failure.

Only durable user preferences are serialized. Runtime availability and transient
diagnostics are recomputed from current services after startup.

## Production composition

`ReachySettingsApplicationCompositionProvider` supplies all eight RMA-080
boundaries:

| Boundary | RMA-082 implementation | Initial health |
| --- | --- | --- |
| Simulation | Authoritative runtime adapter | Ready |
| Camera | Fixed presentation-camera service | Ready |
| Audio | Settings-aware speech boundary | Unavailable |
| Provider | Settings-aware ASR/TTS/LLM/VLM boundary | Unavailable |
| Perception | Explicit unavailable boundary | Unavailable |
| Behavior | Explicit unavailable boundary | Unavailable |
| Persistence | Durable settings service | Ready or Degraded |
| UI | Main screen plus seven settings sections | Ready |

The expected aggregate state remains `Degraded` until optional runtime
capabilities actually exist. Selecting a preference never upgrades an
unavailable service to ready.

## Validation boundary

The engine-independent settings contract is covered by warnings-as-errors
managed tests and a permanent hosted workflow. Unity edit-mode tests cover the
settings surface, fixed-camera preservation, persistence round-trip, corrupt
file quarantine, and Android-service network truthfulness.

Installed Android validation remains mandatory before RMA-082 completion. The
current `kawa` runner must have a valid Unity Editor license before that gate can
load the project, compile Unity assemblies, build the ARM64 APK, or run the
installed-device regression suites.
