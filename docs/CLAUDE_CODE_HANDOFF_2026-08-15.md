# Claude Code Handoff — Weachy Mini

**Date:** 2026-08-15  
**Repository:** `https://github.com/ekkus93/weachy-mini`  
**Primary branch:** `master`  
**Code baseline immediately before this handoff document:** `db05da6e02cc0571cfe038aaef56acb4b4f1eff4` — `fix: pace RMA-135 post-load stabilization`

> The commit containing this handoff document is documentation-only. Use the current `master` after pulling, but treat `db05da6e02cc0571cfe038aaef56acb4b4f1eff4` as the exact code baseline described below.

## 1. How to work on this repository

The preferred workflow for this project is:

1. Start from an up-to-date local checkout of `master`.
2. Inspect the repository TODO/spec files before choosing the next implementation task.
3. Make the smallest change that preserves the existing fail-closed architecture and invariants.
4. Run lint, unit, static-contract, compile/static checks locally as far as the local environment allows.
5. Do **not** spend time polling or monitoring GitHub Actions unless explicitly asked. The user monitors CI and will bring back failures that need work.
6. When a device/Unity-only gate cannot be executed locally, say so clearly and leave the physical gate for the user/self-hosted runner rather than weakening acceptance.
7. Do not add silent fallback behavior to make acceptance pass.

The user has generally been promoting small, verified fixes directly to `master` rather than waiting on PR review. Preserve that workflow unless the user asks for something different.

## 2. Current high-level project state

The project is late in the Android digital-twin roadmap. Most Phase 19 hardening tasks are implemented. The immediate active problem is **RMA-135 physical Android resource-governor acceptance** on the LG-H872 boundary device.

There are also two later bookkeeping/qualification items that remain open:

- **RMA-183:** functionality is implemented and contract-tested, but the authoritative roadmap block is still unchecked.
- **RMA-184:** repository policy/tooling exists, but representative physical-device qualification is incomplete.

After those, Phase 20 (`RMA-190` through `RMA-194`) remains open.

## 3. Current exact code baseline

The latest code commit before this handoff file is:

```text
db05da6e02cc0571cfe038aaef56acb4b4f1eff4
fix: pace RMA-135 post-load stabilization
```

Recent relevant commits, newest first:

```text
db05da6e02cc0571cfe038aaef56acb4b4f1eff4  fix: pace RMA-135 post-load stabilization
77a302387d8444a8633193e272cd059cb67f15b5  fix: make RMA-135 fault injection preflight deterministic
24a85bc423fa30e3ca43eeb47784b3a194383fbb  fix: attach RMA-135 monitor threads to Android JVM
a0dfceca0eba09f0c4398b8b9b947dff60333485  fix: format RMA-183 static contract
faafc0a64966da01483f60f82e0635ef6054a298  RMA-183: restore memory storage pressure closure contract
c4167f5229a00f586c2eb5e0bc431ed7b97a0a41  fix: silence Unity nullable test warnings
ccf20ec95d34630d41a5d27ff3acb4ab03269498  fix: align Unity tests with lifecycle and structured diagnostics
```

At handoff time there are no open PRs.

### Temporary branch cleanup

A temporary remote staging branch exists:

```text
rma135-postload-settle-fix
```

It was used to stage the last RMA-135 change in two commits. **Do not merge it.** `master` already has the clean squashed equivalent at `db05da6...`. The branch can be deleted when convenient.

## 4. Immediate active task — RMA-135 physical acceptance

### 4.1 Authoritative task documents

Read these first:

- `docs/RMA_135_RESOURCE_THERMAL_GOVERNOR_SPEC_2026-08-10.md`
- `docs/RMA_135_RESOURCE_THERMAL_GOVERNOR_TODO_2026-08-10.md`

The RMA-135 TODO is still **In progress**. Phases 0 through 6 are complete. Phase 7 physical/exact-SHA evidence and Phase 8 closure remain open.

Open Phase 7/8 work includes:

