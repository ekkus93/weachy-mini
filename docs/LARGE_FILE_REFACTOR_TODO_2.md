# Large File Split/Refactor TODO — Round 2

This is the second round of the large-file-splitting effort. Round 1
(`docs/LARGE_FILE_REFACTOR_TODO.md`) split the original top-10 files by line
count down to under 800 lines each; this document covers the *new* top-10
that surfaced once those were split. It follows the exact same process and
ground rules as round 1 — read that document's "Ground rules" section for
full context on tooling/conventions; the summary is repeated below.

## Ground rules (same as round 1)

- **Process files one at a time, in the order listed below.** Do not start
  file N+1 until file N's split is committed and the relevant subset of
  `./scripts/ci.sh` (native/managed/Android build+test, or `python3 -m
  unittest discover -s scripts/tests` for Python) is green.
- **Target: every resulting file under 800 lines.**
- **No behavior changes.** Pure move/reorganize refactors — same types, same
  members, same signatures, same namespaces/packages/public API. Don't fix
  bugs or rename things in the same commit; file that as separate follow-up
  work (the one narrow exception found in round 1: a genuinely necessary
  visibility change like `private` → package-private/`internal` when a member
  is promoted out of a nested/enclosing scope — do only the minimum needed).
- **Commit incrementally**, following the suggested extraction order below
  (smallest/safest pieces first), verifying build and tests pass after each
  step.
- Per `CLAUDE.md`, commits land directly on `master`, prefixed `RMA-<n>:` —
  file or ask for a ticket number before starting if one doesn't exist for
  this cleanup. (Round 1's commits used plain conventional-commit prefixes
  instead, following this session's established precedent — continue that
  unless told otherwise.)
- Where a file has framework-specific mechanics (Unity `.meta` files, C#
  `partial class`, MonoBehaviour serialization, Python sibling-file loading),
  follow the guidance in that file's section.

## ⚠️ The single most important lesson from round 1

**Every file in this round except two (`AndroidOnDeviceAsrProvider.cs` and
`ReachyPresentationBuilder.cs`) has at least one test or CI workflow that
reads the file's exact literal source text — by hardcoded path, and/or by
grepping for specific method names, constants, or string fragments — to
verify a "contract" is still present in the code.** This is *not* a
hypothetical risk: in round 1, six separate files hit this exact landmine,
and every one of them silently broke on the very first extraction step
before being caught. **This is a genuinely dangerous, no-warning failure
mode**: the code still compiles and the class still works — the private
member the check was grepping for is now just in a different file — but the
CI gate (or, worse, the *local* test) throws a assertion/`SystemExit` because
its `File.ReadAllText`/`Path(...).read_text()` call only ever looked at one
path.

