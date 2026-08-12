# Large File Split/Refactor TODO — Round 3

This is the third round of the large-file-splitting effort. Round 1
(`docs/LARGE_FILE_REFACTOR_TODO.md`) and round 2
(`docs/LARGE_FILE_REFACTOR_TODO_2.md`) each split a top-10 by line count down
to under 800 lines; this document covers the *new* top-10 that surfaced once
those were split. It follows the exact same process and ground rules as
rounds 1 and 2 — read round 1's "Ground rules" section for full context on
tooling/conventions; the summary is repeated below.

**New this round**: the first C file (`rma133_benchmark.c`) to ever appear in
this effort — see its section for C-specific mechanics (translation-unit
boundaries, `static`/`extern` visibility, a shared header, and a hand-written
NDK build script instead of CMake).

## Ground rules (same as rounds 1–2)

- **Process files one at a time, in the order listed below.** Do not start
  file N+1 until file N's split is committed and the relevant subset of
  `./scripts/ci.sh` (native/managed/Android build+test, or `python3 -m
  unittest discover -s scripts/tests` for Python) is green.
- **Target: every resulting file under 800 lines.**
- **No behavior changes.** Pure move/reorganize refactors — same types, same
  members, same signatures, same namespaces/packages/public API. Don't fix
  bugs or rename things in the same commit; file that as separate follow-up
  work (the one narrow exception found in rounds 1–2: a genuinely necessary
  visibility change like `private` → package-private/`internal` when a member
  is promoted out of a nested/enclosing scope, or a mandatory rename to avoid
  a real naming collision — do only the minimum needed, see file #10 below).
- **Commit incrementally**, following the suggested extraction order below
  (smallest/safest pieces first), verifying build and tests pass after each
  step.
- Per `CLAUDE.md`, commits land directly on `master`, prefixed `RMA-<n>:` —
  file or ask for a ticket number before starting if one doesn't exist for
  this cleanup. (Rounds 1–2's commits used plain conventional-commit prefixes
  instead, following this session's established precedent — continue that
  unless told otherwise.)
- Where a file has framework-specific mechanics (Unity `.meta` files, C#
  `partial class`, MonoBehaviour serialization, Python sibling-file loading,
  or — new this round — C translation-unit `static`/`extern` boundaries),
  follow the guidance in that file's section.

## ⚠️ The single most important lesson from rounds 1–2

**Every file in this round except three (`AndroidSystemTtsProvider.cs`,
`Rma110VisionProviderContracts.cs`, and `AndroidOnDeviceAsrTests.cs`) has at
least one test or CI workflow that reads the file's exact literal source
text** — by hardcoded path, and/or by grepping for specific method names,
constants, or string fragments — **to verify a "contract" is still present in
the code.** This is *not* a hypothetical risk: in round 1, six separate files
hit this exact landmine, and round 2 found it in eight of its ten. **This is a
genuinely dangerous, no-warning failure mode**: the code still compiles and
the class still works — the private member the check was grepping for is now
just in a different file — but the CI gate (or, worse, the *local* test)
throws an assertion/`SystemExit` (or, in one case this round, an **uncaught
`IndexError`** — see file #7) because its `File.ReadAllText`/
`Path(...).read_text()` call only ever looked at one path.

**For every file below with a landmine noted, fix the test/CI check FIRST,
as its own prerequisite commit, before touching the actual split** — verify
the fix passes against the *current, unsplit* file, then proceed. The fix is
almost always the same shape: replace the single hardcoded
`File.ReadAllText(path)` / `Path(...).read_text()` with a loop that
concatenates every file in the target directory matching a naming pattern
that covers the planned split (a `Directory.GetFiles(dir, "Prefix*.cs")` glob
in C#, or an explicit list / `Path(...).glob(...)` in Python), then re-run
the check to confirm nothing regressed before starting.

**File #10 (`ReachyRma134LocalLlmAcceptance.cs`) also carries a *mandatory*
naming-collision rename** — not a CI-text landmine but a genuine C# compile
error waiting to happen, inherited from round 2's RMA-135 split. Read its
section before starting.

## Progress tracker

| # | File | Lines | Landmine? | Status |
|---|---|---|---|---|
| 1 | `Assets/ReachyMini/Runtime/Core/Speech/AndroidSystemTtsProvider.cs` | 953 | No | ☑ Done (commit `af99fbe`) |
| 2 | `managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs` | 921 | No (CI check already directory-glob-based) | ☑ Done (commit `33a43d3`) |
| 3 | `scripts/calibration_data.py` | 919 | Soft only (rma070 CI path trigger — no in-file grep) | ☑ Done (commits `8ebcfe4`, `076c197`) |
| 4 | `scripts/calibration_profile_approval.py` | 875 | **Yes** (rma074 CI path trigger + `compileall` file list) | ☑ Done (commits `257f272`, `919acfa`) |
| 5 | `native/llama_runtime/benchmark/rma133_benchmark.c` | 858 | **Yes — build script + Python contract test** | ☑ Done (commits `d69b191`, `7848238`) |
| 6 | `managed/ReachyMini.LocalLlm.Tests/Program.cs` | 853 | **Yes** (rma134 `sha256sum` evidence list) | ☑ Done (commit `669592e`) |
| 7 | `Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraAcquisition.cs` | 834 | **Yes — 2 workflows** (rma091, rma104 — one risks an uncaught `IndexError`) | ☐ Not started |
| 8 | `managed/ReachyMini.AndroidOnDeviceAsr.Tests/AndroidOnDeviceAsrTests.cs` | 831 | No (CI check already directory-glob-based) | ☐ Not started |
| 9 | `Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraTextureBridge.cs` | 812 | **Yes** (rma091 workflow, single `read_text`) | ☐ Not started |
| 10 | `Assets/ReachyMini/Runtime/Application/ReachyRma134LocalLlmAcceptance.cs` | 807 | **Yes — 2 workflows + mandatory naming-collision rename** | ☐ Not started |

Mark each row `☑ Done (commit <hash>)` as it lands.

---

## 1. `AndroidSystemTtsProvider.cs` (953 lines)

**Responsibility:** the RMA-124 "Android system/network TTS" speech provider — `AndroidSystemTtsProvider` adapts an `IAndroidSystemTtsPlatform` (a JNI bridge implemented elsewhere, `ReachyAndroidSystemTtsPlatform.cs` in `Runtime/Application`) to the engine-wide `ITtsProvider` contract: availability preflight, voice enumeration, network-voice explicit-opt-in gating, a single-in-flight-operation guard, streaming `SpeakAsync`, timeout/cancellation, and disposal. Unlike its round-2 ASR sibling, this file's DTOs are *already* extracted into `AndroidSystemTtsTypes.cs` — this file is purely the provider class itself (mirroring `AndroidOfflineTtsProvider.cs`'s post-split shape).

**Mechanism:** `partial class`, matching the `AndroidOfflineTtsProvider.cs`/`.Internal.cs` precedent exactly (same directory, same RMA-12x speech-provider shape, same field/method inventory: ctor, `Descriptor`/`Capabilities` properties, `CheckAvailabilityAsync`, `GetVoicesAsync`, `SpeakAsync`, `DisposeAsync`, static `SelectVoice`, plus a matching set of private helpers and private nested evaluation/result classes). `AndroidOfflineTtsProvider.cs`'s anchor keeps **every public member** (406 lines) and its `.Internal.cs` holds every private helper and nested class (633 lines) — this file should follow the identical 2-way split. No shared mutable state beyond the same three fields already present in both siblings (`lifetimeCancellation`, `operationInFlight`, `disposed`), which are read from both the public async methods and the private helpers — `partial class` is required for that reason, exactly as in `AndroidOfflineTtsProvider`. No naming-collision risk: every type here is `AndroidSystemTts*`-prefixed and there is no sibling in the `ReachyMini.Speech` namespace reusing those names.

### Landmine check

**None found** — verified by grepping the exact filename and the bare type name across the whole repo:

- `managed/ReachyMini.AndroidSystemTts.Tests/AndroidSystemTtsTests.cs` has a `Read(string relativePath)` helper, but its three call sites read the Java JNI bridge sources and `ReachyAndroidSystemTtsPlatform.cs` — **none read `AndroidSystemTtsProvider.cs` itself.** The rest of the file's `AndroidSystemTtsProvider` references are ordinary compiled member access, which survive a `partial class` split unchanged.
- `.github/workflows/rma124-android-system-tts.yml`'s `paths:` triggers already use the **directory-wide** glob `'Assets/ReachyMini/Runtime/Core/Speech/**'` — no trigger-coverage fix needed.
- `scripts/tests/test_provider_source_set_integrity.py` and `test_rma145_openai_compatible_tts.py` both hardcode reads of *other* files in the same directory (`BufferedAsrUtteranceContracts.cs`, `BufferedTtsAudioContracts.cs`) — not this file.
- No `sha256sum`/hash-recording step anywhere references this filename.

Compiled via `managed/ReachyMini.Core/ReachyMini.Core.csproj`'s glob include (`Runtime/Core/**/*.cs`) — no `.csproj` edit needed for new sibling files. Verify with `dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror` and `dotnet run --project managed/ReachyMini.AndroidSystemTts.Tests/ReachyMini.AndroidSystemTts.Tests.csproj --configuration Release` (the exact two commands the workflow runs) after each step.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `AndroidSystemTtsProvider.cs` (anchor) | Consts, fields, ctor, `Descriptor`/`Capabilities`/`ConfiguredLanguageTag`/`ExplicitlySelectedNetworkVoiceId` properties, `CheckAvailabilityAsync`, `GetVoicesAsync`, `SpeakAsync` (the ~210-line async iterator), `DisposeAsync`, static `SelectVoice` | ~435 |
| `AndroidSystemTtsProvider.Internal.cs` | `AvailabilityFromProbe`, `ProbeSafelyAsync`, `LoadVoiceCatalogSafelyAsync`, `BuildVoiceList`, `ValidateRequestedVoice`, `MapPlatformEvent`, `MapFailure`, `CancellationOrTimeout`, `Failed`, `TryAcquireOperation`/`ReleaseOperation`/`ThrowIfDisposed`, `CompareVoice`, `Bound`, `MoveNextSafelyAsync`, nested `ProbeEvaluation`, `VoiceCatalogEvaluation`, `VoiceValidation`, `FailureMapping`, `PlatformMoveNextResult` | ~525 |

Both land comfortably under 800 with a plain 2-way split, matching `AndroidOfflineTtsProvider.cs`'s (406) / `.Internal.cs`'s (633) precedent line-for-line.

### Watch out for

- **`[EnumeratorCancellation]` async iterator**: `SpeakAsync` (~210 lines) is a compiler-transformed async-iterator state machine — mechanical to move (no field-access changes needed) but the single riskiest, most control-flow-dense method in the file. Keep it in the anchor per the target table above, matching `AndroidOfflineTtsProvider.cs`'s precedent.
- Three instance fields (`lifetimeCancellation`, `operationInFlight`, `disposed`) are shared across both files — fine under `partial class`, just declare them once, in the anchor.
- `MapFailure` (private, static, `.Internal.cs`) is called from `MapPlatformEvent` (also `.Internal.cs`) — both land in the same file so this is a same-file call, no comment needed.
- `AndroidSystemTtsProvider.DefaultMaximumInputCharacters`/`ProviderId`/`ProviderVersion` consts are referenced externally by `FakeAndroidSystemTtsPlatform.cs` and `AndroidSystemTtsTests.cs` — as long as they stay declared anywhere in the partial class (the anchor), those external call sites need zero changes.
- No `[Serializable]` DTOs live in this file (already in `AndroidSystemTtsTypes.cs`) and no visibility changes are needed anywhere.

### Extraction order

1. Add `partial` to `AndroidSystemTtsProvider`'s class declaration only — no code moved yet, verify baseline build.
2. Move the five self-contained nested private classes (`ProbeEvaluation`, `VoiceCatalogEvaluation`, `VoiceValidation`, `FailureMapping`, `PlatformMoveNextResult`) into `.Internal.cs` first — pure data/result types, zero control-flow risk.
3. Move the static/near-static helpers next: `MapFailure`, `CompareVoice`, `Bound`, `TryAcquireOperation`/`ReleaseOperation`/`ThrowIfDisposed`, `MoveNextSafelyAsync` — small, low-coupling.
4. Move `AvailabilityFromProbe`, `ProbeSafelyAsync`, `LoadVoiceCatalogSafelyAsync`, `BuildVoiceList`, `ValidateRequestedVoice`, `MapPlatformEvent`, `CancellationOrTimeout`, `Failed` into `.Internal.cs` — each has only 1-2 call sites in the still-monolithic anchor, easy to diff one at a time.
5. Confirm the anchor now contains only ctor/properties/`CheckAvailabilityAsync`/`GetVoicesAsync`/`DisposeAsync`/static `SelectVoice`/`SpeakAsync` — **leave `SpeakAsync` for last and diff-test it carefully**, line-by-line against the pre-split version rather than trusting a clean compile alone.
6. Final: run the build+run commands after each step; no CI/test-file fixes are needed before or after, since none was found.

---

## 2. `Rma110VisionProviderContracts.cs` (921 lines)

**Responsibility:** the RMA-110 vision-provider-executor contract test suite — 10 test cases covering provider descriptor/capability validation, transformed-frame ownership/coverage/validity requirements, frame-source raw-fallback and stale-sequence rejection, caller cancellation, timeout quarantine, provider-fault visibility, provider-switch supersession, result-identity mismatch fail-closed behavior, cloud network-disclosure gating, and exactly-once frame-resource disposal. It is invoked from the sibling console-runner `managed/ReachyMini.Camera.Tests/Program.cs`, whose `Main()` calls `await Rma110VisionProviderContracts.RunAsync().ConfigureAwait(false)` as one step among 12. Baseline run confirmed locally: build succeeds with 0 warnings/errors, and the console pass banner ends with `RMA-110 vision provider contracts passed.` followed by `RMA-111 lightweight tracking contracts passed.` and `RMA-090/RMA-091/RMA-100 camera contracts passed.`

**Mechanism:** a plain (non-`partial`) `internal static class Rma110VisionProviderContracts` whose own `internal static async Task RunAsync()` explicitly calls all 10 test methods in sequence, printing one summary line at the end — no `[ModuleInitializer]`, no case counter. None of the 9 sibling files in this directory are declared `partial` today — this file would be the first, and only because (unlike its siblings) its 10 test methods share heavy fan-in into a common set of private helper factories, 5 duplicated assertion helpers, and 4 nested `private sealed class` test doubles, all of which must stay reachable from every test-group file after the split.

### Landmine check

**None found that block a split** — the workflow that reads this file is already split-safe:

- `.github/workflows/rma110-vision-provider-contracts.yml` (lines 90–228) concatenates *every* `Rma110*.cs` file in `managed/ReachyMini.Camera.Tests/` (a directory glob, `sorted((root / '...').glob('Rma110*.cs'))`) before checking for 14 required test tokens and forbidding `[ModuleInitializer]`/`GetAwaiter().GetResult()`. As long as every new split-out file keeps the `Rma110` filename prefix, this check needs **zero changes**.
- The same workflow separately reads `Program.cs` by exact path and checks for the literal `await Rma110VisionProviderContracts.RunAsync()` — unaffected by a pure partial-class split (class name/method signature preserved).
- No other workflow or `scripts/tests/*.py` file references this file.

**Conclusion: clean, no CI-fix prerequisite needed.**

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `Rma110VisionProviderContracts.cs` (anchor) | usings, `internal static partial class Rma110VisionProviderContracts` decl, `RunAsync()` dispatch only | ~35 |
| `Rma110VisionProviderContracts.Assertions.cs` | `Contains`, `True`, `False`, `Equal<T>`, `Throws<TException>` | ~55 |
| `Rma110VisionProviderContracts.Fixtures.cs` | `Descriptor`, `TrackerCapability`, `VisionLanguageCapability`, `Context`, `Frame`, `RawFrame`, `WaitForTrackerCancellationAsync`, `ThrowTrackerFailure` | ~145 |
| `Rma110VisionProviderContracts.Fakes.cs` | `FakeResources`, `FakeFrameSource`, `FakeTracker`, `FakeVisionLanguageProvider` (all 4 nested `private sealed class` test doubles) | ~180 |
| `Rma110VisionProviderContracts.CapabilityAndFrameTests.cs` | `ProviderKindsAndCapabilitiesRemainExplicit`, `TransformedFramesRequireOwnedColorValidityAndCoverageAsync`, `FrameResourcesDisposeExactlyOnceAsync` | ~145 |
| `Rma110VisionProviderContracts.FrameSourceTests.cs` | `FrameSourceRejectsRawFallbackAndStaleSequenceAsync` | ~80 |
| `Rma110VisionProviderContracts.TrackingLifecycleTests.cs` | `CallerCancellationReturnsTypedFailureAsync`, `TimeoutQuarantinesProviderAsync`, `ProviderFaultRemainsVisibleAsync`, `ProviderSwitchSupersedesLateResultsAsync`, `ResultIdentityMismatchFailsClosedAsync` | ~255 |
| `Rma110VisionProviderContracts.VisionLanguageTests.cs` | `CloudDisclosureIsRequiredBeforeInvocationAsync` | ~55 |

All 8 files land well under 800; the largest (`TrackingLifecycleTests`) is under a third of the current file's size.

### Watch out for

- **Requires converting the class to `partial`** — unlike most of this directory's other `Rma1XX*.cs` files, because nearly every test method calls the shared `Descriptor`/`Context`/`Frame`/`RawFrame` factories, the 5 assertion helpers, and 3 of the 4 fake types.
- **Duplicated assertion helpers, pre-existing, not a landmine, don't "fix" it here**: `Contains`/`True`/`False`/`Equal<T>`/`Throws<TException>` are already copy-pasted verbatim into `Program.cs` and most of the other `Rma1XX*.cs` files in this directory — repo-wide convention, not something to clean up as part of this pure-move split.
- **No case-count safety net to preserve or break**: `RunAsync()` calls its 10 test methods directly with no counter, and no doc claims a specific test-case count — don't invent an `ExpectedCaseCount` as part of this split.
- `FakeTracker`/`FakeVisionLanguageProvider` constructors call `TrackerCapability()`/`VisionLanguageCapability()` (the `Fixtures.cs` factories) — a genuine cross-file dependency from `Fakes.cs` back into `Fixtures.cs`; harmless under `partial class` but worth a one-line comment once split.
- No static/shared mutable state anywhere in this file — lower risk than the `partial`-class splits in round 2 that had a shared `current`/counter field.

### Extraction order

1. Add `partial` to the class declaration only — no code moved yet; rebuild and re-run to confirm the baseline pass banner is unchanged.
2. `Rma110VisionProviderContracts.Assertions.cs` (zero dependencies, leaf utilities).
3. `Rma110VisionProviderContracts.Fixtures.cs` (depends only on production `ReachyMini.Perception` types, not on the fakes).
4. `Rma110VisionProviderContracts.Fakes.cs` (depends on step 3's `TrackerCapability`/`VisionLanguageCapability`).
5. `Rma110VisionProviderContracts.FrameSourceTests.cs` (single test method, smallest test-group extraction).
6. `Rma110VisionProviderContracts.CapabilityAndFrameTests.cs`.
7. `Rma110VisionProviderContracts.VisionLanguageTests.cs`.
8. `Rma110VisionProviderContracts.TrackingLifecycleTests.cs` last (largest test group, 5 methods, most interdependent).
9. Final: confirm the anchor contains only the class decl + `RunAsync()`; rebuild/rerun and diff against the captured baseline banner; also re-run the RMA-110 workflow's Python contract-verification snippet locally against the split files to confirm the glob-based check still passes untouched.

---

## 3. `calibration_data.py` (919 lines)

**Responsibility:** the RMA-070 versioned calibration-dataset contract module — schema/version constants and pinned schema hashes, a fail-closed `ImportLimits` resource-bound dataclass, deep structural validation of an untrusted calibration dataset (robot/environment/capture/clocks/clock-alignments, 8 sample-type-specific stream validators, source-file manifest), canonical JSON hashing and dataset finalization/integrity verification, and strict duplicate-key/non-finite-safe JSON loading helpers.

**Mechanism: confirmed** (via grep, not assumed) to be loaded the exact same dual way as `calibration_fitting.py`/`calibration_experiment.py` were in rounds 1–2:
- **Plain `from calibration_data import (...)`** from `scripts/capture_reachy_calibration.py` and `scripts/validate_calibration_dataset.py`, and a plain `import calibration_data` attempted first inside `scripts/calibration_fitting_jsonio.py`'s `_load_calibration_data()`.
- **`importlib.util.spec_from_file_location(...)` by explicit path** from `scripts/tests/test_calibration_data.py`, `scripts/tests/test_calibration_capture.py`, `scripts/generate_rma073_synthetic_data.py`, and the fallback branch of `calibration_fitting_jsonio.py`'s `_load_calibration_data()`.

**Reuse the exact same facade + `_load_sibling` bootstrap pattern**, not a plain package. Note: `calibration_data` is **not** part of the `calibration_fitting_*` family — it's an independent module that `calibration_fitting_jsonio.py` merely *consumes* (its bootstrap comment "owns the calibration_data sibling-loading bootstrap" refers to `calibration_fitting_jsonio.py` owning the *loading code*, not to `calibration_data.py` being one of its own siblings). `calibration_fitting_datasets.py` also depends on it transitively via `calibration_data = calibration_fitting_jsonio.calibration_data`, touching only `calibration_data.load_json_file` and `calibration_data.validate_dataset`.

### Landmine check

| Hit | Classification |
|---|---|
| `.github/workflows/rma070-calibration-data.yml` — `paths:` trigger lists `'scripts/calibration_data.py'` literally (push and pull_request blocks) | **Soft** — trigger-coverage only, no content grep. |
| `scripts/tests/test_calibration_data.py`, `test_calibration_capture.py`, `generate_rma073_synthetic_data.py`, `calibration_fitting_jsonio.py`'s fallback — all load this file by path with the literal filename `calibration_data.py` | **Soft / no-op** — these are the module's own dual-load call sites, not source-text greps. Needs no change as long as the facade keeps this exact filename. |

**No hard landmine found** — no workflow greps this file's source text for literal tokens, and no `sha256sum` step hashes it. The one real requirement is: **the facade must keep the literal filename `scripts/calibration_data.py`** — do not relocate the "final" contents to a renamed file the way round 2's file #7 did.

### Target files

| File | Contents | Depends on | Est. lines |
|---|---|---|---|
| `calibration_data_contracts.py` | Constants (`CONTRACT_ID`…`TOP_LEVEL_KEYS`), `CalibrationValidationError`, `ImportLimits`/`DEFAULT_LIMITS`, `canonical_json_bytes`, `_error`, `_require_dict`, `_require_list`, `_require_exact_keys`, `_require_string`, `_require_id`, `_require_bool`, `_require_integer`, `_require_number`, `_require_nullable_number`, `_require_vector`, `_validate_iso_utc`, `_validate_hash` | stdlib only | ~250 |
| `calibration_data_metadata.py` | `_validate_schema`, `_validate_register_value`, `_validate_robot`, `_validate_environment`, `_validate_capture`, `_validate_clocks`, `_validate_alignments` | contracts | ~260 |
| `calibration_data_samples.py` | `_validate_common_sample`, `_validate_command`/`_joint`/`_current_load`/`_voltage`/`_imu`/`_external_pose`/`_force_torque`/`_temperature`, `_SAMPLE_VALIDATORS`, `_validate_streams`, `_validate_source_files` | contracts | ~300 |
| `calibration_data_integrity.py` | `compute_dataset_sha256`, `finalize_dataset`, `validate_dataset` (orchestrator), `load_json_text`, `load_json_file`, `schema_descriptor` | contracts, metadata, samples | ~150 |
| `calibration_data.py` (facade) | Docstring on the dual-loading rationale, `_load_sibling` helper, ordered loads, re-export bindings for every public name | all 4 siblings | ~100 |

No genuine dependency cycle exists — unlike `calibration_experiment.py`'s `validate_plan`/`compile_plan` pair, `validate_dataset` is a strictly one-directional orchestrator: it calls into the metadata and sample validators, neither of which calls back. `calibration_data_metadata.py` and `calibration_data_samples.py` are mutually independent (both depend only on contracts) and can be extracted in either order.

### Watch out for

- **Keep the facade's filename exactly `calibration_data.py`.** Four call sites hardcode this literal path, plus the `rma070-calibration-data.yml` `paths:` trigger.
- **No private-helper collision risk despite identical names.** `calibration_fitting_validation.py` already defines its own `_error`/`_require_dict`/etc. with its own distinct `ImportLimits`/`DEFAULT_LIMITS`. Not a namespace clash — each `_load_sibling` call registers under its own distinct `sys.modules` key. Just don't reuse an existing `calibration_fitting_*` module name.
- `calibration_data.ImportLimits` and `calibration_fitting_validation.ImportLimits` are semantically different contracts that happen to share a class name — don't consolidate them during this split.
- `_validate_robot` calls `canonical_json_bytes` and `compute_dataset_sha256`/`finalize_dataset` call it too — make sure `calibration_data_contracts.py` loads before `calibration_data_metadata.py`/`calibration_data_integrity.py` in the facade's bootstrap order.
- `_SAMPLE_VALIDATORS` is a private module-level dict keyed by `sample_type`, consumed only by `_validate_streams` in the same file — keep both in `calibration_data_samples.py`, don't split the dispatch table from its dispatcher.

### Extraction order

1. `calibration_data_contracts.py` (pure leaf: constants, error class, `ImportLimits`, primitives, `canonical_json_bytes` — zero internal deps).
2. `calibration_data_metadata.py` (schema/robot/environment/capture/clocks/alignments validators — depends only on contracts).
3. `calibration_data_samples.py` (sample-type validators, `_SAMPLE_VALIDATORS`, `_validate_streams`, `_validate_source_files` — depends only on contracts; safe in either order relative to step 2).
4. `calibration_data_integrity.py` last (`compute_dataset_sha256`, `finalize_dataset`, `validate_dataset` orchestrator, `load_json_text`/`load_json_file`, `schema_descriptor` — highest fan-in).
5. Finalize the facade (confirm the filename stays `scripts/calibration_data.py`, widen `rma070-calibration-data.yml`'s two `paths:` entries to `'scripts/calibration_data*.py'`) + run `ruff check`/`ruff format --check` + the full `python3 -m unittest discover -s scripts/tests` suite.

---

## 4. `calibration_profile_approval.py` (875 lines)

**Responsibility:** the RMA-074 physical calibration *approval* module — strict validation and cross-binding of an RMA-073 fit candidate against physical preflight evidence, dataset provenance, and a held-out physical validation report; deterministic Ed25519 signing/verification of the resulting unit-specific approval document (via `openssl`); and fail-closed resolution of the user-facing "Calibrated for this unit" / "Uncalibrated" UI label.

**Mechanism: confirmed** (via grep, not assumed) to be loaded the same dual way as `calibration_fitting.py`/`calibration_experiment.py` — plain `from calibration_profile_approval import (...)` from `scripts/resolve_calibration_mode.py` and `scripts/approve_calibration_profile.py`, **and** `importlib.util.spec_from_file_location(...)` by explicit path from `scripts/tests/test_rma074_calibration_profile_approval.py`. **Reuse the exact same facade + `_load_sibling` bootstrap pattern**, not a new subpackage.

This file is itself a *consumer* of `calibration_fitting.py`'s dual-loading pattern: `_verify_candidate_default` loads `scripts/calibration_fitting.py` by explicit path via its own `importlib.util.spec_from_file_location(...)` call, registering it under `"_rma073_calibration_fitting"`. This is a one-way, read-only dependency (calls `module.verify_profile(...)`) and is unaffected by splitting this file itself — preserve it verbatim in whichever sibling ends up owning `_verify_candidate_default`.

### Landmine check

- **Hard landmine** — `.github/workflows/rma074-approval-contract.yml` hardcodes `'scripts/calibration_profile_approval.py'` as a `paths:` trigger filter, **and** inside a `python3 -m compileall -q scripts/calibration_profile_approval.py scripts/approve_calibration_profile.py scripts/resolve_calibration_mode.py` step. `compileall` is an explicit file list, not a glob — after the split, the new sibling files will silently never be syntax-checked by this workflow unless the list is widened. Fix both: widen the `paths:` entry to `'scripts/calibration_profile_approval*.py'`, and widen the `compileall` invocation to include every new sibling file.
- **Soft/informational** — `scripts/calibration_fitting.py` and `docs/LARGE_FILE_REFACTOR_TODO.md` mention `calibration_profile_approval.py` by name as a downstream consumer. No action needed.
- `scripts/tests/test_rma074_calibration_profile_approval.py` hardcodes the facade path and loads it via `spec_from_file_location` — no change needed as long as the facade keeps re-exporting every name it references (`create_approval`, `verify_approval`, `resolve_calibration_label`, etc.).
- No `sha256sum` steps anywhere reference this file.

### Target files table

| File | Contents | Depends on | Est. lines |
|---|---|---|---|
| `calibration_profile_approval_validation.py` | Constants (`CONTRACT_ID`…`BLOCKED_APPROVAL_PUBLIC_KEY_SHA256`), `ApprovalValidationError`, `_error`, `_require_dict`, `_require_list`, `_require_exact_keys`, `_require_string`, `_require_id`, `_require_hash`, `_require_bool`, `_require_int`, `_require_number`, `_validate_utc`, `_strict_object_pairs`, `strict_json_loads`, `load_json_file`, `canonical_json_bytes` | stdlib only | ~230 |
| `calibration_profile_approval_evidence.py` | `_candidate_hash`, `_candidate_datasets`, `_validate_preflight`, `_validate_dataset_evidence`, `_validate_metric`, `_validate_heldout_report` | validation | ~240 |
| `calibration_profile_approval_signing.py` | `compute_approval_sha256`, `signature_payload_bytes`, `_openssl_sign`, `_openssl_verify` | validation | ~75 |
| `calibration_profile_approval_core.py` | `_verify_candidate_default` (loads `calibration_fitting.py` by path), `create_approval`, `verify_approval` | validation, evidence, signing | ~290 |
| `calibration_profile_approval_labeling.py` | `LabelResolution` (dataclass + `to_document`), `resolve_calibration_label` | core (calls `verify_approval`) | ~55 |
| `calibration_profile_approval.py` (facade, kept) | Docstring on the dual-loading rationale, `_load_sibling` bootstrap, ordered loads, re-export bindings for every public name | all 5 siblings | ~110 |

### Watch out for

- **A genuine one-way (not cyclic) call dependency**: `create_approval` calls `verify_approval` as a mandatory self-check on the document it just built and signed; `verify_approval` never calls `create_approval` back. **Keep `_verify_candidate_default`, `create_approval`, and `verify_approval` together in one `_core.py` module** rather than splitting create/verify apart — they also share nearly every other dependency.
- `_openssl_sign` and `_openssl_verify` are side-effecting subprocess calls to the system `openssl` binary via temp files — keep them together in `_signing.py`; a mismatch in temp-file handling here is a correctness landmine, not just a style one.
- **Private helper name collisions are expected, not new risk**: `_error`, `_require_dict`, etc. already exist under identical names in both `calibration_fitting_validation.py` and `calibration_experiment_contracts.py`. Safe duplication since each sibling registers under a distinct `sys.modules` key — but name the new module `calibration_profile_approval_validation` (not a generic `validation.py`) to keep the key distinct.
- **Naming inconsistency to preserve, not "fix" mid-split**: this file calls its integer-validation primitive `_require_int`, whereas the equivalent primitives elsewhere are named `_require_integer`. Carry `_require_int` over unchanged.
- `resolve_calibration_label` is the only consumer of `verify_approval` outside `create_approval`'s self-check — make sure `calibration_profile_approval_labeling.py` loads strictly after `calibration_profile_approval_core.py` in the facade's `_load_sibling` order.
- `BLOCKED_APPROVAL_PUBLIC_KEY_SHA256` is checked in *both* `create_approval` and `verify_approval` — keep the constant in `_validation.py` and import it, don't duplicate the literal.
- `_verify_candidate_default` is only ever called from inside `create_approval` (confirmed via grep) — fold it into `_core.py` alongside `create_approval` rather than giving it its own file.

### Extraction order

1. `calibration_profile_approval_validation.py` (pure stdlib, zero internal deps).
2. `calibration_profile_approval_signing.py` (depends only on validation — run the existing signing/verification tests immediately after, since a payload/temp-file mismatch here fails silently as a bad signature rather than an import error).
3. `calibration_profile_approval_evidence.py` (depends only on validation).
4. `calibration_profile_approval_core.py` (depends on validation, signing, evidence — kept together per the one-way-dependency note above).
5. `calibration_profile_approval_labeling.py` (depends on core).
6. Finalize the facade: bootstrap + ordered `_load_sibling` calls + re-exports of every public name; widen `.github/workflows/rma074-approval-contract.yml`'s `paths:` trigger and `compileall` file list to cover the new siblings; then run `ruff check scripts/`/`ruff format --check scripts/`, followed by the full `python3 -m unittest discover -s scripts/tests` suite.

---

## 5. `rma133_benchmark.c` (858 lines)

**Responsibility:** the RMA-133 "V1" local-LLM candidate benchmark harness — a standalone POSIX C executable (linked only against `libreachy_llama.so` and libc/libm) that loads a GGUF model through the `reachy_llama` ABI, streams generations for a fixed TSV list of "behavior cases" against a system prompt, and emits one newline-delimited JSON record per model-load/case/summary event (timing, token counts, decode rate, peak RSS, battery temperature) to stdout for `scripts/score_rma133_benchmark.py` to consume. It enforces a fail-closed thermal safety stop (aborts before model load and before each case if battery temperature is unavailable or at/above a caller-supplied threshold) and treats a missing/degraded telemetry read as a hard failure rather than silently continuing.

**Mechanism:** this file has **zero file-scope static/global mutable state** — every one of its 19 `static` functions operates purely on parameters and locals, so there is no `partial`-class-style shared-field problem to solve; splitting is pure function relocation. What it *does* need is a small shared internal header, because most of the 19 `static` helpers are called from more than one logical group and C enforces translation-unit boundaries strictly (a `static` function is invisible outside its own `.c` file — there is no C# `internal`/assembly-wide escape hatch). Propose one new header, **`native/llama_runtime/benchmark/rma133_benchmark_internal.h`** (private to this benchmark, not installed, distinct from the public `include/reachy_llama.h` ABI contract header), holding `extern` prototypes for exactly the functions that cross the planned file boundaries. Every new `.c` file includes both `reachy_llama.h` and this new internal header. All three macros (`POLL_INITIAL_CAPACITY`, `RESPONSE_INITIAL_CAPACITY`, `MAX_CASE_LINE_BYTES`) each have every use-site landing in a single target file, so none need to move into the shared header.

### Build system landmine check

**This file has no CMake target at all — the usual "hardcoded `add_executable` file list" landmine doesn't apply in its usual form, but a worse variant does.** `native/llama_runtime/CMakeLists.txt` only builds the `reachy_llama` shared library and three C++ contract-test executables — grepping the whole `native/` tree for `rma133` inside any `CMakeLists.txt` returns nothing. `./scripts/build_native.sh`/`./scripts/ci.sh` therefore **never compiles this file at all**, and **no ctest exists for it**.

The actual build mechanism is a hand-written Android NDK cross-compile shell script that invokes the compiler directly with a single hardcoded source argument:

- **`scripts/build_rma133_android_benchmark.sh`** — `SOURCE_FILE="${ROOT_DIR}/native/llama_runtime/benchmark/rma133_benchmark.c"`, then passes `"${SOURCE_FILE}"` as the *sole* `.c` argument to a direct `clang ... -o "${OUTPUT_DIR}/rma133_benchmark"` invocation (no CMake, no Makefile). **Hard landmine**: after the split, this line must become a list of every resulting `.c` file, or the binary silently fails to link with "undefined reference" errors for every function now defined in a sibling file.
- **`scripts/tests/test_rma133_benchmark_contract.py`** — `BENCHMARK_SOURCE = ROOT / "native/llama_runtime/benchmark/rma133_benchmark.c"`, read as a single file. `test_benchmark_has_no_network_or_model_fallback_path` asserts against that one file's full text (`"http://"`/`"https://"` absent, `"fallback"`/`"cloud"` absent, `"thermal safety"`/`"response_bytes_hex"`/`"json_hex_bytes(stdout, response, response_length);"` present). **Hard landmine, and it runs in CI** — `.github/workflows/rma133-local-llm-benchmark.yml`'s "Validate historical and V6 contracts" step invokes this test. **Fix first**: change `source = BENCHMARK_SOURCE.read_text(...)` to concatenate every `native/llama_runtime/benchmark/rma133_benchmark*.c` file, verify it still passes against the *current* unsplit file, then proceed.
- `.github/workflows/rma133-local-llm-benchmark.yml`'s three `paths:`/change-scope-gate entries already use the glob `'native/llama_runtime/benchmark/**'` — **no trigger-widening needed.**
- **Notable pre-existing gap, not something to fix here**: no `-fsyntax-only` host-side syntax check exists for this file in any workflow (unlike its `rma133_benchmark_v6.c` sibling) — the real correctness check for this split has to be a manual host compile (see extraction order) rather than anything `./scripts/ci.sh` already runs.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `rma133_benchmark_internal.h` (new) | `extern` prototypes for every function that crosses a file boundary, grouped by section with a one-line comment each; includes `reachy_llama.h` for shared ABI types used in signatures | ~55 |
| `rma133_benchmark_args.c` | `parse_u32`, `parse_i32`, `parse_float_value`, `parse_double_value` — CLI numeric-argument parsing | ~70 |
| `rma133_benchmark_platform.c` | `monotonic_us`, `sleep_one_millisecond`, `read_text_file`, `read_status_bytes`, `normalize_temperature`/`read_temperature_path`/`read_battery_temperature_c` (private cluster) | ~170 |
| `rma133_benchmark_output.c` | `init_error`, `print_runtime_error`, `json_string`, `json_hex_bytes` | ~95 |
| `rma133_benchmark_generation.c` | `append_bytes` (private), `render_prompt` (private), `finalize_generation` (private), `run_case` (the per-case benchmark loop — only externally-called symbol from this file) | ~365 |
| `rma133_benchmark.c` (anchor, keeps original name) | `#define _POSIX_C_SOURCE`, includes, `MAX_CASE_LINE_BYTES`, `main()` (argv validation, config, thermal pre-check, model load, TSV case-file loop, summary record, model unload) | ~255 |

Sum (~1010) exceeds the original due to per-file include/license-comment boilerplate and the new header — expected, consistent with the overhead seen in every round 1/2 file.

### Watch out for

- **Functions that must lose `static` and gain an `extern` prototype in `rma133_benchmark_internal.h`** (complete list, traced by call site):
  - `parse_u32`, `parse_i32`, `parse_float_value`, `parse_double_value` — called only from `main()`.
  - `read_text_file`, `read_status_bytes`, `read_battery_temperature_c` — called from both `run_case` and `main()`.
  - `init_error`, `print_runtime_error` — called from generation.c helpers **and** `main()`.
  - `json_string`, `json_hex_bytes` — called from `run_case` and `main()`.
  - `run_case` — called only from `main()`.
  This is the minimal necessary visibility change per the no-behavior-change rule. `append_bytes`, `render_prompt`, `finalize_generation`, `normalize_temperature`, `read_temperature_path` each have their only caller in the *same* target file — leave these `static`, do not add them to the header.
- **Warnings-as-errors is a real safety net here, not just a risk**: `docs/WARNING_POLICY.md`'s `-Werror` (including `-Wunused-function`) means a moved-and-orphaned `static` definition fails the build immediately and loudly — unlike a C#/Python file where dead code can silently linger. Don't rely on it as a substitute for deleting the moved definition in the same commit as adding it elsewhere, though.
- No file-scope static/global mutable variables exist anywhere in this file.
- `#define _POSIX_C_SOURCE 200809L` must appear as the *first* line of every new `.c` file that needs it (not just once, globally) — `rma133_benchmark_platform.c` and the anchor need it; `args.c`/`output.c`/`generation.c` don't call POSIX-only libc functions directly and shouldn't cargo-cult it in.
- `POLL_INITIAL_CAPACITY`/`RESPONSE_INITIAL_CAPACITY` land entirely inside `generation.c`; `MAX_CASE_LINE_BYTES` lands entirely inside the anchor — confirmed no macro needs to move to the shared header.

### Extraction order

1. Create `rma133_benchmark_internal.h` with the prototypes above, and mechanically drop `static` from those same 8 functions in the still-monolithic file — no code moved yet. Compile with `clang -std=c17 -fsyntax-only -Wall -Wextra -Wpedantic -Wconversion -Wsign-conversion -Wshadow -Werror -Inative/llama_runtime/include native/llama_runtime/benchmark/rma133_benchmark.c` to confirm this alone is a no-op.
2. **Fix `scripts/tests/test_rma133_benchmark_contract.py`'s `BENCHMARK_SOURCE` read** to concatenate the planned `rma133_benchmark*.c` glob, and confirm `python3 -m unittest scripts.tests.test_rma133_benchmark_contract` still passes against the current (still-monolithic) file before extracting anything.
3. `rma133_benchmark_args.c` (zero cross-references beyond the header, safest leaf).
4. `rma133_benchmark_platform.c` (leaf with respect to everything except the header; carries its own `_POSIX_C_SOURCE` define).
5. `rma133_benchmark_output.c` (leaf with respect to everything except the header).
6. `rma133_benchmark_generation.c` (depends on platform.c's and output.c's now-extern functions — highest fan-in, riskiest single move).
7. Shrink `rma133_benchmark.c` down to includes/`MAX_CASE_LINE_BYTES`/`main()`, update `scripts/build_rma133_android_benchmark.sh`'s source variable and `clang` invocation to list all five source files.
8. Final: confirm the `-fsyntax-only` compile passes on each new `.c` file individually. There is **no CMake target and no ctest**, so run `python3 -m unittest scripts.tests.test_rma133_benchmark_contract` (the file's one automated CI-run correctness check) and, if an NDK + prebuilt `libreachy_llama.so` are available, `ANDROID_NDK_HOME=... ./scripts/build_rma133_android_benchmark.sh` as the authoritative end-to-end link/build check.

---

## 6. `managed/ReachyMini.LocalLlm.Tests/Program.cs` (853 lines)

**Responsibility:** the RMA-134 local LLM provider managed contract test harness (console-runner, `OutputType: Exe`) — validates the native `reachy_llama` ABI v2 struct layouts, the frozen RMA-133-v6 baseline execution profile, the strict/fail-closed behavior-intent JSON parser, and the full `LocalLlmProvider` lifecycle (creation gating on manifest/artifact/ABI mismatch, prompt construction against the frozen system prompt and GBNF grammar, streaming, busy/cancel/reset semantics, output-limit and consumer/runtime-failure handling, out-of-memory classification and explicit-reload recovery, and dispose) against an in-process fake `ILocalLlmRuntime`. There is no README in this directory and the file has no top-of-file banner comment.

**Mechanism:** `internal static class Program` — **not yet `partial`**. `Main()` calls all 21 test methods **directly, sequentially**, with no `Run`/`RunAsync` wrapper and **no `caseCount`/`ExpectedCaseCount` safety net**. It ends with a hardcoded `Console.WriteLine("RMA-134 local LLM managed contracts passed (21 groups).");`. Matches round 2's `LocalVlm.Tests` pattern, not round 1's `RemoteVlm.Tests` pattern. Manual count of `Main()`'s calls confirms exactly 21, matching the banner — but nothing in the code enforces that match, so it must be re-verified by eye after the split.

**Baseline run:**
```
$ dotnet build managed/ReachyMini.LocalLlm.Tests/ReachyMini.LocalLlm.Tests.csproj --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet run --project managed/ReachyMini.LocalLlm.Tests/ReachyMini.LocalLlm.Tests.csproj --configuration Release
RMA-134 local LLM managed contracts passed (21 groups).
```

### Landmine check

- `.github/workflows/rma134-local-llm-provider.yml`'s `paths:` trigger globs `managed/ReachyMini.LocalLlm.Tests/**` (both push/pull_request). **Soft** — directory glob, auto-covers new split files. Its `dotnet run --project ...` step targets the `.csproj`, which has no explicit `<Compile Include>` list, so SDK-style implicit globbing picks up new partial files automatically. **Soft**.
- **Hard** — the same workflow's "Create exact-SHA evidence" step runs `sha256sum ... managed/ReachyMini.LocalLlm.Tests/Program.cs managed/ReachyMini.LocalLlm.Tests/ReachyMini.LocalLlm.Tests.csproj > "$OUT/source-sha256.txt"`. This hardcodes the single literal file path — after the split it will silently omit every new split file from the evidence artifact. Widen the list to include every new split file as part of finalizing this split.
- **Soft, informational only, do not touch**: the same workflow also writes a hardcoded `"managed_contract_groups": 17` into its evidence JSON — pre-existing, already inconsistent with the console banner's "21 groups", and not derived from `Program.cs` by any parse (verified). Not affected by this split; out of scope to "fix."
- `scripts/tests/test_rma134_local_llm_provider.py` — confirmed clean, reads only production source files (`ReachyLocalLlmProvider.cs`, etc.), never this test `Program.cs`.
- No other test `Program.cs` or workflow references this file's path or method/type names.
- `[assembly: InternalsVisibleTo("ReachyMini.LocalLlm.Tests")]` in `ReachyMiniInteropAssemblyInfo.cs` — keyed on the assembly name, unaffected by an in-project file split.
- **Nested-type naming overlap check vs `ReachyRma134LocalLlmAcceptance.cs`** (round 3's own file #10, a *different* file in `Assets/ReachyMini/Runtime/Application/`): that file also declares private nested `CollectingSink`/`ArtifactVerification`, and this file separately declares its own private nested `CollectingSink`. **Not a live or future build-collision risk** — this file's project (`ReachyMini.LocalLlm.Tests.csproj`) only references `ReachyMini.Core.csproj`, which globs in only `Runtime/{Core,Interop,Simulation}/**` — never `Runtime/Application/**`, so the two `CollectingSink` types can never end up in the same compilation. Note for human-hygiene only — no rename required here, unlike file #10.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `Program.cs` (anchor) | usings, `internal static partial class Program`, `ValidIntent` const (shared across most test-group files), `Main()` unchanged | ~50 |
| `Program.Assertions.cs` | `Require` | ~10 |
| `Fakes.cs` (new top-level, non-partial) | `CollectingSink`, `ThrowingSink`, `FakeRuntime` — promoted from `private` nested to top-level `internal sealed class` (`FakeRuntime : ILocalLlmRuntime`) | ~255 |
| `Program.Fixtures.cs` | `StopTokens`, `SupportedAbis` (relocated here — `CreateManifest` is their sole consumer, neither used in `Main()`), `CreateProviderAsync`, `Request`, `CreateManifest`, `CreateApprovedArtifact` | ~75 |
| `Program.AbiAndProfileTests.cs` | `TestNativeAbi2Layouts`, `TestRma133BaselineProfile` | ~25 |
| `Program.IntentParserTests.cs` | `TestStrictIntentParser` | ~45 |
| `Program.ProviderCreationTests.cs` | `TestManifestArtifactAndAbiFailuresAsync` | ~48 |
| `Program.IntentAndOutputLimitTests.cs` | `TestInvalidIntentIsNotRepairedAsync`, `TestOutputLimitAsync` | ~30 |
| `Program.ContextAndConcurrencyTests.cs` | `TestContextPreflightAsync`, `TestBusyAndCancellationAsync`, `TestResetSuppressesStaleOutputAsync` | ~55 |
| `Program.ConsumerAndRuntimeFailureTests.cs` | `TestStreamConsumerFailureAsync`, `TestTerminalConsumerFailureAsync`, `TestRuntimeTerminalErrorAsync`, `TestFailedCancelDoesNotRetryAsync`, `TestPollExceptionCleanupAsync`, `TestMetricsExceptionStillReleasesAsync` | ~100 |
| `Program.ReloadAndDisposeTests.cs` | `TestReloadRecoveryAndDisposeAsync` | ~25 |
| `Program.OutOfMemoryTests.cs` | `TestOutOfMemoryBeforeNativeHandleAsync`, `TestOutOfMemoryAfterNativeStartAsync`, `TestOutOfMemoryCleanupFailureFaultsAsync`, `TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync` | ~95 |
| `Program.GenerationSuccessTests.cs` | `TestWorkerPromptAndSuccessAsync` (largest single test) | ~60 |

All target files land well under the 800-line ceiling (the whole original file is only 853 lines — this split is about clean grouping, not forced size reduction).

### Watch out for

- `CollectingSink`, `ThrowingSink`, `FakeRuntime` must become top-level `internal sealed class` in `Fakes.cs` (top-level types cannot be `private`). `FakeRuntime` implements `ILocalLlmRuntime` and owns real disposable/stateful members — verify analyzer rules don't trip on the promotion.
- `ValidIntent` (const) is referenced from 8 different test methods across nearly every planned test-group file — must stay declared once in the anchor; works transparently under `partial class`.
- `StopTokens`/`SupportedAbis` are used only inside `CreateManifest` — relocate both to `Program.Fixtures.cs` rather than leaving them stranded in the anchor.
- No hidden cross-test mutable shared state beyond each test's own freshly-constructed `FakeRuntime` — every fixture factory is effectively pure/stateless.
- **No README exists to cross-check a stated case count** — the only claim is the console banner "(21 groups)", confirmed by direct manual count. Since there is no `caseCount` safety net, a dropped/duplicated call during the split will not be caught automatically — re-verify by eye and diff the `dotnet run` output against the captured baseline.
- Do not touch or "fix" the unrelated `"managed_contract_groups": 17` constant in the workflow as part of this split — out of scope.

### Extraction order

1. Add `partial` to `Program`, no code moved — verify baseline (build + run, confirm banner unchanged).
2. `Program.Assertions.cs` (zero dependencies — just `Require`).
3. `Fakes.cs` (pure type promotion: `CollectingSink`, `ThrowingSink`, `FakeRuntime`).
4. `Program.Fixtures.cs` (depends on `Fakes.cs` for `FakeRuntime`; also relocates `StopTokens`/`SupportedAbis`).
5. Test-group files, ordered by increasing fixture/runtime coupling:
   `AbiAndProfileTests` → `IntentParserTests` → `ProviderCreationTests` → `IntentAndOutputLimitTests` → `ContextAndConcurrencyTests` → `ConsumerAndRuntimeFailureTests` (broadest, 6 tests) → `ReloadAndDisposeTests` → `OutOfMemoryTests` (4 tests, most cross-test-dependent narrative) → `GenerationSuccessTests` last (single most complex test).
6. Final: confirm `Program.cs` contains only usings + `ValidIntent` + `Main()`; build + run; diff console output byte-for-byte against the captured baseline; manually recount `Main()`'s 21 `Test*` calls; then widen the `sha256sum` file list in `.github/workflows/rma134-local-llm-provider.yml` to cover every new split file.

---

## 7. `ReachyAndroidCameraAcquisition.cs` (834 lines)

**Responsibility:** the RMA-091 Android CameraX frame-acquisition binding layer. `ReachyAndroidCameraAcquisition` is a `MonoBehaviour` that owns the CameraX lifecycle state machine for a bound camera: it selects a camera from `ReachyAndroidCameraDiscovery`'s capability snapshot, starts/stops/pauses/resumes the platform binding through `IReachyDeviceCameraAcquisitionPlatform`, polls for JSON state snapshots, translates them into `ReachyCameraAcquisitionStateStore` transitions, and decodes/publishes frame metadata (crop, intrinsics) while failing closed on unavailable/permission-revoked/faulted conditions. The file also carries the platform abstraction itself: the `IReachyDeviceCameraAcquisitionPlatform` interface, a JNI-bridge implementation (`ReachyUnityAndroidCameraAcquisitionPlatform`), and four `[Serializable]` JSON DTOs used to deserialize the bridge's snapshots. Per `docs/validation/RMA_091_CAMERA_ACQUISITION_VALIDATION_2026-08-04.md`, this is the accepted RMA-091 acquisition contract (RMA-092's texture bridge — file #9 below — and RMA-104's reprojection suite build on top of it).

Note the nested `ReachyUnityAndroidCameraAcquisitionPlatform` is effectively a fallback/test default in production: `EnsureInitialized()` constructs it only as the initial `platform`, but `ReachyCameraAcquisitionBootstrap.cs` always swaps in a different implementation (`ReachyAndroidUiThreadCameraAcquisitionPlatform.cs`) via `ConfigurePlatformForTests` before activating the GameObject in real builds.

**Mechanism:** `partial class` for `ReachyAndroidCameraAcquisition` only — it's a `sealed`, `[DisallowMultipleComponent]` `MonoBehaviour` with `Awake`/`Update`/`OnApplicationPause`/`OnDestroy` lifecycle callbacks and a large set of fields (`state`, `discovery`, `platform`, `desiredActive`, `pendingStartAfterStop`, `preferredFacing`, `nextPollTime`, `nextSessionId`, `initialized`, `disposed`) read/written across nearly every method — it cannot be decomposed into independent types without breaking component identity. The interface, the JNI platform implementation, and the four DTOs are fully independent types with zero shared instance state — plain type-splitting into new top-level files, no `partial` needed for those three.

### ⚠️ Landmine: 2 workflows, one risks an uncaught `IndexError`

1. **`.github/workflows/rma091-camera-acquisition.yml`** — reads this exact file's text as a single `read_text()`, then requires 8 literal tokens: `'PollIntervalSeconds = 0.05f'`, `'StartPreferred'`, `'StopPlatformForSwitch'`, `'OnApplicationPause'`, `'MarkPermissionRevoked'`, `'camera_frame_contract_failed'`, `'AcquireLatestTextureFrame'`, `'ReachyCameraFrameMetadata'`, and forbids `'AndroidJavaProxy'`. Checked against the proposed anchor alone, **`OnApplicationPause`, `camera_frame_contract_failed`, and `ReachyCameraFrameMetadata` would disappear from the anchor** once `Lifecycle.cs` and `StateTranslation.cs` are extracted — a hard break (`SystemExit`), not soft. Also **soft**: `paths:` triggers hardcode the exact filename in both push/pull_request blocks.
2. **`.github/workflows/rma104-reprojection-test-suite.yml`** — separately reads this same file into `files['acquisition']`, requiring `'pendingStartAfterStop'` and `'restartAfterClose'`. **`restartAfterClose` is a local variable that only exists inside `ApplyPlatformSnapshot`'s `"Stopped"` case** — moved to `StateTranslation.cs` by the proposed split, which would hard-break this check. **Worse**: this workflow also does `text['acquisition'].split('public void StopAcquisition()', 1)[1].split('public void RefreshNow()', 1)[0]` then asserts `'state.MarkStopped'` is absent from that slice — if `'public void StopAcquisition()'` is not found at all, `.split(...)[1]` raises an **uncaught `IndexError`**, a harder failure mode than any other landmine catalogued in rounds 1–3. **This means `StopAcquisition()` and `RefreshNow()` must both stay in the literal anchor file, with `StopAcquisition` textually before `RefreshNow`.** Also **soft**: its `paths:` trigger has the same widening need.

No landmine found in `managed/ReachyMini.Camera.Tests/**` or `rma090-camera-discovery.yml` (that workflow only reads `ReachyMainScreen.cs`/`ReachyAndroidCameraDiscovery.cs` — already covered by round 2's file #3).

**Fix both workflows FIRST**, as prerequisite commits, before extracting `Lifecycle.cs`/`StateTranslation.cs` (extracting the DTOs and platform-contract files first is safe and needs no fix): replace both single-file `read_text()` calls with a glob+concatenate over `ReachyAndroidCameraAcquisition*.cs`, and for rma104 specifically, keep the `stop_body` split logic working by ensuring the concatenation still contains `'public void StopAcquisition()'` before `'public void RefreshNow()'` — true as long as both stay in the anchor per the target table. Verify both fixes against the current unsplit file first.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyCameraAcquisitionDto.cs` (new, not partial) | `ReachyCameraAcquisitionEnvelope`, `ReachyCameraFrameDto`, `ReachyCameraFrameCropDto`, `ReachyCameraFrameIntrinsicsDto` | ~70 |
| `ReachyAndroidCameraAcquisitionPlatform.cs` (new, not partial) | `IReachyDeviceCameraAcquisitionPlatform` interface, `ReachyUnityAndroidCameraAcquisitionPlatform` (JNI bridge implementation) | ~200 |
| `ReachyAndroidCameraAcquisition.cs` (anchor) | `[DisallowMultipleComponent]`/`: MonoBehaviour` decl, fields/properties, `Configure`, `ConfigurePlatformForTests`, public command API: `Toggle`, `StartPreferred`, `StopAcquisition`, `RefreshNow`, `AcquireLatestTextureFrame` (keeps `StopAcquisition`/`RefreshNow` together and in order — required by the rma104 landmine above) | ~200 |
| `ReachyAndroidCameraAcquisition.Lifecycle.cs` | `Awake`, `Update`, `OnApplicationPause`, `OnDestroy`, `OnCapabilitiesChanged`, `StopPlatformForSwitch` | ~145 |
| `ReachyAndroidCameraAcquisition.StateTranslation.cs` | `ApplyPlatformSnapshot` (JSON envelope → state-store transitions, contains `restartAfterClose`), `PublishFrame` (DTO → `ReachyCameraFrameMetadata`, contains `camera_frame_contract_failed`) | ~155 |
| `ReachyAndroidCameraAcquisition.Selection.cs` | `EnsureInitialized`, `EnsureReady`, `RequirePlatform`, `RequireDiscovery` guards; static helpers `SelectCamera`, `FindCamera`, `CanRemainBoundToSelectedCamera`, `ParseFacing`, `ParsePixelFormat`, `ParseIntrinsicsSource`, `GetFacingLabel`, `PrefixError` | ~140 |

### Watch out for

- **Shared mutable instance state across every partial file**: `state`, `discovery`, `platform`, `desiredActive`, `pendingStartAfterStop`, `preferredFacing`, `nextPollTime`, `nextSessionId`, `initialized`, `disposed` are all declared once in the anchor and read/written from `Lifecycle.cs`, `StateTranslation.cs`, and `Selection.cs` — correct under `partial class`, don't duplicate a declaration by mistake.
- **JNI/native interop ordering**: `ReachyUnityAndroidCameraAcquisitionPlatform`'s methods each construct a fresh `AndroidJavaClass`/`AndroidJavaObject` and must not be reordered relative to the `disposed` guard or relative to `OnDestroy()`'s `platform.Stop()` → `platform.Dispose()` sequence (a `try/finally`) — load-bearing for clean CameraX shutdown, must move as one atomic unit.
- **Cross-file DTO reference**: the four DTOs are referenced only from `ApplyPlatformSnapshot`/`PublishFrame` in `StateTranslation.cs` — a normal same-namespace type reference once DTOs move to their own file.
- `nextPollTime` is set from three different places (`StartPreferred`/`StopAcquisition` in the anchor, `Update` and `StopPlatformForSwitch` in `Lifecycle.cs`) — safe under `partial class`, but a reviewer diffing `Lifecycle.cs` in isolation could easily miss that the anchor also touches it.
- `restartAfterClose` captures `desiredActive && pendingStartAfterStop` *before* calling `state.MarkStopped(detail)`, then calls `StartPreferred(preferredFacing)` (anchor) only after — a genuine cross-partial-file call whose ordering matters; worth a one-line comment.
- Disposal: `OnDestroy` (Lifecycle.cs) unsubscribes `discovery.State.Changed -= OnCapabilitiesChanged` — keep that unsubscribe paired with the `Configure()` subscribe (anchor) in reviewers' minds even though they end up in different files.

### Extraction order

1. `ReachyCameraAcquisitionDto.cs` (pure data, zero logic, none of the rma091/rma104 checked tokens live here).
2. `ReachyAndroidCameraAcquisitionPlatform.cs` (interface + JNI impl are self-contained; `AcquireLatestTextureFrame` remains in the anchor's own method of the same name afterward, so this step alone doesn't yet require the CI fix).
3. **Prerequisite**: fix both workflows' single-file reads to glob+concatenate `ReachyAndroidCameraAcquisition*.cs`. Verify both against the current (post steps 1–2) file before proceeding.
4. `ReachyAndroidCameraAcquisition.Selection.cs` (lowest instance-state coupling — only reads fields).
5. `ReachyAndroidCameraAcquisition.Lifecycle.cs` (touches `disposed`/`initialized`/`desiredActive`, calls into `Selection.cs`'s guards — do after step 4).
6. `ReachyAndroidCameraAcquisition.StateTranslation.cs` last (largest, most control-flow-heavy, the one genuine cross-file re-entrant call into `StartPreferred`) — re-run both fixed CI checks' Python snippets locally, plus `managed/ReachyMini.Camera.Tests` and the relevant Editor test suites.
7. Final: confirm the anchor contains only the MonoBehaviour decl, fields, `Configure`/`ConfigurePlatformForTests`, and the 5 command-API methods (~200 lines, `StopAcquisition` still textually before `RefreshNow`); widen both workflows' `paths:` trigger lists.

---

## 8. `AndroidOnDeviceAsrTests.cs` (831 lines)

**Responsibility:** the RMA-121 "Android explicit on-device ASR" provider contract test suite — a deterministic, hardware-free harness (via `FakeAndroidOnDeviceAsrPlatform`) exercising `AndroidOnDeviceAsrProvider`'s full state machine (already split in round 2, section 9): availability/preflight across API levels, permission gating, partial/final/no-match streaming results, busy-without-queue concurrency, operation/utterance timeouts, caller cancellation, platform failure-code mapping, callback request-identity/terminal-event contract violations, provider-redirect rejection, no-automatic-retry, and disposal semantics. It also registers (but does not implement) 3 static-source-text contract cases owned by the sibling `Rma121SourceContracts.cs`.

**Mechanism:** `internal static class AndroidOnDeviceAsrTests` — **not currently `partial`**. It is a distinct class from the project's entry point: `Program.cs` (42 lines, separate file, same directory) holds the actual `Main()`, which just iterates `AndroidOnDeviceAsrTests.All` (the public `IReadOnlyList<(string Name, Func<Task> Run)>` test registry) and prints `PASS`/`FAIL` per case plus a final summary line. `Program.cs` itself is small and out of scope for this split. The natural mechanism is adding `partial` to `AndroidOnDeviceAsrTests` and dividing its 30 in-file test methods plus shared fixture/assertion helpers into sibling `AndroidOnDeviceAsrTests.*.cs` files, following the exact naming convention already used one directory up for the production code under test.

Two other files already live alongside it and are unaffected: `FakeAndroidOnDeviceAsrPlatform.cs` (159 lines, already standalone) and `Rma121SourceContracts.cs` (169 lines, already standalone).

### Landmine check

**None found.**

- `.github/workflows/rma121-android-on-device-asr.yml` triggers on the **directory-wide glob** `managed/ReachyMini.AndroidOnDeviceAsr.Tests/**` and its only validation step is `dotnet run --project ...` — no `File.ReadAllText`/`sha256sum`/regex over this file's source. Any new sibling file automatically stays covered.
- `scripts/tests/*.py` has zero hits for `AndroidOnDeviceAsr`.
- `Rma121SourceContracts.cs` reads production Java/manifest/bridge sources, never this test file.
- No README states an exact case count in prose; `Program.cs`'s printed count is computed dynamically from `AndroidOnDeviceAsrTests.All.Count`, not a hardcoded literal — the split is inherently safe on this axis.

**Baseline run** (for future diffing): build succeeds 0 warnings/0 errors; console output ends `RMA-121 Android on-device ASR contracts passed: 33.` (33 = 30 in-file tests + 3 `Rma121SourceContracts` cases registered in `All` but implemented in the sibling file).

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `AndroidOnDeviceAsrTests.cs` (anchor) | usings, `internal static partial class AndroidOnDeviceAsrTests`, the `All` registry array (unchanged, references methods across every partial file by unqualified name) | ~50 |
| `AndroidOnDeviceAsrTests.Availability.cs` | 12 availability/preflight tests: `DescriptorIsExplicitOnDevice`, `Api30Unavailable`, `Api31RecognizerUnavailable`, `PermissionRequired`, `Api31PreflightUnavailable`, `InstalledLanguageAvailable`, `ModelDownloadRequired`, `ModelDownloadPending`, `UnsupportedLanguage`, `PreflightUnavailableStillAvailable`, `SupportFaulted`, `ConfiguredLanguageEnforced` | ~260 |
| `AndroidOnDeviceAsrTests.Recognition.cs` | `PartialAndFinalResults`, `NoMatch`, `BusyWithoutQueue` | ~90 |
| `AndroidOnDeviceAsrTests.TimeoutAndCancellation.cs` | `PlatformSpeechTimeout`, `OperationTimeoutCancels`, `CallerCancellationReachesPlatform`, `MaximumUtteranceCapsTimeout` | ~100 |
| `AndroidOnDeviceAsrTests.FailureMapping.cs` | `ServiceDisconnected`, `LanguageModelUnavailable`, `RuntimeUnsupportedLanguage`, `NetworkFailureIsContractViolation`, `CallbackIdentityMismatch`, `StreamEndsWithoutTerminal`, `PlatformExceptionVisible`, private `AssertFailureMapping` helper | ~140 |
| `AndroidOnDeviceAsrTests.Lifecycle.cs` | `ProviderRedirectRejected`, `NoAutomaticRetry`, `DisposeCancelsActive`, `DisposedProviderRejectsUse` | ~100 |
| `AndroidOnDeviceAsrTests.Fixtures.cs` | `CreateProvider`, `Options`, `Request`, `Support`, `Event`, `FailedPlatform`, `CollectAsync`, `AssertSingleFailure`, `AssertFailure`, `ExpectThrowsAsync<TException>`, `Assert`, `AssertEqual<T>` | ~140 |

All 7 files land comfortably under 800 lines.

### Watch out for

- `CreateProvider`/`Options`/`Request`/`CollectAsync`/`AssertEqual<T>` in `Fixtures.cs` are called from every single test method across every other partial file — this is why `partial class` (not free-standing static classes) is required.
- `AssertFailureMapping` is a group-local helper used only by 4 tests in `FailureMapping.cs` — keep it colocated there rather than pulling it into `Fixtures.cs`.
- No mutable shared state between test methods — every test builds/disposes its own fake platform/provider — lower risk than round 1's `RemoteVlm.Tests` split.
- **Nested-type naming-collision check performed and clean**: `AndroidOnDeviceAsrTests.cs` declares zero nested types. Note the production side has its own `private sealed class FailureMapping` (in `AndroidOnDeviceAsrProvider.Readiness.cs`, round 2's split) — a different type in a different namespace/assembly, and the proposed `AndroidOnDeviceAsrTests.FailureMapping.cs` here is a **file** name, not a type name, so this is a name echo only, not a collision.
- `All`'s three `Rma121SourceContracts.*` entries reference a sibling file untouched by this split.

### Extraction order

1. Add `partial` to `AndroidOnDeviceAsrTests`'s declaration only — no code moved, verify baseline build + run still shows "passed: 33."
2. `AndroidOnDeviceAsrTests.Fixtures.cs` (leaf utilities with zero dependency on any test method — de-risks everything after).
3. `AndroidOnDeviceAsrTests.Recognition.cs` (smallest test group, 3 methods — good first test-group move).
4. `AndroidOnDeviceAsrTests.Lifecycle.cs`.
5. `AndroidOnDeviceAsrTests.TimeoutAndCancellation.cs`.
6. `AndroidOnDeviceAsrTests.FailureMapping.cs` (move `AssertFailureMapping` together with its 4 callers).
7. `AndroidOnDeviceAsrTests.Availability.cs` last (largest group, 12 methods).
8. Final: confirm the anchor contains only usings + class declaration + the `All` array; build + run, diffing console output against the "passed: 33" baseline.

---

## 9. `ReachyAndroidCameraTextureBridge.cs` (812 lines)

**Responsibility:** the RMA-092 GPU-side half of the Android camera pipeline. It defines the YUV/lease/state contracts consumed across the camera subsystem (`ReachyCameraTextureFrameDescriptor`, `IReachyCameraTextureFrameLease`, `ReachyCameraTextureBridgeSnapshot`), then a `[DisallowMultipleComponent] sealed class ReachyAndroidCameraTextureBridge : MonoBehaviour` that polls `ReachyAndroidCameraAcquisition` (file #7 above) each `Update()`, uploads Y/U/V planes into three `Texture2D` (`TextureFormat.R8`) via `LoadRawTextureData`, and GPU-converts them to an `ARGB32` `RenderTexture` via `Graphics.Blit` with a YUV→RGB conversion `Material`/shader — explicitly avoiding any CPU readback (`GetRawTextureData`/`ReadPixels` are forbidden by CI, see below). It publishes an immutable `ReachyCameraTextureBridgeSnapshot`/`Changed` event as its public contract.

**Mechanism:** `partial class` is required for the `ReachyAndroidCameraTextureBridge` portion — a `sealed` MonoBehaviour attached to a GameObject at runtime, carrying substantial shared mutable instance state (`yTexture`/`uTexture`/`vTexture`/`outputTexture`/`conversionMaterial`/`current`/`lastUploadedSessionId`/`lastUploadedSequence`/`disposed`) touched from nearly every method. The six contract types above the class have zero shared state and no MonoBehaviour dependency — plain type-splitting, no partial needed, matching the `ReachyVisionProviderContracts.cs` precedent from round 2.

### ⚠️ Landmine: `.github/workflows/rma091-camera-acquisition.yml`

**Hard landmine.** This workflow reads this file's exact literal text into a `texture_unity` variable, then asserts 7 required literal tokens are present: `'LoadRawTextureData(source, length)'` (in `UploadPlane`), `'TextureFormat.R8'` (in `EnsurePlaneTexture`), `'Graphics.Blit'` (in `PumpOnce`), `'descriptor.TimestampNanoseconds'` (in `Publish(...)`), `'FrameMatchesActiveSession'` (method name), `'using (lease)'` (in `PumpOnce`), `'ReachyCameraTextureBridgeState.Ready'`. It separately asserts `'GetRawTextureData' not in texture_unity` and `'ReadPixels' not in texture_unity` (forbidden CPU-readback). `paths:` triggers also hardcode the exact filename (push and pull_request).

**Fix first**: change the read/glob logic to concatenate every `ReachyAndroidCameraTextureBridge*.cs` / `ReachyCameraTextureBridgeContracts*.cs` file the planned split will produce, then re-run the check against the *current unsplit* file to confirm no regression before starting. All 7 required tokens naturally land in the "Pump" and "TextureResources" target files below — no code changes needed, only the workflow's read logic.

**Soft/non-issue, for awareness only**: `rma102-gpu-homography-warp.yml` checks that a *different* file (`ReachyCameraHomographyWarpPipeline.cs`) contains the literal string `"ReachyAndroidCameraTextureBridge"` — a type-name reference check on a consumer file, unaffected since the type name is unchanged by a `partial class` split.

**Not a text-landmine, but a real coupling to respect**: `ReachyCameraTextureStageDiagnostics.cs` (compiled only under `#if UNITY_ANDROID && !UNITY_EDITOR && DEVELOPMENT_BUILD`) uses reflection (`GetField("yTexture"/"uTexture"/"vTexture"/"conversionMaterial", BindingFlags.Instance | BindingFlags.NonPublic)`) against the compiled type — works regardless of which partial file declares each field, but the 4 field *names* are hardcoded string literals there. **Do not rename these fields during the split** — a pure move is fine, a rename silently breaks this reflection-based probe with no compile error.

Also checked and clean: no hits in `scripts/tests/*.py`, `managed/ReachyMini.Camera.Tests/Program.cs`, or any other workflow.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyCameraTextureFrameContracts.cs` | `ReachyCameraYuvColorStandard`, `ReachyCameraYuvColorRange` enums, `ReachyCameraTextureFrameDescriptor`, `IReachyCameraTextureFrameLease` | ~221 |
| `ReachyCameraTextureBridgeContracts.cs` | `ReachyCameraTextureBridgeState` enum, `ReachyCameraTextureBridgeSnapshot`, `ReachyCameraTextureBridgeChangedEventArgs` | ~73 |
| `ReachyAndroidCameraTextureBridge.cs` (anchor) | `[DisallowMultipleComponent]`/`: MonoBehaviour` decl, `ShaderName` const, shader property-ID statics, instance fields, `Current`/`OutputTexture`/`PreviewTexture`/`AnalysisTexture` properties, `Changed` event, `Configure()` | ~97 |
| `ReachyAndroidCameraTextureBridge.Pump.cs` | `PumpOnceForTests`, `Update`, `PumpOnce` (main per-frame acquire→validate→upload→blit→publish pipeline; hosts 4 of the 7 CI-checked tokens) | ~112 |
| `ReachyAndroidCameraTextureBridge.Lifecycle.cs` | `OnAcquisitionChanged`, `OnDestroy`, `FrameMatchesActiveSession`, `ValidateLease` | ~70 |
| `ReachyAndroidCameraTextureBridge.TextureResources.cs` | `EnsurePlaneTextures`, `EnsurePlaneTexture`, `EnsureOutputTexture`, `ConfigureMaterial`, `UploadPlane` (hosts the other 3 CI-checked tokens + the forbidden-token area) | ~131 |
| `ReachyAndroidCameraTextureBridge.Diagnostics.cs` | `InvalidateOutput`, `PublishWaiting`, `Publish`, `DestroyFrameResources`, `DestroyAllResources`, `DestroyOutputTexture`, `RequireOutputTexture`, `RequireTexture`, `DestroyUnityObject` | ~102 |

### Watch out for

- **Resource lifetime is spread across three of the proposed files by design** — `EnsurePlaneTexture`/`EnsureOutputTexture` (TextureResources.cs) *create*; `PumpOnce` (Pump.cs) is the only caller that triggers creation; `DestroyFrameResources`/`DestroyOutputTexture`/`DestroyAllResources` (Diagnostics.cs) are the only disposal paths. `partial class` merges these back at compile time, but leave a short comment at each creation/disposal site cross-referencing the other file.
- `conversionMaterial` is created in `Configure()` (anchor) but destroyed only in `DestroyAllResources()` (Diagnostics.cs) — pre-existing asymmetry, worth a comment noting the pairing so the split doesn't make it look like an oversight.
- **`IReachyCameraTextureFrameLease`/`ReachyCameraTextureFrameDescriptor` have wide fan-out**: implemented/consumed by `ReachyAndroidJavaCameraTextureFrameLease.cs`, `ReachyAndroidCameraAcquisition.cs` (file #7's `AcquireLatestTextureFrame` return type), `ReachyCameraYuvReferenceConverter.cs`, `ReachyCameraTextureEvidence.cs`, `ReachyCameraHomographyWarpPipeline.cs`, `ReachyAndroidUiThreadCameraAcquisitionPlatform.cs`, and 4 Editor test files — extracting `ReachyCameraTextureFrameContracts.cs` changes only which sibling file declares those types; all external call sites unaffected (same namespace, same type names).
- Android JNI/native interop happens in `ReachyAndroidJavaCameraTextureFrameLease.cs`, not this file — no interop-boundary code to relocate.
- No nested private DTOs in this file — all types are top-level in the `ReachyMini.AppState` namespace, so no visibility changes needed.

### Extraction order

1. `ReachyCameraTextureFrameContracts.cs` (zero dependency on the MonoBehaviour; highest external fan-in, so proving it moves cleanly first de-risks everything downstream).
2. `ReachyCameraTextureBridgeContracts.cs` (depends only on the previous file's descriptor, still zero MonoBehaviour coupling).
3. **Prerequisite**: widen `rma091-camera-acquisition.yml`'s `texture_unity` read and `paths:` triggers to glob the planned filenames; re-verify against the current unsplit MonoBehaviour file.
4. Add `partial` to `ReachyAndroidCameraTextureBridge`'s class declaration only — no code moved, verify baseline + widened check.
5. `ReachyAndroidCameraTextureBridge.Lifecycle.cs` (smallest, mostly event-driven glue).
6. `ReachyAndroidCameraTextureBridge.TextureResources.cs` (touches 3 of the CI-checked tokens — re-run the rma091 Python check specifically after this step).
7. `ReachyAndroidCameraTextureBridge.Diagnostics.cs` (teardown calls reference the fields TextureResources.cs creates — do after step 6).
8. `ReachyAndroidCameraTextureBridge.Pump.cs` last (largest, hosts 4 of the 7 CI-checked tokens including the forbidden-token check — re-run the full rma091 Python check plus `ReachyCameraTextureBridgeTests.cs` after this step).
9. Final: confirm the anchor is ~97 lines, re-run the rma091 workflow's Python check end-to-end against the finished split, and confirm `ReachyCameraTextureStageDiagnostics.cs`'s reflection probe still finds all 4 fields by name.

---

## 10. `ReachyRma134LocalLlmAcceptance.cs` (807 lines)

**Responsibility:** a Unity runtime acceptance-test harness (`MonoBehaviour`, self-installs via `[RuntimeInitializeOnLoadMethod]` when the `reachy_rma134_acceptance` Android launch intent extra is present) that runs a single end-to-end RMA-134 physical acceptance test on-device: verifies the exact staged Qwen3-0.6B GGUF artifact (size + SHA-256), boots the managed `LocalLlmProvider` against a frozen RMA-133 V6 execution profile, runs a constrained initial generation concurrently with a native MuJoCo physics loop (asserting a 500 Hz / 2 ms p95 step budget isn't violated), exercises mid-stream generation cancellation, conversation-reset supersession, and an explicit in-process reload, runs a second constrained generation, and writes JSON checkpoint + final report files for a device-pull-based CI harness. It parallels round 2's RMA-135 resource-governor acceptance harness closely but targets the local-LLM/physics-coexistence contract rather than resource-governor admission/fault-injection.

**Mechanism:** today this is a single non-`partial` class — `internal sealed class ReachyRma134LocalLlmAcceptance : MonoBehaviour`, no `partial` keyword present yet. Splitting requires adding `partial` (Unity `MonoBehaviour`-subclass constraint — same mechanism round 2's RMA-135 split used), plus 2 new top-level non-partial helper files for the nested test doubles/DTOs (promoted from `private` to `internal`, and **renamed with an `Rma134` prefix** — mandatory, see below). `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` appears exactly once, on `Bootstrap()`. Static mutable fields `bootstrapError`, `unhandledFailure`, `unhandledFailureMessage`, `checkpointSequence`, `checkpointStopwatch` are shared across `Bootstrap()`/`Start()` and the diagnostics helpers — identical shape to RMA-135's static-field sharing across its anchor/Diagnostics partials.

### ⚠️ CRITICAL — mandatory naming-collision rename (`CollectingSink` / `ArtifactVerification`)

This file declares `private sealed class CollectingSink : ILocalLlmStreamSink` (4 non-declaration call sites) and `private sealed class ArtifactVerification` (3 non-declaration call sites). Both are `private` today — no collision yet, since `private` is scoped to the enclosing type. But round 2's RMA-135 split (already executed in this repo) promoted its identically-named nested types to **top-level `internal`** classes in the *same namespace* (`ReachyMini.Validation`): `Rma135CollectingSink` (in `Rma135AcceptanceTestDoubles.cs`) and `Rma135ArtifactVerification` (in `Rma135AcceptanceReportModels.cs`).

**If this split promotes `CollectingSink`/`ArtifactVerification` to top-level `internal` without renaming them, it is a real C# compile error** (`error CS0101: The namespace 'ReachyMini.Validation' already contains a definition for '...'`), not a latent risk. **The rename to `Rma134CollectingSink`/`Rma134ArtifactVerification` is therefore mandatory, not optional.**

For consistency with how RMA-135 handled this (it prefixed *all three* of its extracted test-double/DTO types, not just the one that collided), also prefix two other private nested types this file promotes even though grep found no existing collision for them: `CancelOnFirstTextSink` (1 call site) → `Rma134CancelOnFirstTextSink`, and `PhysicsTimingReport` (5 call sites) → `Rma134PhysicsTimingReport`. `Rma134AcceptanceCheckpoint` and `Rma134AcceptanceReport` are **already** ticket-prefixed as private nested types today — no rename needed, only promotion to top-level `internal`.

### Landmine check

1. **Hard landmine** — `.github/workflows/rma134-local-llm-provider.yml`'s "exact-SHA evidence" step runs `sha256sum` over an explicit file list including this file and `ReachyRma134AndroidSha256.cs`. The anchor keeps its name after the split, so this step won't *fail* — but its evidence artifact would silently stop covering logic moved into the new files. Widen the `sha256sum` file list to include all 5 resulting files, exactly as round 2's RMA-135 analogous step was widened.
2. **Hard-ish landmine** — the same workflow's `push`/`pull_request` `paths:` triggers use the glob `'Assets/ReachyMini/Runtime/Application/ReachyRma134*.cs'`. This matches new partial files (still start with `ReachyRma134`) but **does not match** new top-level helper files named `Rma134Acceptance*.cs` (they start with `Rma134`, not `ReachyRma134`). Add `'Assets/ReachyMini/Runtime/Application/Rma134Acceptance*.cs'` as an additional `paths:` entry (both blocks).
3. **Hard-ish landmine** — `.github/workflows/rma134-local-llm-android.yml`'s regex change-scope gate has the fragment `...(Assets/ReachyMini/Runtime/Application/ReachyRma134.*\.cs)|...` — same gap as #2. Widen to something like `(ReachyRma134LocalLlmAcceptance(\.[A-Za-z]+)?|Rma134Acceptance(TestDoubles|ReportModels))\.cs`, mirroring the already-fixed RMA-135 android workflow's regex fragment.
4. **None found** — `scripts/tests/test_rma134_local_llm_provider.py` (checked in full) only touches `Runtime/Core/LocalModels`/`Runtime/Interop`/`benchmarks/rma133/*`; it does not reference this file at all. RMA-134 has no analog to RMA-135's `test_rma135_android_acceptance_contracts.py`.
5. **Soft/no-op** — `scripts/run_rma134_local_llm_acceptance_android.sh` references the *runtime string values* of `ResultFileName`/`CheckpointFilePrefix`/`LaunchExtraName`/`ModelFileName`, not the `.cs` source text — unaffected as long as the constants stay declared once in the anchor.

### Target files

| File | Contents | Est. lines |
|---|---|---|
| `ReachyRma134LocalLlmAcceptance.cs` (anchor) | Consts/fields (`LaunchExtraName`, `ResultFileName`, `ModelFileName`, `CheckpointFilePrefix`, physics/prompt consts, static mutable fields), `Bootstrap()` (`[RuntimeInitializeOnLoadMethod]`), `Start()` | ~155 |
| `ReachyRma134LocalLlmAcceptance.Acceptance.cs` | `RunAcceptanceAsync()` (~210 lines) + `ValidateSuccessfulGeneration`, `ValidatePhysicsTiming`, `CreateSimulationSession`, `RunPhysicsLoop`, `WaitUntil`, `Percentile`, `VerifyArtifact`, `CreateSelectedManifest`, `CreateRequest`, `MetricsValue` | ~425 |
| `ReachyRma134LocalLlmAcceptance.Diagnostics.cs` | `ReadBatteryTemperatureCelsius`, `ReadSelfRssBytes`, `ReadBooleanLaunchExtra`, `HandleLogMessage`, `InitializeCheckpointRun`, `WriteCheckpoint`, `TryWriteCheckpoint`, `WriteReport`, `Bound` | ~145 |
| `Rma134AcceptanceTestDoubles.cs` (new top-level, non-partial) | `Rma134CollectingSink` (`ILocalLlmStreamSink`), `Rma134CancelOnFirstTextSink` (`ILocalLlmStreamSink`) — renamed from `CollectingSink`/`CancelOnFirstTextSink` | ~60 |
| `Rma134AcceptanceReportModels.cs` (new top-level, non-partial) | `Rma134AcceptanceCheckpoint` (already prefixed, promoted only), `Rma134AcceptanceReport` (already prefixed, promoted only), `Rma134ArtifactVerification` (renamed from `ArtifactVerification`), `Rma134PhysicsTimingReport` (renamed from `PhysicsTimingReport`) | ~80 |

### Watch out for

- **Mandatory rename, not optional**: `CollectingSink` → `Rma134CollectingSink` (4 call sites) and `ArtifactVerification` → `Rma134ArtifactVerification` (3 call sites). Skipping this produces a genuine `CS0101` duplicate-definition compile error against RMA-135's already-`internal` types in the same namespace, since both files are always compiled together (same assembly, same directory).
- For consistency with the RMA-135 precedent, also rename `CancelOnFirstTextSink` → `Rma134CancelOnFirstTextSink` (1 call site) and `PhysicsTimingReport` → `Rma134PhysicsTimingReport` (5 call sites) even though no existing collision was found for either — defense against a *future* RMA-13x split introducing the same bare name.
- Static mutable fields are shared across `Bootstrap()`/`Start()` (anchor) and the diagnostics helpers — safe under `partial class`, just declare them once, in the anchor.
- `[RuntimeInitializeOnLoadMethod]` must stay exactly on `Bootstrap()` in the anchor — don't let it get lost or duplicated during the move.
- `RunAcceptanceAsync()` constructs `Rma134CollectingSink` three times (initial generation, reset probe, post-reload generation) and `Rma134CancelOnFirstTextSink` once (cancellation probe) — all inside the moved `.Acceptance.cs` partial; make sure the renamed constructors are updated in the same diff as the type declarations move, not split across commits.
- Both workflow fixes (sha256sum list widening, paths/regex widening) are silent-gap issues (evidence-incompleteness / CI-trigger gap), not hard build failures — but should be fixed anyway, in the same commit as the step that makes the new top-level files exist.

### Extraction order

1. DTOs first → `Rma134AcceptanceReportModels.cs`: promote `Rma134AcceptanceCheckpoint`/`Rma134AcceptanceReport` to top-level `internal` (no rename needed), and rename+promote `ArtifactVerification` → `Rma134ArtifactVerification` (3 call sites) and `PhysicsTimingReport` → `Rma134PhysicsTimingReport` (5 call sites). Pure data, lowest risk, and gets the mandatory collision-avoiding rename done first.
2. Test-double helpers next → `Rma134AcceptanceTestDoubles.cs`: rename+promote `CollectingSink` → `Rma134CollectingSink` (4 call sites) and `CancelOnFirstTextSink` → `Rma134CancelOnFirstTextSink` (1 call site). Still no behavior change.
3. `Diagnostics` partial (self-contained utility methods; only dependency is the already-shared static fields established in the anchor).
4. `Acceptance` partial last (largest, most control-flow-heavy — do this only after steps 1–2 have already renamed every type it references, so this move is a pure cut-and-paste with no simultaneous rename).
5. Widen `.github/workflows/rma134-local-llm-provider.yml` (`sha256sum` file list + `paths:` globs) and `.github/workflows/rma134-local-llm-android.yml` (regex change-scope gate) — do this in the same commit as step 4, not after, since that's the point at which the new top-level files' presence would otherwise stop being covered by these gates. No Python test needs updating.

---