- pass hosted RMA-135 validation on the intended exact SHA;
- pass permanent repository CI on that SHA;
- pass Local Unity Android Validation on that SHA;
- collect representative-phone memory evidence;
- record API-26 thermal telemetry as explicitly unavailable;
- run the local LLM concurrently with authoritative MuJoCo stepping;
- prove a physics-budget violation cancels/suspends LLM work rather than degrading physics;
- prove same-process recovery;
- record exact source/APK/device/model/governor/physics/cleanup provenance;
- write the permanent validation document;
- reconcile the RMA-135 roadmap bullets and mark RMA-135 complete only when the exact implementation SHA has hosted + Unity/Android + physical acceptance evidence.

### 4.2 RMA-135 architectural invariants — do not weaken these

The production resource-governor design is intentionally fail-closed. Preserve all of these:

- authoritative MuJoCo physics has higher priority than local LLM throughput;
- never enlarge the physics timestep to make LLM inference fit;
- never arbitrarily skip physics work as an LLM mitigation;
- never create a second MuJoCo simulation worker/session in the physical acceptance harness;
- the acceptance harness is non-owning and must not stop/dispose/replace the live production simulation worker;
- no automatic retry of a cancelled generation;
- no hidden provider/model fallback;
- no cloud fallback;
- no JSON/output repair fallback;
- unavailable telemetry remains explicit rather than being interpreted as healthy;
- the loaded local-LLM execution profile is immutable; if a more restrictive profile is required, explicit provider recreation is required;
- cancellation uses the normal provider cancellation/drain/release path;
- no prompt/chat/response content should enter resource diagnostics or acceptance reports.

The RMA-135 spec is explicit that physical acceptance must show a controlled pressure/cancellation case and recovery without app restart.

## 5. RMA-135 failure/fix history

Understanding the last three physical failures is important because they were three different issues.

### 5.1 Failure 1 — Android JNI signal sampling crashed during monitor continuation

Observed failure:

```text
status=SignalFailure
detail=Local LLM generation was cancelled because resource monitoring failed:
Object reference not set to an instance of an object.
```

Root cause:

- the resource monitor executes periodic sampling after async continuation;
- `ReachyAndroidLocalLlmResourceSignalSource.Capture()` uses `AndroidJavaObject` JNI calls;
- background/custom threads must be attached to the Android JVM before JNI calls;
- the bridge did not attach worker threads.

Fix:

```text
24a85bc423fa30e3ca43eeb47784b3a194383fbb
fix: attach RMA-135 monitor threads to Android JVM
```

Changed:

- `Assets/ReachyMini/Runtime/Application/ReachyAndroidLocalLlmResourceSignalSource.cs`
- `scripts/tests/test_rma135_resource_governor.py`

Behavior now:

- records the managed thread that constructed the Android signal source;
- leaves the normal Unity owner thread alone;
- attaches a different worker thread to the JVM before JNI sampling;
- detaches it in `finally`;
- attachment/detachment failures remain fail-visible.

### 5.2 Failure 2 — synthetic `Exceeded` was consumed by preflight

Physical run #236 was sourced from `24a85bc...` and failed with:

```text
status=ResourceSuspendedBeforeStart
reasons=DeviceProfileLimit, ThermalSignalUnavailable, PhysicsBudgetExceeded
```

This was not the JNI crash. The one-shot controlled `Exceeded` signal could land during `GenerateAsync` preflight instead of during active-generation monitoring.

Fix:

```text
77a302387d8444a8633193e272cd059cb67f15b5
fix: make RMA-135 fault injection preflight deterministic
```

Changed:

- `Assets/ReachyMini/Runtime/Application/Rma135AcceptanceTestDoubles.cs`
- `scripts/tests/test_rma135_android_acceptance_contracts.py`

Current `Rma135FaultInjectingPhysicsBudgetSource` behavior:

