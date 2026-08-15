# RMA-171 Diagnostics Screen

## Scope

RMA-171 replaces the main screen's opaque diagnostics string with a typed,
read-only snapshot. The screen is an observer; it does not own simulation, camera,
provider, calibration, or resource-governor state.

## Snapshot contract

The screen always presents six sections:

1. Simulation
2. Rendering
3. Camera
4. Providers
5. Versions
6. Device

Every metric is explicitly `Available`, `Degraded`, or `Unavailable`. Degraded and
unavailable metrics require a reason. Missing telemetry is never represented by a
fabricated zero, 100%, or healthy value.

## Sources

- Simulation timing comes from the authoritative production worker snapshot.
  Observed physics frequency is calculated from monotonic step-count deltas;
  target frequency comes from the pinned physics timestep.
- Rendering uses authoritative renderer status, Unity frame timing, Unity allocated
  memory, and existing RMA-135 Android thermal/resource telemetry.
- Camera FPS uses accepted acquisition-frame deltas and active camera state.
  Camera discovery is shown independently.
- Provider rows come from durable ASR/TTS/LLM/VLM selections and include execution
  location/connectivity status.
- Version rows expose simulation model identity, selected local model/calibration,
  native ABI, the authoritative MuJoCo version requirement, Reachy source-model
  hash, and app version.
- Device rows use Unity device/OS/graphics information and the same memory/CPU
  inputs used to select the local-LLM resource profile.

The current application composition does not publish production homography timing
or coverage snapshots. Those fields therefore render as `Unavailable` with the
specific missing-source reason. RMA-171 does not invent a second camera pipeline
merely to make the dashboard look complete.

## UI behavior

The existing Diagnostics button still toggles the panel. The panel is enlarged and
scrollable so all sections remain accessible on smaller displays. Legacy
`Func<string>` binding remains only as an explicit adapter that marks all typed
sections unavailable; the settings-backed production composition uses the typed
snapshot source.