**For every file below with a landmine noted, fix the test/CI check FIRST,
as its own prerequisite commit, before touching the actual split** — verify
the fix passes against the *current, unsplit* file, then proceed. The fix is
almost always the same shape: replace the single hardcoded
`File.ReadAllText(path)` / `Path(...).read_text()` with a loop that
concatenates every file in the target directory matching a naming pattern
that covers the planned split (a `Directory.GetFiles(dir, "Prefix*.cs")` glob
in C#, or an explicit list / `Path(...).glob(...)` in Python), then re-run
the check to confirm nothing regressed before starting.

## Progress tracker

| # | File | Lines | Landmine? | Status |
|---|---|---|---|---|
| 1 | `managed/ReachyMini.Core.Tests/Program.cs` | 1161 | No (but see `InternalsVisibleTo` note) | ☑ Done (commits `d7ec94b`, `edb03bf`, `279fbf4`, `cd773a1`, `269812c`, `75795bd`, `225e97f`, `139863e`) |
| 2 | `Assets/ReachyMini/Runtime/Core/Perception/ReachyVisionProviderContracts.cs` | 1158 | No (CI check already directory-wide) | ☐ Not started |
| 3 | `Assets/ReachyMini/Runtime/Application/ReachyMainScreen.cs` | 1115 | **Yes — 4 separate checks** (rma081, rma082, rma090, test_rma132) | ☐ Not started |
| 4 | `Assets/ReachyMini/Runtime/Core/Application/ReachySettingsState.cs` | 1112 | **Yes** (rma082-settings.yml) | ☐ Not started |
| 5 | `scripts/calibration_experiment.py` | 1101 | **Yes** (rma072 CI path trigger only — no in-file grep) | ☐ Not started |
| 6 | `managed/ReachyMini.LocalVlm.Tests/Program.cs` | 1029 | No (but no case-count safety net — see notes) | ☐ Not started |
| 7 | `Assets/ReachyMini/Runtime/Core/Perception/ReachyLocalVisionLanguageContracts.cs` | 1016 | **Yes** (rma114 workflow + `LocalVlm.Tests/Program.cs`) | ☐ Not started |
| 8 | `Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.cs` | 996 | **Yes — 2 workflows + a Python test** | ☐ Not started |
| 9 | `Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.cs` | 989 | No | ☐ Not started |
| 10 | `Assets/ReachyMini/Editor/ReachyPresentationBuilder.cs` | 957 | No (one duplicated-constants note, not a landmine) | ☐ Not started |

Mark each row `☑ Done (commit <hash>)` as it lands.

---

## 1. `managed/ReachyMini.Core.Tests/Program.cs` (1161 lines)

**Responsibility:** the console-runner (`OutputType: Exe`) test suite for
`ReachyMini.Core.Tests`. `Main()` unconditionally runs 3 fast, native-free
tests, then — only when `REACHY_MANAGED_NATIVE_TESTS=1` — runs 6 more that
exercise the real native `reachy_sim` backend and the authoritative
simulation worker.

**Mechanism:** `internal static partial class Program`, following the exact
precedent set by `RemoteVlm.Tests`/`LocalVlm.Tests` in round 1.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `Program.cs` (anchor) | `Main()` only, unchanged dispatch table including the env-var gate | ~30 |
| `Program.TestHelpers.cs` | `WaitForSnapshot`, `AssertTrajectoryInvariant`, `CreateCommandBatch`/`WriteUInt32`/`WriteUInt64`, `AssertBytesEqual`, `AssertControlSuccess`, `AssertEqual<T>`, `AssertThrows<TException>` | ~190 |
| `Program.Rma031LayoutTests.cs` | `TestProjectMetadata`, `TestNativeLayouts` | ~90 |
| `Program.Rma031SessionLifecycleTests.cs` | `TestNativeSessionLifecycle` | ~130 |
| `Program.Rma032WorkerTests.cs` | `TestAuthoritativeSimulationWorker`, `TestAuthoritativeSimulationWorkerPublishedState`, nested `SyntheticAuthoritativeStateReader`, `ObserveWorkerAtCadence` | ~360 |
| `Program.Rma033SnapshotTests.cs` | `TestAuthoritativeSimulationWorkerSnapshots` | ~215 |
| `Program.Rma032WarningAndFaultTests.cs` | `TestSimulationWorkerWarningAccounting`, `TestAuthoritativeSimulationWorkerDeadlineMetrics` + `WaitForNativeStepEntry`, `TestAuthoritativeSimulationWorkerFaultRetention` + `WaitForWorkerFault` | ~240 |

### Watch out for

- **Do NOT convert any gated test to the sibling `[ModuleInitializer]`
  self-run pattern** used by `Rma031ManagedInteropContractTests.cs` /
  `Rma146ProviderFallbackPolicyContractTests.cs` / etc. in this same project.
  Module initializers run unconditionally at assembly load — that would
  silently make the 6 native-backend tests run in every environment
  (including CI/sandboxes without the native lib), defeating the
  `REACHY_MANAGED_NATIVE_TESTS` gate's whole purpose. Keep every moved method
  as an explicitly-invoked `private static` method called from `Main()`
  exactly as today.
- **`TestSimulationWorkerWarningAccounting` calls
  `ReachySimulationWorker.CountNewSolverWarningEpisodes`**, an `internal
  static` member made visible to this test assembly purely via
  `[assembly: InternalsVisibleTo("ReachyMini.Core.Tests")]` (declared in
  `ReachyMiniInteropAssemblyInfo.cs`). This grant is by *assembly name*, not
  namespace or file, so it survives the split automatically as long as the
  method stays somewhere inside the `ReachyMini.Core.Tests` project and the
  project's assembly name doesn't change — no action needed, just don't be
  surprised it "just works."
- No static/shared mutable state between the 9 test methods themselves (each
  builds and disposes its own `ReachySimSession`/`ReachySimulationWorker`) —
  only the assertion/helper layer is shared.

### Extraction order

1. Add `partial` to the class declaration only — no code moved yet, verify baseline.
2. `Program.TestHelpers.cs` (leaf utilities, lowest risk, de-risks everything after).
3. `Program.Rma031LayoutTests.cs` (pure-managed, no native backend needed — fastest feedback).
4. `TestSimulationWorkerWarningAccounting` alone into `Program.Rma032WarningAndFaultTests.cs` first (isolates the `InternalsVisibleTo`-dependent method for an easy-to-revert check), then the rest of that file.
5. `Program.Rma031SessionLifecycleTests.cs`.
6. `Program.Rma033SnapshotTests.cs`.
7. `Program.Rma032WorkerTests.cs` last (largest, most interdependent — nested fixture class + sole consumer of `ObserveWorkerAtCadence`).
8. Final: confirm `Program.cs` contains only `Main()`; run both `dotnet run` and `REACHY_MANAGED_NATIVE_TESTS=1 dotnet run` end-to-end.

---

## 2. `ReachyVisionProviderContracts.cs` (1158 lines)

**Responsibility:** the RMA-110 vision-provider vocabulary — pure data/interface
layer (no MonoBehaviours, no logic beyond constructor validation) shared by
frame sources, lightweight trackers, and VLM providers: provider
identity/capabilities, selection/epoch/timeout bookkeeping, frame
identity/coverage/ownership, per-kind requests, tracking geometry, per-kind
results, and the three provider interfaces.

**Mechanism:** no `partial class` needed — every type is fully self-contained
(no shared instance state, only two small `internal static` cross-type
helpers). Pure "cut a type block into a new file" work.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyVisionProviderEnums.cs` | 8 enums | ~75 |
| `ReachyVisionProviderDescriptors.cs` | `ProviderDescriptor` (hosts the shared `RequireText` helper), `FrameSourceCapabilities`, `TrackerCapabilities`, `VisionLanguageCapabilities` | ~225 |
| `ReachyVisionProviderSelection.cs` | `VisionProviderSelectionSnapshot`, `VisionProviderSelection`, `VisionRequestContext` | ~145 |
| `ReachyVisionFrameContracts.cs` | `ReachyVisionFrameIdentity`, `ReachyVisionCoverage`, `IReachyVisionFrameResources`, `ReachyVisionFrame` | ~275 |
| `ReachyVisionProviderRequests.cs` | `FrameSourceRequest`, `TrackingRequest`, `VisionLanguageRequest` | ~90 |
| `ReachyVisionTrackingGeometry.cs` | `NormalizedVisionBounds`, `TrackedObject` | ~100 |
| `ReachyVisionProviderResults.cs` | `FrameSourceResult` (hosts the shared `RequireFailure` helper), `TrackingResult`, `VisionLanguageResult` | ~250 |
| `ReachyVisionProviderInterfaces.cs` | `IReachyVisionFrameSource`, `IVisualTracker`, `IVisionLanguageProvider` | ~35 |

### Watch out for

- `ProviderDescriptor.RequireText` and `FrameSourceResult.RequireFailure` are
  `internal` (not `private`) — safe across files in the same assembly, no
  visibility changes needed, just don't accidentally tighten them to
  `private` during the move.
- **CI check is already directory-wide and safe**: `.github/workflows/
  rma110-vision-provider-contracts.yml` globs every `*.cs` in the `Perception`
  directory and concatenates them before checking tokens — confirmed some of
  those tokens (e.g. `AutomaticProviderFallbackEnabled => false`) already live
  in *other* files today, not this one. No CI fix needed before starting,
  **but the workflow's `git ls-files | grep -E 'rma110_(test|closeout|executor)|...'`
  stray-file check** means avoid naming new files with those substrings
  (none of the proposed names collide).
- `managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs` only
  references types via `using ReachyMini.Perception;` — unaffected.
- Unity `.meta` files: don't copy the original file's GUID into a new file
  (would collide) — let the Editor generate fresh ones.

### Extraction order

1. `ReachyVisionProviderEnums.cs` (zero dependencies).
2. `ReachyVisionProviderInterfaces.cs` (signature-only, tiny).
3. `ReachyVisionTrackingGeometry.cs` (depends only on `ProviderDescriptor.RequireText`, internal/cross-file-safe even before `ProviderDescriptor` itself moves).
4. `ReachyVisionProviderRequests.cs`.
5. `ReachyVisionFrameContracts.cs`.
6. `ReachyVisionProviderDescriptors.cs` (moving `ProviderDescriptor` here, after its consumers already moved in steps 3–5, proves the cross-file `RequireText` call works).
7. `ReachyVisionProviderSelection.cs`.
8. `ReachyVisionProviderResults.cs` last (highest fan-in — references nearly everything else).
9. Delete the now-empty original file + its `.meta`.

---

## 3. `ReachyMainScreen.cs` (1115 lines)

**Responsibility:** the production Android UI shell — a `MonoBehaviour`
(`[DisallowMultipleComponent]`, one `[SerializeField]` field, `OnGUI`/
`OnDestroy` lifecycle) rendering the whole app via Unity immediate-mode GUI:
status card, bottom control bar, a 7-section settings panel, diagnostics
panel. Binds to 3 external state stores and exposes ~23 public "command"
methods invoked by button clicks.

**Mechanism:** `partial class` is the *only* viable mechanism — it's a
`sealed` MonoBehaviour attached to a scene GameObject, so it can't be split
into independent classes without breaking `Bind()`/serialization identity.
Matches the existing `ReachySimulationWorker.*.cs` precedent.

### ⚠️ This file has FOUR separate hardcoded-source-text checks

All four `read_text()`/`File.ReadAllText()` the file at its exact current
path and grep literal strings out of it:

1. **`.github/workflows/rma081-main-screen.yml`** — checks for literal
   `void {control}(` for `RequestMicrophone`, `RequestCameraSelection`,
   `ToggleSettings`, `ToggleDiagnostics`, plus forbidden-navigation-token
   absence checks.
2. **`.github/workflows/rma082-settings.yml`** — checks for literal
   `ReachyProviderKind.Asr/.Tts/.Llm/.Vlm` (today these only appear inside
   `DrawProviderSettings`'s inline enum array!) and `void {action}(` for all
   16 settings actions.
3. **`.github/workflows/rma090-camera-discovery.yml`** — checks for literal
   `'RMA-091'` and `'CameraCapabilitySnapshot'`.
4. **`scripts/tests/test_rma132_local_model_packages.py`** — lower-cases the
   whole file and asserts none of `qwen`/`gemma`/`llama-3`/`smollm`/`phi-`
   leak into the UI text.

**Before touching the split**, per the ground-rule prerequisite: add a small
shared `private static readonly ReachyProviderKind[] ProviderKinds = { Asr,
Tts, Llm, Vlm };` field to the file (next to the existing `SettingsSections`
array) and switch `DrawProviderSettings` to iterate it — this is a no-op
behavior change that guarantees the literal enum-member tokens rma082 checks
for stay present in the file regardless of where `DrawProviderSettings`
itself later moves. Do this *before* extracting `SettingsPanel.cs` (see
below). All 23 action methods and the `RMA-091`/`CameraCapabilitySnapshot`
tokens naturally stay in the anchor file per the target-file table below, so
checks 1, 3, and 4 need no other prerequisite fix — only check 2's
`ReachyProviderKind.*` tokens need the `ProviderKinds` array workaround.
After the split, also widen the three workflows' `paths:` trigger lists to
include the new partial-file names (they currently only list
`ReachyMainScreen.cs`).

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyMainScreen.cs` (anchor) | `[DisallowMultipleComponent]`/`: MonoBehaviour` decl, constants + new `ProviderKinds` array, fields, `Bind`×3, all 23 action methods, `OnDestroy`, the 3 `On*Changed` handlers, `Require*` guards | ~395 |
| `ReachyMainScreen.Hud.cs` | `OnGUI`, `DrawStatusCard`, `DrawBottomControls`, `DrawDiagnosticsPanel`, `CenterSettingsPanel`/`CenterPanel` | ~185 |
| `ReachyMainScreen.SettingsPanel.cs` | `DrawSettingsPanel`, `DrawSettingsNavigation`, `DrawSettingsContent`, `DrawProviderSettings`, `DrawCameraSettings`, `BuildPreferredCameraCapabilityLabel` | ~260 |
| `ReachyMainScreen.SettingsSections.cs` | `DrawSpeechSettings`, `DrawLocalModelSettings`, `DrawSimulationSettings`, `DrawPrivacySettings`, `DrawLicenseSettings` | ~170 |
| `ReachyMainScreen.Styles.cs` | 11 `GUIStyle?` fields, `EnsureStyles()`, `CreatePanelTexture()` | ~110 |

### Watch out for

- Only the anchor file declares `[DisallowMultipleComponent]`/`: MonoBehaviour`/`sealed`; other partials just declare `public sealed partial class ReachyMainScreen`.
- `GUIStyle?` fields, `snapshot`/`settingsSnapshot`/`cameraCapabilitySnapshot` fields, and the `Require*` helpers are read across all 5 files — fine under `partial class`, just don't duplicate a helper by mistake.
- `OnGUI`/`OnDestroy` can live in any partial file (Unity finds them via reflection over the whole type) — the table above places them for readability, not correctness.

### Extraction order

1. Prerequisite: add the `ProviderKinds` shared array in the still-monolithic file, verify rma082's check still passes.
2. `ReachyMainScreen.Styles.cs` (zero CI-text dependency, zero cross-method coupling beyond field visibility).
3. `ReachyMainScreen.SettingsSections.cs` (no CI literal-text dependency).
4. `ReachyMainScreen.SettingsPanel.cs` (the risky step — re-run all 3 workflow Python snippets locally before proceeding).
5. `ReachyMainScreen.Hud.cs` last (`OnGUI` ties everything together).
6. Final: confirm anchor is ~395 lines, re-run the full local test suite and all 3 workflows' Python checks, widen their `paths:` triggers.

---

## 4. `ReachySettingsState.cs` (1112 lines)

**Responsibility:** the Unity-independent Settings-screen application model —
enums, immutable value objects (`ReachyProviderSelection`,
`ReachyLicenseNotice`, `ReachyDurableSettings`, `ReachySettingsSnapshot`), and
the single stateful `ReachySettingsStateStore` (user-intent mutators,
durable-settings capture/apply, static label lookups, provider-construction
helpers, derived-text builders, generic cycling helpers).

**Mechanism:** `partial class` for `ReachySettingsStateStore`; the 5 value
types split out first with no partial needed.

### ⚠️ Landmine: `.github/workflows/rma082-settings.yml`

The same workflow file noted in file #3 above *also* reads
`ReachySettingsState.cs` as a single file (`state = Path(...).read_text(...)`)
and checks: the `enum ReachySettingsSection { ... }` block via regex; literal
`ReachyProviderKind.{Asr,Tts,Llm,Vlm}` (checked against `state` OR `screen` —
i.e. it's tolerant if the token is in *either* file, but don't rely on that);
`'Network-backed provider selections must be labeled network required.'`
(lives in `ReachyProviderSelection`'s ctor); `'ReachyProviderExecution.AndroidService'`
/`'ReachyConnectivityRequirement.NetworkRequired'`/`'never labeled offline'`
(all live in `BuildProvider`); `'privacyCloudSummary'`/`'SendsDataOffDevice'`
(split across `ReachySettingsSnapshot` and `ReachyProviderSelection`); and a
tolerant check for `CaptureDurableSettings`/`ApplyDurableSettings` (accepts a
match in either this file or `ReachySettingsPersistence.cs`).

**Fix first**: update the script's `state = ...` line to read and concatenate
every `ReachySettings*.cs` file in the `Application` directory (matching the
planned split's naming) before running its checks, and widen the `paths:`
trigger similarly. Verify against the current unsplit file before proceeding.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachySettingsEnums.cs` | 6 enums | ~55 |
| `ReachyProviderSelection.cs` | `ReachyProviderSelection` | ~75 |
| `ReachyLicenseNotice.cs` | `ReachyLicenseNotice` | ~40 |
| `ReachyDurableSettings.cs` | `ReachyDurableSettings` | ~30 |
| `ReachySettingsSnapshot.cs` | `ReachySettingsSnapshot`, `ReachySettingsChangedEventArgs` | ~150 |
| `ReachySettingsStateStore.cs` (anchor) | static option tables, field/ctor, `Current`/`Changed` | ~90 |
| `ReachySettingsStateStore.Sequencing.cs` | `NextString`, `NextInt`, `Contains` overloads | ~55 |
| `ReachySettingsStateStore.DerivedText.cs` | `BuildSpeechNetworkStatus`, `BuildPrivacySummary` (contains the CI-checked truthfulness strings) | ~55 |
| `ReachySettingsStateStore.ProviderBuilders.cs` | `CreateDefaultProviders`, `NextProvider`, `BuildProvider`, `SanitizeProviderExecution` (contains the CI-checked `"never labeled offline"` etc.) | ~120 |
| `ReachySettingsStateStore.Labels.cs` | `GetSectionLabel`, `GetProviderKindLabel`, `GetExecutionLabel`, `GetConnectivityLabel`, `GetCameraFacingLabel`, `GetSimulationFidelityLabel`, `GetLicenseNotices` (called externally by fully-qualified static access — verify `ReachyMainScreen.cs`/`ReachySettingsApplicationCompositionProvider.cs` call sites still resolve, which they will unchanged) | ~110 |
| `ReachySettingsStateStore.Persistence.cs` | `CaptureDurableSettings`, `ApplyDurableSettings` | ~105 |
| `ReachySettingsStateStore.Mutators.cs` | All 13 public `CycleXxx`/`ToggleHistory`/etc. methods + private `Publish`/`CreateSnapshot` | ~330 |

### Watch out for

- `current` field (single source of settings truth) is read/written from
  nearly every mutator — this is *the* reason `partial class` is required
  here, not composition.
- `BuildProvider` and `GetProviderKindLabel` are each called from more than
  one file/region — fine under `partial class`, no visibility changes.
- The optional-parameter overlay pattern in `Publish` mirrors
  `ReachySettingsSnapshot`'s constructor 1:1 — leave a comment noting the
  coupling once split, since a future signature change to one requires
  updating the other.
- Value types (enums, `ReachyProviderSelection`, `ReachyLicenseNotice`,
  `ReachyDurableSettings`, `ReachySettingsSnapshot`) have zero dependency back
  on `ReachySettingsStateStore` — safe to extract first with zero risk.

### Extraction order

1. `ReachySettingsEnums.cs`.
2. `ReachyLicenseNotice.cs`.
3. `ReachyDurableSettings.cs`.
4. `ReachyProviderSelection.cs`.
5. `ReachySettingsSnapshot.cs` (largest of the value-type extractions — do after the smaller ones).
6. Add `partial` to `ReachySettingsStateStore`'s declaration, no other change — commit separately.
7. `ReachySettingsStateStore.Sequencing.cs` (smallest, no `current` field access — validates partial-class mechanics with a trivial file first).
8. `ReachySettingsStateStore.DerivedText.cs` (still static, but now touches CI-critical strings — re-run the rma082 check specifically).
9. `ReachySettingsStateStore.ProviderBuilders.cs`.
10. `ReachySettingsStateStore.Labels.cs` (re-run `ReachyMainScreenTests.cs`/`ReachySettingsScreenTests.cs` specifically, since these are called externally by fully-qualified access).
11. `ReachySettingsStateStore.Persistence.cs` (first step touching `current` directly — do after 7–9 since it depends on their helpers).
12. `ReachySettingsStateStore.Mutators.cs` last (largest, touches `current` in every method, depends on nearly everything else already moved).

---

## 5. `scripts/calibration_experiment.py` (1101 lines)

**Responsibility:** the RMA-072 versioned, fail-closed calibration
experiment planning and execution module — contracts/constants, strict
untrusted-JSON ingestion, deep plan validation (8 experiment-type variants),
deterministic schedule compilation, serialization, and safety-gated physical
execution (`execute_schedule`, fail-closed emergency-stop-on-error).

**Mechanism: confirmed** (via grep, not assumed) to be loaded the exact same
dual way as `calibration_fitting.py` was in round 1 — plain `import
calibration_experiment` from `scripts/run_calibration_experiment.py`, AND
`importlib.util.spec_from_file_location` by explicit path from
`scripts/tests/test_calibration_experiment.py`. **Reuse the exact same
facade + `_load_sibling` bootstrap pattern**, not a new subpackage (a package
would break the plain `from calibration_experiment import (...)` call site).

### Landmine (path-trigger only, no in-file grep)

`.github/workflows/rma072-experiment-runner.yml` hardcodes the literal path
`'scripts/calibration_experiment.py'` twice as a `paths:` trigger filter.
There is **no** step that greps file contents (unlike most other landmines
in this doc) — the only consequence of splitting without fixing this is that
the workflow silently stops triggering on changes to the new sibling files.
Fix: widen both `paths:` lines to `'scripts/calibration_experiment*.py'`,
matching the fix already applied to `rma073-calibration-fitting.yml` in
round 1. Do this as part of the finalization step (step 5 below), not
necessarily before starting — it's not a correctness landmine, just a
CI-trigger-coverage one.

### Target files

| File | Contents | Depends on | Est. lines |
|---|---|---|---|
| `calibration_experiment_contracts.py` | Constants (`PLAN_CONTRACT_ID`…`ACTION_TYPES`), `ExperimentValidationError`, `ExperimentExecutionError`, `ImportLimits`/`DEFAULT_IMPORT_LIMITS`, `canonical_json_bytes`, `_error`, `_reject_constant`, `_reject_duplicate_pairs`, `strict_json_loads`, all `_require_*` primitives, `_validate_utc`, `_validate_hash`, `_position`, `_positive_duration`, `compute_plan_sha256`, `finalize_plan`, `schema_descriptor` | stdlib only | ~260 |
| `calibration_experiment_model.py` | `ScheduledAction`, `CompiledSchedule`, `ExecutionAuthorization`, `SafetyState`, `ExperimentAdapter` (Protocol) | contracts | ~120 |
| `calibration_experiment_planning.py` | `load_plan_file`, `_validate_experiment`, `validate_plan`, `_ScheduleBuilder`, `compile_plan`, `command_jsonl_bytes`, `schedule_json_bytes` | contracts, model | ~680 |
| `calibration_experiment_execution.py` | `_validate_authorization`, `_check_safety_state`, `execute_schedule` | contracts, model, planning | ~150 |
| `calibration_experiment.py` (facade) | Docstring on the dual-loading rationale, `_load_sibling` helper, ordered loads, re-export bindings for every public name | all 4 siblings | ~100 |

### Watch out for

- **A genuine two-way call cycle**: `validate_plan` calls `compile_plan(value,
  validate=False)` to cross-check compiled action count/duration against
  safety limits; `compile_plan` calls `validate_plan(plan)` by default unless
  told not to. **Keep `validate_plan` + `_validate_experiment` +
  `_ScheduleBuilder` + `compile_plan` together in one `_planning.py` module**
  — this is the one deliberate deviation from "one section = one file,"
  called out explicitly because splitting them would require fragile
  circular imports for no size benefit (combined they're only ~640 lines).
- `CompiledSchedule.manifest()` calls `_validate_utc` (a real load-order
  dependency, not just a type dependency) — load `contracts` before `model`.
- `_ScheduleBuilder` is private/module-internal — don't add it to the
  facade's re-export list.
- `DEFAULT_IMPORT_LIMITS`/`ImportLimits` are used pervasively as default
  arguments — all consumers must import the canonical instance from
  `contracts`, not redefine it.
- `scripts/tests/test_calibration_experiment.py` needs **no changes** — it
  loads the facade by path and only touches names off the module object, same
  as `test_calibration_fitting.py` needed none in round 1.

### Extraction order

1. `calibration_experiment_contracts.py` (pure leaf, zero internal deps).
2. `calibration_experiment_model.py` (depends only on step 1).
3. `calibration_experiment_planning.py` as one atomic unit (avoids the cycle) — largest, riskiest step, run the full test suite plus a specific load_plan_file/validate_plan/compile_plan round-trip check.
4. `calibration_experiment_execution.py` (now safely depends on the already-extracted planning module, no cycle).
5. Finalize the facade + widen the `rma072-experiment-runner.yml` path triggers; run `ruff check`/`ruff format --check` plus the full test suite.

---

## 6. `managed/ReachyMini.LocalVlm.Tests/Program.cs` (1029 lines)

**Responsibility:** the RMA-114 "optional/local-VLM extension point"
contract test harness (console-runner, `OutputType: Exe`) — manifest data
model validation, adapter capability/availability invariants, provider
configuration/creation gating, the fail-closed `UnavailableLocalVisionLanguageAdapter`
stub, JSON schema contract checks, and repo/documentation boundary checks.
README calls it a "45-case harness."

**Mechanism:** same `partial class Program` pattern as round 1's
`RemoteVlm.Tests` split, **with one structural difference to respect**: this
file's `Main()` calls test methods **directly**, with no `Run`/`RunAsync`
wrapper and **no `caseCount`/`ExpectedCaseCount` safety net**. Do not invent
one as part of this split — that would be a behavior change beyond a pure
structural move. Just move method bodies verbatim and leave `Main()` alone.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `Program.cs` (anchor) | usings, `internal static partial class Program`, `Main()` unchanged | ~55 |
| `Fakes.cs` | `FakeProvider` promoted from nested `private` to top-level `internal sealed class` | ~55 |
| `Program.Assertions.cs` | `True`, `False`, `Equal<T>`, `Same`, `Contains`, `SetEqual`, `Throws<TException>` | ~90 |
| `Program.Fixtures.cs` | `Manifest`, `Identity`, `Runtime`, `Limits`, `Distribution`, `Capabilities`, `Artifact`, `Configuration`, `Provider`, `LoadSchema`, `RepoRoot` | ~185 |
| `Program.ManifestOverviewTests.cs` | `ReleasePolicyIsOptionalAndFailClosed`, `ValidManifestPublishesExactCapabilities`, `ManifestRejectsUnsupportedSchema` | ~50 |
| `Program.IdentityRuntimeLimitsTests.cs` | 6 identity/runtime/limits tests | ~96 |
| `Program.DistributionCapabilitiesTests.cs` | 4 distribution/capabilities tests | ~47 |
| `Program.ArtifactAndManifestIntegrityTests.cs` | 10 artifact/manifest-integrity tests | ~120 |
| `Program.AdapterCapabilityTests.cs` | 5 adapter capability/availability tests | ~93 |
| `Program.ProviderConfigurationTests.cs` | 3 provider-configuration-gating tests | ~70 |
| `Program.ProviderCreationTests.cs` | 4 provider-creation-result tests (async) | ~76 |
| `Program.UnavailableStubTests.cs` | 4 unavailable-stub-adapter tests (async) | ~86 |
| `Program.SchemaContractTests.cs` | `RequiredManifestSections`/`RequiredArtifactFields` field arrays + 3 schema tests | ~110 |
| `Program.DocumentationBoundaryTests.cs` | 3 documentation/repo-boundary tests | ~76 |

### Watch out for

- `FakeProvider` (nested `private`) must become top-level `internal sealed
  class FakeProvider : IVisionLanguageProvider` so `Program.Fixtures.cs`'s
  `Provider(...)` factory can reference it unqualified.
- The two static readonly arrays (`RequiredManifestSections`,
  `RequiredArtifactFields`) aren't used in `Main()` — relocate them to
  `SchemaContractTests.cs`, their only consumer, rather than stranding them
  in the anchor.
- No hidden mutable shared state beyond those two arrays — every fixture
  factory is a pure function, materially lower-risk than the `RemoteVlm.Tests`
  split in round 1 (no live counter to race).
- After the split, sanity-check that the total method-call count in `Main()`
  still matches the README's "45-case" claim (no automated assertion catches
  a dropped case here, unlike `RemoteVlm.Tests`).

### Extraction order

1. Add `partial` to `Program`, no code moved — verify baseline.
2. `Program.Assertions.cs` (zero dependencies).
3. `Fakes.cs` (promote `FakeProvider`).
4. `Program.Fixtures.cs` (depends on `Fakes.cs` from step 3).
5. Test-group files, ordered by increasing fixture coupling:
   `DocumentationBoundaryTests` → `SchemaContractTests` →
   `DistributionCapabilitiesTests` → `IdentityRuntimeLimitsTests` →
   `ArtifactAndManifestIntegrityTests` → `ManifestOverviewTests` →
   `AdapterCapabilityTests` → `ProviderConfigurationTests` →
   `ProviderCreationTests` → `UnavailableStubTests`.
6. Final: confirm `Program.cs` contains only usings + `Main()`; run `dotnet
   run` and diff console output against the original pass banner; manually
   confirm the case count still matches "45."

---

## 7. `ReachyLocalVisionLanguageContracts.cs` (1016 lines)

**Responsibility:** the RMA-114 local/on-device VLM extension-point contract
layer — release/feature-flag policy enums, the model manifest schema
(identity, runtime requirements, limits, distribution rules, capabilities,
artifacts, aggregate manifest), and the adapter runtime contracts (descriptor/
capabilities/availability, provider configuration, provider-creation result,
the `ILocalVisionLanguageAdapter` interface, and the always-unavailable stub).

**Mechanism:** no partial class needed — same as file #2, every type is
self-contained; only two `internal static` helpers (`RequireBoundedText`/
`RequireIdentifier` on `LocalVlmModelIdentity`) are reused across files, and
being `internal` they're safe cross-file.

### ⚠️ Landmine: two places

1. **`.github/workflows/rma114-local-vlm-extension.yml`** — a "Record source
   digests" step runs `sha256sum` over a **fixed list of paths including this
   exact filename**; if the file is deleted/renamed mid-split without
   updating this list, the step **fails the CI job outright** (`sha256sum`
   errors on a missing file). Also a separate "exact-SHA evidence" step
   hashes the single old path (softer failure — stale evidence, not a hard
   CI break, but still wrong).
2. **`managed/ReachyMini.LocalVlm.Tests/Program.cs`**,
   `SourceContractContainsNoDownloadOrFallbackExecution()` — reads this exact
   file by hardcoded path and asserts 3 literal substrings are present
   (`"AutomaticModelDownloadEnabled => false"`, `"AutomaticProviderFallbackEnabled => false"`
   from `LocalVlmReleasePolicy`; the diagnostic string
   `"No local VLM runtime or model is installed; no fallback or download was attempted."`
   from `UnavailableLocalVisionLanguageAdapter`) plus 3 forbidden-substring
   checks (`HttpClient`/`WebRequest`/`Process.Start` absence).

**Fix `Program.cs`'s check first** (update it to read/concatenate the
resulting 5 files, or scan the whole `Perception` directory), verify against
the current unsplit file, **then** do the workflow's file-list update as part
of the same step that actually moves `LocalVlmReleasePolicy` and
`UnavailableLocalVisionLanguageAdapter` out of the original filename (see
extraction order below — this is folded into step 3, not deferred to the
end, since that's the exact point the check would otherwise start failing).

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyLocalVlmEnums.cs` | `LocalVlmReleasePolicy`, `LocalVlmArtifactSource`, `LocalVlmAdapterState`, `LocalVlmProviderCreationStatus` | ~45 |
| `ReachyLocalVlmManifestContracts.cs` (the file that inherits the original name's "final" contents) | `LocalVlmModelIdentity` (hosts `RequireBoundedText`/`RequireIdentifier`/`RequireHttpsUri`), `LocalVlmRuntimeRequirement`, `LocalVlmModelLimits`, `LocalVlmDistribution`, `LocalVlmSemanticCapabilities` | ~330 |
| `ReachyLocalVlmArtifactManifest.cs` | `LocalVlmArtifactDescriptor`, `LocalVlmModelManifest` | ~195 |
| `ReachyLocalVlmAdapterConfiguration.cs` | `LocalVlmAdapterDescriptor`, `LocalVlmAdapterCapabilities`, `LocalVlmAdapterAvailability`, `LocalVlmProviderConfiguration` | ~245 |
| `ReachyLocalVlmAdapterRuntime.cs` | `LocalVlmProviderCreationResult`, `ILocalVisionLanguageAdapter`, `UnavailableLocalVisionLanguageAdapter` (contains the CI-checked diagnostic string) | ~235 |

### Watch out for

- No production code outside this file references these types except
  `managed/ReachyMini.LocalVlm.Tests/Program.cs` — confirmed via repo-wide
  grep, so no silent external-caller breakage risk beyond the landmine above.
- `RequireBoundedText`/`RequireIdentifier` on `LocalVlmModelIdentity` are
  `internal`, reused by `LocalVlmRuntimeRequirement`, `LocalVlmAdapterDescriptor`,
  `LocalVlmProviderConfiguration` — safe cross-file, no visibility change.

### Extraction order

1. `LocalVlmProviderCreationResult` → `ReachyLocalVlmAdapterRuntime.cs` (self-contained, only reads from `LocalVlmModelManifest` which stays put for now).
2. `ReachyLocalVlmEnums.cs` (zero dependencies).
3. `ILocalVisionLanguageAdapter` + `UnavailableLocalVisionLanguageAdapter` into the same `ReachyLocalVlmAdapterRuntime.cs` from step 1 — **update `Program.cs`'s source-contract check as part of this same step**, since this is the point the two literal strings split across files.
4. `ReachyLocalVlmAdapterConfiguration.cs`.
5. `ReachyLocalVlmArtifactManifest.cs`.
6. Rename what remains (`LocalVlmModelIdentity` etc.) to `ReachyLocalVlmManifestContracts.cs` last — this changes the *original* filename, so update the `rma114-local-vlm-extension.yml` workflow's fixed source-digest path list in this same step, once, rather than juggling it mid-sequence.
7. After each step: `dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --warnaserror` and `dotnet run --project managed/ReachyMini.LocalVlm.Tests/...` (the same two commands CI runs).

---

## 8. `ReachyRma135ResourceGovernorAcceptance.cs` (996 lines)

**Responsibility:** a Unity runtime acceptance-test harness (`MonoBehaviour`,
self-installs via `[RuntimeInitializeOnLoadMethod]` when an Android launch
intent extra is present) that runs a single end-to-end RMA-135 physical
acceptance test on-device: verifies a staged GGUF artifact, exercises the
`LocalLlmResourceGovernor`/`LocalLlmGovernedGenerationCoordinator` admission
and governed-generation path, injects a controlled physics-budget-exceeded
fault to prove cancellation, verifies recovery, runs a second generation, and
writes JSON report + checkpoint files for a device-pull-based CI harness.

**Mechanism:** `partial class` (Unity MonoBehaviour constraint, same as file
#3), plus 2 new top-level non-partial helper files for the nested test
doubles/DTOs (promoted from `private` to `internal`, and **renamed with an
`Rma135` prefix** to avoid a latent naming collision — see below).

### ⚠️ Landmine: 2 workflows + 1 Python test

1. **`.github/workflows/rma135-resource-thermal-governor.yml`** — lists the
   exact file path in multiple `paths:` triggers and in an artifact-copy step.
2. **`.github/workflows/rma135-resource-governor-android.yml`** — a
   **regex**-based change-scope gate:
   `...(ReachyAndroidLocalLlmResourceSignalSource|ReachyRma135ResourceGovernorAcceptance)\.cs)|...`
   — must be widened to also match the new partial/helper filenames (e.g.
   `ReachyRma135ResourceGovernorAcceptance(\.[A-Za-z]+)?\.cs`), or changes to
   the split-out files silently won't trigger this workflow.
3. **`scripts/tests/test_rma135_android_acceptance_contracts.py`** — the most
   direct hazard: hardcodes `ACCEPTANCE = ROOT / ".../ReachyRma135ResourceGovernorAcceptance.cs"`,
   then does ~20 literal-substring `require()`/`forbid()` checks against that
   single file's text, including tokens (`"controlled_one_shot_budget_exceeded"`,
   `"ResourceCancelledDuringGeneration"`, `"post_load_stabilization_started"`,
   `"requiredConsecutiveAdmissible = 3"`, `"report_contains_prompt_or_response_content = false"`,
   etc.) that live inside `RunAcceptanceAsync()`/its helpers (the biggest
   moved chunk) or the DTO file, not the file that keeps the original name.

**Fix the Python test first** (glob `ReachyRma135ResourceGovernorAcceptance*.cs`
+ `Rma135Acceptance*.cs` in the `Application` directory and concatenate
before running its `require`/`forbid` checks), verify against the current
file, then widen both workflow files' path/regex filters as part of
finalization.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyRma135ResourceGovernorAcceptance.cs` (anchor) | Constants/fields, `Bootstrap()` (`[RuntimeInitializeOnLoadMethod]`), `Start()` | ~150 |
| `ReachyRma135ResourceGovernorAcceptance.Acceptance.cs` | `RunAcceptanceAsync()` (353 lines) + `WaitForProductionRuntimeAsync`, `WaitForPhysicsBudgetAsync`, `WaitForTimingProgressAsync`, `ValidateSuccessfulGeneration`, `VerifyArtifact`, `CreateSelectedManifest`, `CreateRequest` | ~570 |
| `ReachyRma135ResourceGovernorAcceptance.Diagnostics.cs` | `ReadBooleanLaunchExtra`, `ReadApiLevel`, `TryReadApiLevel`, `HandleLogMessage`, `InitializeCheckpointRun`, `WriteCheckpoint`, `TryWriteCheckpoint`, `WriteReport`, `Bound` | ~130 |
| `Rma135AcceptanceTestDoubles.cs` (new top-level, non-partial) | `Rma135PhysicsStartupStabilization`, `Rma135FaultInjectingPhysicsBudgetSource` (`ILocalLlmPhysicsBudgetSource`), `Rma135CollectingSink` (`ILocalLlmStreamSink`) — renamed from `PhysicsStartupStabilization`/`FaultInjectingPhysicsBudgetSource`/`CollectingSink` | ~105 |
| `Rma135AcceptanceReportModels.cs` (new top-level, non-partial) | `Rma135AcceptanceCheckpoint`, `Rma135AcceptanceReport`, `Rma135ArtifactVerification` (renamed from `ArtifactVerification`) | ~100 |

### Watch out for

- **Naming collision risk**: `ReachyRma134LocalLlmAcceptance.cs` (a sibling
  807-line file, same `namespace ReachyMini.Validation`) *also* declares
  private nested `CollectingSink` and `ArtifactVerification`. No clash today
  since both are `private`, but promoting RMA-135's versions to top-level
  `internal` without the `Rma135` prefix would collide with any future
  extraction of RMA-134's identically-named types. **Do the rename as part of
  this split**, updating the ~15 call sites inside `RunAcceptanceAsync`/
  `VerifyArtifact`.
- Static mutable fields (`bootstrapError`, `unhandledFailure`,
  `unhandledFailureMessage`, `checkpointSequence`, `checkpointStopwatch`) are
  shared across `Bootstrap()`/`Start()` (anchor) and `HandleLogMessage()`/
  `WriteCheckpoint()`/`InitializeCheckpointRun()` (Diagnostics) — safe under
  `partial class`, just keep the fields declared once.
- `[RuntimeInitializeOnLoadMethod]` must stay exactly on `Bootstrap()` — don't
  let it get lost/duplicated during the move.

### Extraction order

1. DTOs first → `Rma135AcceptanceReportModels.cs`, with the `ArtifactVerification` → `Rma135ArtifactVerification` rename and its ~2 call-site updates. Pure data, lowest risk.
2. Test-double helper classes → `Rma135AcceptanceTestDoubles.cs`, with the `Rma135`-prefix renames and construction-site updates. Still no behavior change.
3. `Diagnostics` partial (self-contained utility methods, only static-field dependency already established as shared).
4. `Acceptance` partial last (largest, most logic-dense — do this only after the Python contract test has already been updated per step 3's dependency, and verify compilation/a local run of the Android acceptance script or at minimum a Unity batch-mode compile).
5. Update `scripts/tests/test_rma135_android_acceptance_contracts.py` to glob across all 5 resulting files, and widen both workflow YAMLs' path/regex filters — do this in the same commit as step 4, not after, since that's the point the checks would otherwise break.

---

## 9. `AndroidOnDeviceAsrProvider.cs` (989 lines)

**Responsibility:** the RMA-121 "Android explicit on-device ASR" speech
provider — protocol DTOs (probe/support/event types), the
`IAndroidOnDeviceAsrPlatform` abstraction (implemented by a JNI bridge
elsewhere), and `AndroidOnDeviceAsrProvider` itself, adapting that platform
to the engine-wide `IAsrProvider` contract (availability preflight,
single-in-flight-operation guard, streaming `RecognizeAsync`, timeout/
cancellation, disposal).

**Mechanism:** `partial class`, matching `AndroidOfflineTtsProvider.cs`/
`.Internal.cs` and the round-1 `ReachyLocalLlmProvider.*.cs` precedent. **No
hardcoded-reference landmine found** — clean split.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `AndroidOnDeviceAsrTypes.cs` (new, not partial) | 3 enums, `AndroidOnDeviceAsrProbe`, `AndroidOnDeviceAsrSupportResult`, `AndroidOnDeviceAsrPlatformFailure`, `AndroidOnDeviceAsrPlatformEvent`, `IAndroidOnDeviceAsrPlatform` — mirrors the existing `AndroidSystemTtsTypes.cs`/`AndroidOfflineTtsTypes.cs` naming convention already used in this directory | ~215 |
| `AndroidOnDeviceAsrProvider.cs` (anchor) | Consts, fields, ctor, `Descriptor`/`Capabilities`, `CheckAvailabilityAsync`, `DisposeAsync`, `TryAcquireOperation`/`ReleaseOperation`/`ThrowIfDisposed`, `Bound` | ~180 |
| `AndroidOnDeviceAsrProvider.Recognition.cs` | `RecognizeAsync` (the ~202-line async iterator), `MapPlatformEvent`, `Failed`, `MoveNextSafelyAsync`, nested `PlatformMoveNextResult` | ~410 |
| `AndroidOnDeviceAsrProvider.Readiness.cs` | `EvaluateReadinessSafelyAsync`, `EvaluateReadinessAsync`, `MapFailure`, nested `Readiness`/`ReadinessEvaluation`/`FailureMapping` | ~310 |

### Watch out for

- Two distinct `Bound` helpers exist: the DTO's own private one (moves with
  it, untouched) and the provider's (795-803, used by `Failed` in Recognition
  and `Readiness.Available`/`Unavailable` in Readiness) — keep the provider's
  copy in the anchor as the shared cross-file helper.
- `MapFailure` (Readiness file) is called from `MapPlatformEvent` (Recognition
  file) — the one genuine cross-partial-file call; a one-line comment noting
  it is worth adding.
- `RecognizeAsync` uses `[EnumeratorCancellation]` and is compiler-transformed
  into an async-iterator state machine — mechanical to move (no field-access
  changes) but the single riskiest method due to size and `yield`/`try`/
  `finally` interaction; isolate it last and diff-test carefully.

### Extraction order

1. `AndroidOnDeviceAsrTypes.cs` (zero coupling to provider internals).
2. Mark `AndroidOnDeviceAsrProvider` `partial`, no content moved — confirms the mechanics compile.
3. `MapFailure` + nested `FailureMapping` into `.Readiness.cs` (pure/static, no instance-field dependency).
4. `MoveNextSafelyAsync` + nested `PlatformMoveNextResult` into `.Recognition.cs` (also static/self-contained).
5. `Readiness`/`ReadinessEvaluation`/`EvaluateReadinessAsync`/`EvaluateReadinessSafelyAsync` into `.Readiness.cs` (touch instance fields, but only 2 call sites — easy to diff).
6. `RecognizeAsync`/`MapPlatformEvent`/`Failed` into `.Recognition.cs` last (largest, most control-flow-heavy).
7. Final: confirm anchor only has ctor/properties/`CheckAvailabilityAsync`/`DisposeAsync`/guards/`Bound`; run `managed/ReachyMini.AndroidOnDeviceAsr.Tests` and `dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror` after each step.

---

## 10. `ReachyPresentationBuilder.cs` (957 lines)

**Responsibility:** a Unity-**Editor**-only static build tool
(`namespace ReachyMini.Editor`, `UnityEditor`/`UnityEditor.SceneManagement`)
that converts an externally-prepared "Reachy Unity render" directory
(described by a `UNITY_RENDER_MAP.json` manifest) into first-class Unity
assets: validates the manifest, creates `Material`/`Mesh` assets (including a
custom minimal `.obj` parser), assembles a `GameObject` hierarchy into a
prefab, and instantiates it into a build-ready Editor scene.

**Mechanism:** `partial class` on the `static class` — zero instance/mutable
static state (every method is a pure function of its parameters), which
makes this split mechanically the safest of the whole round. **No
hardcoded-reference landmine found** for the file itself (see the one
duplicated-constants note below, which is worth flagging but isn't a
blocker).

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyPresentationBuilder.cs` (anchor) | Class decl, constants, `BuildFromCommandLine()`, `BuildPresentation()`, `ResolveRenderRoot()`, `ReadManifest()` | ~95 |
| `ReachyPresentationBuilder.ManifestValidation.cs` | `ValidateManifest` (~150 lines), `IsSha256`, and (recommended) both `IsFinite` overloads since `ValidateManifest` is their heaviest caller | ~165 |
| `ReachyPresentationBuilder.Assets.cs` | `ReplaceGeneratedRoot`, `CreateMaterials`, `ConfigureTransparentMaterial` | ~85 |
| `ReachyPresentationBuilder.Meshes.cs` | `CreateMeshes`, `ResolveContainedPath`, `ComputeSha256` | ~90 |
| `ReachyPresentationBuilder.ObjParser.cs` | `ParseGeneratedObj`, `ParseVector3`, `ParseFiniteSingle`, `ParseFace`, `ValidateObjIndex`, nested `readonly struct ObjFace` | ~215 |
| `ReachyPresentationBuilder.PrefabBuilder.cs` | `CreatePrefab`, `ValidateBody`, `SanitizeName`, `ApplyPose`, `AssertNoUnityPhysics` | ~185 |
| `ReachyPresentationBuilder.Scene.cs` | `CreatePresentationScene` | ~60 |
| `ReachyPresentationBuilder.Manifest.Dto.cs` | All `[Serializable] private sealed class` DTOs (`RenderManifest`, `SourceEntry`, `MeshEntry`, `MaterialEntry`, `BodyEntry`, `VisualGeomEntry`, `PoseEntry`, `SourceCameraEntry`, `PresentationEntry`) | ~85 |

### Watch out for

- **Public constants (`GeneratedRoot`, `PrefabPath`, `ScenePath`)** are
  consumed externally by `ReachyPresentationPipeline.cs` and `AndroidBuild.cs`
  — as long as these stay declared anywhere in the partial class (doesn't
  matter which file) with unchanged names/values, those two consumers need
  **zero changes**. This is the strongest argument for `partial class` over
  splitting into distinct top-level types here.
- **`private` nested DTO classes are referenced from methods across multiple
  proposed files** (`ValidateManifest` needs `MeshEntry`/`MaterialEntry`/
  `BodyEntry`/`VisualGeomEntry`; `CreatePrefab` needs `RenderManifest`/
  `BodyEntry`/`VisualGeomEntry`/`PoseEntry`) — `partial class` merges them all
  into one type at compile time, so `private` visibility survives with zero
  accessibility changes needed.
- **Non-blocking duplication note** (flagged by the analysis, not a CI
  landmine): `scripts/prepare_reachy_unity_presentation.sh` hardcodes the
  fully-qualified `-executeMethod ReachyMini.Editor.ReachyPresentationPipeline.BuildFromCommandLine`
  string (points at `ReachyPresentationPipeline`, unaffected by this split)
  and *also* duplicates the `PrefabPath`/`ScenePath` constant *values* as
  separate literal shell strings. As long as the constants stay unchanged
  (per the point above), no update is needed for this split — but this
  duplication is a pre-existing, unrelated latent drift risk worth mentioning
  to the user, not fixing here.
- `.github/workflows/ci.yml` has an inline Python block that re-implements
  (duplicates) some of `ValidateManifest`'s same business rules against the
  *generated JSON output* rather than the C# source — not a literal-text
  landmine, but if `ValidateManifest`'s behavior (not just its location)
  ever changes, that independent copy could silently diverge. Not relevant
  to a pure-move split, just a note for future work.

### Extraction order

1. DTOs → `ReachyPresentationBuilder.Manifest.Dto.cs` (zero logic, purely data — validates the partial-class mechanics with no behavioral risk).
2. `ObjFace` + OBJ parser → `.ObjParser.cs` (self-contained).
3. Materials/folder helpers → `.Assets.cs`.
4. Mesh asset creation → `.Meshes.cs` (depends on step 2's parser, resolved transparently since it's all one merged type).
5. Manifest validation → `.ManifestValidation.cs` (largest single block — move `IsFinite` overloads here too).
6. Prefab builder → `.PrefabBuilder.cs` (after step 5, since `ApplyPose` depends on `IsFinite`).
7. Scene builder → `.Scene.cs` (fully self-contained).
8. Final: confirm the anchor only has class decl/constants/`BuildFromCommandLine`/`BuildPresentation`/`ResolveRenderRoot`/`ReadManifest` (~95 lines) and the public-const surface is untouched. Recompile the Unity project and, ideally, run `scripts/prepare_reachy_unity_presentation.sh` once end-to-end to confirm the full `-executeMethod` pipeline still produces a valid prefab/scene.