- stores `LastObservedRealState`;
- requires a real admissible (`Healthy` or `AtRisk`) state before arming;
- replays that verified pass-through exactly once for generation preflight;
- injects `Exceeded` on the next **live** capture, intended to be the active-generation monitor capture;
- records `UnderlyingStateAtInjection`;
- increments `InjectedCount` exactly once.

This fixed the earlier preflight/injection race without changing production governor semantics.

### 5.3 Failure 3 — post-model-load stabilization window was unrealistically short

The first run that definitely used `77a302...` was RMA-135 workflow **run #237**:

```text
RMA-135 workflow run: 31913491208
source_sha: 77a302387d8444a8633193e272cd059cb67f15b5
source Local Unity run: 31912806998
Local Unity conclusion: success
physical artifact id: 9254328319
```

The physical run reached model load and then failed before any generation request:

```text
The real post-model-load physics/resource envelope did not recover enough to
admit the already-loaded provider after 12 observations. No generation request
was started.
```

Important checkpoint evidence from that run:

```text
001 bootstrap_started                  7.033 ms
002 bootstrap_component_installed     14.884 ms
003 start_entered                   5068.157 ms
004 artifact_verification_started   5083.917 ms
005 artifact_verified              8591.652 ms
006 abi_verified                   8622.640 ms
007 production_physics_runtime_started 8648.376 ms
008 production_physics_runtime_ready   8872.071 ms
    state=Healthy
    observations=7
    exceeded_observations=1
    steps=1933
009 resource_admission_ready        8901.516 ms
    mode=Nominal
    device=Conservative
    thermal=Unavailable
    ctx=1024
    threads=2
010 model_load_started              8914.619 ms
011 model_loaded                   12021.842 ms
    load_ms=3101.075
012 post_load_stabilization_started 12035.163 ms
013 cleanup_started               12640.811 ms
014 provider_disposed             12982.048 ms
015 production_physics_runtime_preserved 12990.292 ms
016 failed                        13006.335 ms
```

The critical observation is that model creation consumed about **3.1 seconds**, while all 12 post-load recovery observations completed in only about **0.6 seconds**. Startup itself had already needed seven observations and had seen one genuine `Exceeded` sample.

The evidence did not indicate a process-memory crisis. The captured `dumpsys meminfo` total PSS was approximately **522,644 KiB** with native-heap PSS about **122,408 KiB**.

The RMA-135 specification does **not** impose a sub-second post-load recovery deadline. It requires fail-closed behavior and real physical evidence.

### 5.4 Current fix for Failure 3

Current code baseline:

```text
db05da6e02cc0571cfe038aaef56acb4b4f1eff4
fix: pace RMA-135 post-load stabilization
```

Only the acceptance/evidence layer changed. Production governor thresholds and behavior did not change.

`Rma135FaultInjectingPhysicsBudgetSource` now has:

```text
PostLoadSettleSampleInterval = 800 ms
postLoadSettleSpacingEnabled = true
hasRealCapture
```

Behavior:

1. During the existing post-load stabilization phase, after the first real capture, subsequent real physics captures are spaced by 800 ms.
2. The existing acceptance loop still has its 12-observation bound and existing profile-compatibility checks.
3. This gives the LG-H872 roughly a several-second / up-to-about-nine-second real settling opportunity rather than about half a second.
4. The samples are still real authoritative physics samples; no healthy state is fabricated.
5. When `ArmOneShotExceededAfterPassThrough()` is called, `postLoadSettleSpacingEnabled` becomes `false`.
6. Therefore active governed generation still uses the normal coordinator monitor cadence (`MonitorInterval = 25 ms`) and the synthetic injection remains prompt.
7. Sustained real overload still fails the acceptance gate.

Static regression coverage was added to `scripts/tests/test_rma135_android_acceptance_contracts.py` to require the 800 ms acceptance-only settling interval and its disable-on-arm behavior.

### 5.5 Local validation for `db05da6...`

The last local validation reported:

- RMA-135 Android physical-acceptance static contract: passed;
- RMA-135 resource-governor static contract: passed;
- shell runner `bash -n`: passed;
- Python compile/static checks: passed;
- broader available Python/static suite: **373/373 passed**.

Unity/device execution was not available in the ChatGPT sandbox. The next physical run is therefore the meaningful verification.

## 6. What Claude Code should do next for RMA-135

### First action

Do **not** immediately change the code again. First consume the next physical LG-H872 result built from current `master` / `db05da6...` (or a descendant containing only documentation changes).

The next run should be exact-SHA through:

- `.github/workflows/rma135-resource-thermal-governor.yml`
- Local Unity Android Validation
- `.github/workflows/rma135-resource-governor-android.yml`

The physical workflow intentionally checks out `github.event.workflow_run.head_sha` and downloads the APK from that exact Local Unity run. Preserve this exact-SHA provenance model.

### Expected successful checkpoint progression

A successful run should progress beyond:

```text
post_load_stabilization_started
```

to at least:

```text
post_load_stabilized
physics_fault_injection_generation_started
physics_fault_injection_generation_completed
physics_continuity_verified
governor_recovery_started
governor_recovered
post_recovery_generation_started
post_recovery_generation_completed
final_observation_completed
cleanup_started
provider_disposed
production_physics_runtime_preserved
passed
```

Expected controlled-injection result:

```text
fault_injection_governed_status = ResourceCancelledDuringGeneration
fault_injection_provider_status = Cancelled
physics_fault_injection_count = 1
```

The production simulation worker must continue to advance across cancellation.

Expected recovery behavior:

- governor exits `Suspended` only after ordinary recovery observations;
- the cancelled request is not replayed;
- a new post-recovery request succeeds in the same process;
- physics remains authoritative and advances;
- no network fallback, automatic retry, physics timestep modification, or JSON repair is used.

### If the next run fails

Use the uploaded RMA-135 evidence artifact before editing code. Inspect:

- `workflow-provenance.txt`
- `scope.txt`
- `changed-files.txt`
- `checkpoints/*.json`
- `rma135-resource-governor-acceptance.json`
- `logcat.txt`
- `meminfo.txt`
- `battery.txt`
- `thermalservice.txt`
- APK/model SHA evidence files

Determine which stage actually failed. Do not infer from the final zero-filled failure report alone; the checkpoint set is the authoritative progress trace.

If post-load stabilization still fails after the paced window, that is stronger evidence of a genuine sustained loaded-profile incompatibility or physics-pressure condition. At that point, inspect the real physics states/reasons and consider whether provider admission/profile selection itself needs a reviewed change. Do not simply lengthen waits indefinitely or bypass `ProfileFitsWithin`.

## 7. RMA-135 key files

### Design / task state

- `docs/RMA_135_RESOURCE_THERMAL_GOVERNOR_SPEC_2026-08-10.md`
- `docs/RMA_135_RESOURCE_THERMAL_GOVERNOR_TODO_2026-08-10.md`

### Production policy and signals

- `Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmResourceGovernor.cs`
- `Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmGovernedGenerationCoordinator.cs`
- `Assets/ReachyMini/Runtime/Core/Application/ReachyLocalLlmPhysicsBudgetTracker.cs`
- `Assets/ReachyMini/Runtime/Core/Application/ReachySimulationLocalLlmPhysicsBudgetSource.cs`
- `Assets/ReachyMini/Runtime/Application/ReachyAndroidLocalLlmResourceSignalSource.cs`

### Physical acceptance

- `Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.cs`
- `Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.Acceptance.cs`
- `Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.Diagnostics.cs`
- `Assets/ReachyMini/Runtime/Application/Rma135AcceptanceTestDoubles.cs`
- `Assets/ReachyMini/Runtime/Application/Rma135AcceptanceReportModels.cs`
- `scripts/run_rma135_resource_governor_acceptance_android.sh`

### Tests / CI

- `scripts/tests/test_rma135_resource_governor.py`
- `scripts/tests/test_rma135_android_acceptance_contracts.py`
- `managed/ReachyMini.ResourceGovernor.Integration.Tests/Program.cs`
- `.github/workflows/rma135-resource-thermal-governor.yml`
- `.github/workflows/rma135-resource-governor-android.yml`

## 8. RMA-183 — functionality implemented, authoritative roadmap still stale

RMA-183 core implementation already exists. The major implementation commit was:

```text
6f010aea5eff0ba72b6870e0fbdfb803da9668dd
RMA-183: handle memory and storage pressure
```

Later audit/contract commits were:

```text
faafc0a64966da01483f60f82e0635ef6054a298
RMA-183: restore memory storage pressure closure contract

a0dfceca0eba09f0c4398b8b9b947dff60333485
fix: format RMA-183 static contract
```

### Implemented RMA-183 behavior

The implementation includes:

- Unity `Application.lowMemory` ingress owned by `ReachyApplicationHostBehaviour`;
- camera texture release for recreatable output;
- `ReachyMemoryPressureRegistry` participant sweep;
- `Resources.UnloadUnusedAssets()`;
- structured `application.memory.low_handled` diagnostics;
- idle local-LLM native model unload while preserving active generation/loading state;
- explicit reload requirement after idle model pressure release;
- model-download storage rechecks every 4 MiB;
- resumable `.part` + metadata preservation on storage-pressure download failures;
- non-resumable import staging cleanup semantics;
- diagnostic bundle storage preflight with 16 MiB reserve plus bundle maximum;
- explicit insufficient-storage diagnostics/user guidance;
- Settings UI action `CLEAN UP RECOVERABLE STORAGE`;
- narrow cleanup limited to app-owned diagnostic artifacts plus Unity cache, preserving installed models/settings/credentials/user state.

Static contract:

- `scripts/tests/test_rma183_memory_storage_pressure.py`

Managed coverage:

- `managed/ReachyMini.Core.Tests/Rma183MemoryStoragePressureContractTests.cs`
- local-LLM memory-pressure tests in `managed/ReachyMini.LocalLlm.Tests/Program.MemoryPressureTests.cs`

Design/evidence:

- `docs/RMA_183_MEMORY_STORAGE_PRESSURE_SPEC_2026-08-15.md`
- `docs/validation/RMA_183_MEMORY_STORAGE_PRESSURE_LOCAL_VALIDATION_2026-08-15.md`

### Remaining RMA-183 problem

The authoritative roadmap file is still stale:

- `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`

Its current blob is:

```text
277bd732dbaf0c45181a584b7e4122d3b1efd275
```

The RMA-183 section still says:

```markdown
## RMA-183 — Handle memory and storage pressure

- [ ] Respond to low-memory callbacks.
- [ ] Release caches and optional models without corrupting active state.
- [ ] Handle low storage during model download and diagnostics export.
- [ ] Provide cleanup UI.
```

This is bookkeeping debt, not missing runtime functionality. ChatGPT previously avoided replacing the entire ~143 KiB roadmap through a connector that only supported full-file replacement. Claude Code with a normal local checkout can safely edit this small block.

Recommended eventual closure update:

- add `**Status:** Complete (2026-08-15)`;
- mark all four bullets `[x]`;
- add concise completion evidence referencing the low-memory ingress, registry/local-LLM release policy, storage-aware model/diagnostic paths, cleanup UI, and RMA-183 tests/spec/validation.

Do this only after confirming current `master` still contains the implementation/tests described above.

## 9. RMA-184 — representative-device matrix remains physically incomplete

Authoritative spec:

- `docs/RMA_184_REPRESENTATIVE_DEVICE_MATRIX_SPEC_2026-08-15.md`

Current status in that spec:

```text
In progress — repository qualification tooling complete; mid/high physical long-run evidence pending
```

Repository infrastructure already exists:

- `Assets/ReachyMini/Runtime/Application/ReachyRma184RepresentativeDeviceProbe.cs`
- `Assets/ReachyMini/Runtime/Core/Performance/ReachyRepresentativeDeviceMatrix.cs`
- `models/reachy-mini/android-device-matrix.json`
- `scripts/run_rma184_device_probe_android.sh`
- `scripts/validate_rma184_device_matrix.py`
- `scripts/tests/test_rma184_representative_device_matrix.py`
- `managed/ReachyMini.Core.Tests/Rma184RepresentativeDeviceMatrixContractTests.cs`
- `docs/validation/RMA_184_REPRESENTATIVE_DEVICE_MATRIX_LOCAL_VALIDATION_2026-08-15.md`

Current classes/defaults:

| Class | Representative target | Render target | LLM profile | Memory-growth ceiling |
|---|---|---:|---|---:|
| Low | LG-H872 | 30 FPS | Conservative | 128 MiB |
| Mid | SM-A546E class | 30 FPS | Balanced | 192 MiB |
| High | OnePlus 11 5G / Pixel 7 Pro class | 60 FPS | Performance | 256 MiB |

Long-run qualification is at least **1,800 seconds**. Physics p95 must remain at or below the fixed 2 ms timestep; authoritative state-lag growth is bounded to one 2 ms timestep; local decode remains at least 1 token/s; render/memory/thermal ordering gates also apply.

### RMA-184 work still required

The authoritative roadmap remains unchecked for:

- define low/mid/high performance Android test classes;
- record SoC, Android version, RAM, graphics API, camera capability, speech-service availability;
- establish supported/unsupported criteria;
- publish measured default profiles;
- prove long-running tests do not accumulate unbounded memory/state lag;
- prove documented thermal-degradation priority order;
- prove supported devices meet simulation and interaction targets.

Even though the definitions/tooling largely exist in code/spec, the spec deliberately keeps RMA-184 open because physical qualification is not complete.

Important physical gap:

- the LG-H872 has reusable low-class evidence, but still needs the integrated RMA-184 30-minute post-warmup run;
- SM-A546E mid-class runtime metadata/long-run qualification is pending;
- OnePlus 11 5G high-class qualification is pending;
- Pixel 7 Pro second high-class family qualification is pending.

Do not mark RMA-184 complete based only on repository/static validation.

## 10. Authoritative roadmap state after RMA-184

The relevant tail of `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` currently has RMA-183 and RMA-184 open, followed by Phase 20.

### Phase 20 — all still open

#### RMA-190 — automated end-to-end scenarios

Still required:

- offline launch with no network;
- MuJoCo full model load and neutral reset;
- deterministic gesture replay;
- front camera -> head rotation -> transformed Reachy-eye frame;
- on-device ASR -> local LLM -> behavior -> offline TTS;
- rear-camera visual question with optional VLM;
- independent cloud-provider combinations;
- permission denial/revocation;
- network loss/rate limit/malformed response/cancellation;
- model corruption/OOM;
- solver fault and controlled reset.

If representative-device physical work is temporarily unavailable, RMA-190 is the next substantial software task that can be implemented without pretending RMA-184 is complete.

#### RMA-191 — privacy/security review

Still open:

- embedded-key audit;
- Keystore audit;
- network security config;
- disclosure before cloud-bound audio/image/text;
- logs/diagnostics redaction;
- temporary media cleanup;
- native buffer/lifetime safety.

#### RMA-192 — license/attribution review

Still open:

- dependency/asset inventory reconciliation;
- repository/app notices;
- Reachy-derived asset attribution/share-alike handling;
- no-endorsement verification;
- selected local-model redistribution/download presentation.

#### RMA-193 — documentation

Still open:

- build instructions;
- Android requirements;
- model install/import;
- provider/BYOK configuration warning;
- calibration workflow;
- fidelity-level explanation;
- Camera Level 1 approximation/missing-pixel behavior;
- diagnostic/troubleshooting instructions;
- privacy/data-flow explanation.

#### RMA-194 — final release acceptance

Still open. It includes offline launch, authoritative/stable MuJoCo, full closed-loop rendering, Level 1 reprojection, on-device speech/offline TTS where installed, local LLM coexistence with physics, provider configurability, behavior validation, no silent fallbacks, diagnostics, representative-device report, and license/attribution completion.

The initial release gate also requires every remaining incomplete item to be resolved or explicitly moved to a named later milestone with rationale.

## 11. Prior Unity test fixes that should not be regressed

Two recent fixes are easy to accidentally undo when editing acceptance/runtime code.

### `ccf20ec...` — lifecycle/structured-diagnostic test alignment

Do not reintroduce:

- camera-local `OnApplicationPause(bool)` lifecycle ownership;
- old plaintext error-log expectations;
- duplicate lifecycle ingress.

`ReachyApplicationHostBehaviour` owns the Unity lifecycle ingress. Structured RMA-170 diagnostics are intentional.

### `c4167f...` — nullable warning fix

Unity test compilation had CS8602 warnings in authoritative invariant/rendering tests because nullable flow state did not survive helper lambdas. The fix uses explicit post-helper null-forgiving dereferences (`renderer!`) where the helper guarantees initialization. Do not casually remove those if warnings-as-errors is still active.

## 12. Validation philosophy for the next agent

When a failure appears:

1. identify the exact source SHA first;
2. distinguish hosted/static, Local Unity, and physical-device failures;
3. use the physical checkpoint/evidence artifact rather than only the final exception;
4. preserve the production safety invariants;
5. fix acceptance harness races/timing only when evidence shows the harness is the defect;
6. if the device demonstrates a genuine sustained safety violation, do not turn it into a pass by weakening thresholds;
7. add a regression contract for every acceptance bug that is fixed;
8. keep exact-SHA provenance intact.

## 13. Recommended execution order from this handoff

### Priority 1 — finish RMA-135 exact-SHA physical acceptance

Wait for / consume the first LG-H872 physical result built from current `master` containing `db05da6...`. Diagnose from the evidence artifact. Continue until the physical run demonstrates:

- real resource observations;
- concurrent authoritative physics + local LLM;
- controlled physics-budget cancellation;
- physics continuity;
- same-process governor/provider recovery;
- successful post-recovery generation;
- exact-SHA/device/model/APK evidence.

Then finish RMA-135 Phase 8 documentation/roadmap closure.

### Priority 2 — repair RMA-183 roadmap bookkeeping

Once RMA-135 is not actively failing, safely mark the already-implemented RMA-183 block complete in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` and include completion evidence.

### Priority 3 — complete RMA-184 representative-device qualification

Run/collect physical metadata and 30-minute long-run qualification evidence for the configured device classes. Do not fabricate mid/high measurements.

### Priority 4 — begin Phase 20

Start RMA-190 automated E2E scenarios, then RMA-191 security/privacy, RMA-192 license/attribution, RMA-193 docs, and RMA-194 release acceptance.

## 14. Handoff checklist for Claude Code

Before making a new change:

- [ ] `git fetch` / update local `master`.
- [ ] Confirm current `master` contains `db05da6...` in ancestry.
- [ ] Read this handoff file.
- [ ] Read the RMA-135 spec/TODO.
- [ ] Check whether a newer physical RMA-135 result exists and identify its exact source SHA.
- [ ] If a new device failure exists, inspect the entire physical evidence artifact/checkpoints.
- [ ] Run focused static/managed tests relevant to the change.
- [ ] Do not poll CI unless the user explicitly asks.
- [ ] Do not merge `rma135-postload-settle-fix`; it is disposable staging history.
- [ ] Keep the authoritative roadmap honest: implemented != physically qualified != closed.

## 15. One-sentence current status

**The repository is currently blocked on completing exact-SHA RMA-135 physical resource-governor acceptance on the LG-H872 after the latest acceptance-only post-load settling fix; RMA-183 runtime functionality is implemented but its roadmap block is stale, RMA-184 physical representative-device qualification remains incomplete, and Phase 20 release/E2E work has not started.**
