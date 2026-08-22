# Claude Code Handoff — Weachy Mini

**Date:** 2026-08-22
**Repository:** `https://github.com/ekkus93/weachy-mini`
**Primary branch:** `master`
**Code baseline at handoff:** `660d17428374e2b50457fad9dbbc2da7d78cc1d9` — `fix: LoadCloudAsync had no await, tripping CS1998-as-error in Unity CI`

> The commit containing this handoff document is documentation-only. Use the current `master` after pulling, but treat `660d174...` as the exact code baseline described below. At the moment this document was written, "Local Unity Android Validation" for `660d174` was still running (all other checks, including hosted `CI`, were green) — see §4 for how to check the final result.

The previous handoff (`docs/CLAUDE_CODE_HANDOFF_2026-08-15.md`) covered RMA-135/RMA-183/RMA-184. That handoff's items have mostly moved forward since (see §5). This document supersedes it as the current entry point, but the 2026-08-15 file is still useful background on RMA-135's failure history if you end up debugging that ticket again.

## 1. How to work on this repository

1. Start from an up-to-date local checkout of `master`.
2. Read `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` before choosing the next task — it is the single authoritative source of truth for what's done vs. open. It is large (~3,200 lines); read the specific ticket sections you need rather than the whole file if your tool has a context budget.
3. Also skim `CLAUDE.md` at the repo root — it has the current build/test/lint commands, code-style rules, and gotchas (warnings-as-errors policy, asset policy, git-LFS scope, Android SDK-version notes). Don't duplicate that content here; it doesn't go stale as fast as this document does.
4. Make the smallest change that preserves the existing fail-closed architecture and invariants (see the "no silent fallback" language repeated throughout the TODO doc — it's a hard project rule, not boilerplate).
5. Run lint, unit, static-contract, and compile/static checks locally as far as the local environment allows before pushing. `./scripts/ci.sh` (full) or `./scripts/ci.sh --static-only` (fast) are the canonical local gates — see `CLAUDE.md`.
6. History lands directly on `master`. No feature branches or PRs are used in this repo. Commit messages are usually `RMA-<n>: <summary>` for ticket work, or `fix:`/`docs:`/`chore:` for follow-ups.
7. Do **not** spend time polling/monitoring GitHub Actions unless the user explicitly asks you to watch it. When asked to watch, use whatever your environment's background-monitoring primitive is — don't busy-poll in the foreground.
8. When a device/Unity-only gate cannot be executed locally, say so clearly and leave the physical gate for the self-hosted runner rather than weakening acceptance.
9. Never add silent fallback behavior to force an acceptance gate to pass. This is called out repeatedly in the TODO doc per-ticket and is treated as a hard violation, not a style preference.
10. Two Unity-only source trees genuinely cannot be compile-verified from a non-Unity environment: `Assets/ReachyMini/Runtime/Application/**` and `Assets/ReachyMini/Runtime/Core/**` files that aren't included in `managed/ReachyMini.Core.csproj`'s globs (`Runtime/Core/**`, `Runtime/Interop/**`, `Runtime/Simulation/**` only — NOT `Runtime/Application/**`, NOT `Runtime/Behavior/**`, NOT `Runtime/Providers/**`, NOT `Runtime/Perception/**`). Files outside those globs are Unity-assembly-only and their *only* real compile check is the self-hosted "Local Unity Android Validation" CI job. Be extra careful reviewing changes there by hand (nullable flow, `using` correctness, `async`/`await` correctness — see §6 below for a concrete bug this caught).

## 2. Current high-level project state

The project is deep in Phase 19/20 hardening. Almost every ticket through Phase 19 (RMA-001 through RMA-184) is either fully complete or complete-except-for-physical-device-evidence. **The active frontier of work is RMA-195** ("wire application composition to real subsystems") in Phase 20, which several other Phase 20 tickets are explicitly blocked on.

One-paragraph summary: the Unity-side "composition" layer historically wired every application service to permanently-stub/Unavailable implementations rather than the real production subsystems that already existed lower in the stack (camera, local-LLM provider selection, camera perception/world-model, baseline behavior, and now cloud-LLM provider selection). RMA-195 is the ticket tracking replacing those stubs with the real wiring, phase by phase. Phases A, B, and C are complete; Phase D is partially complete (cloud-LLM composition wiring landed this session — see §3). RMA-190 (automated E2E scenarios) and two of RMA-194's release-acceptance items are explicitly blocked until RMA-195 finishes.

Outside RMA-195, the other genuinely-open items are:

- **RMA-012** — in-app license/attribution/"unofficial project" notice screen: not built yet (partial license-list UI exists, but no disclosure statement).
- **RMA-074** — first calibrated profile from a physical Reachy Mini: no physical data acquired yet.
- **RMA-125** — full offline ASR→conversation proof: blocked because the only physical test phone (LG-H872, API 26) can't run RMA-121's on-device ASR, which needs API 31.
- **RMA-135** — resource/thermal governor: the *governance mechanism* is complete and tested, but physical acceptance surfaced a **real SM-A546E thermal-throttling characteristic** (not a governor bug) — see `docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md`. RMA-135's Android acceptance workflow is expected to intermittently fail/skip on real hardware for this reason; treat that as known/non-actionable unless new evidence suggests otherwise.
- **RMA-142** — OpenAI-compatible LLM adapter: partially complete. Text-only chat adapters (Responses + Chat Completions styles) are implemented and tested (this session, see §3); **image/vision input is deliberately deferred to RMA-115** (the VLM ticket), not RMA-142.
- **RMA-173** — silent-failure regression tests: fully open.
- **RMA-184** — representative low/mid/high device matrix: tooling, contracts, and low-class (LG-H872) evidence exist; mid (SM-A546E)/high (OnePlus 11 5G, Pixel 7 Pro) profiles are deliberately left `pending_measurement` — there is a repo test (`test_roadmap_keeps_physical_qualification_open`) that asserts this stays open until real physical long-run data lands. Don't "fix" that test to pass by fabricating measurements.
- **RMA-190/191/192/193/194** — Phase 20 E2E/security/license/docs/release-acceptance: all still open, several explicitly blocked on RMA-195.

None of these require a decision before starting except RMA-195's remaining scope (see §3.3) and RMA-012/RMA-193, which involve product-facing copy a human should probably review before Claude writes final text.

## 3. This session's work (2026-08-21 → 2026-08-22)

All of the following is already committed and pushed to `master`. Commits, newest first, ending at this handoff's baseline:

```text
660d174  fix: LoadCloudAsync had no await, tripping CS1998-as-error in Unity CI
aa9f6ce  RMA-195 Phase D: wire cloud LLM adapter into provider-selection composition
3426700  fix: RMA-132 HTTPS check assertion is stale against the RMA-163 centralized host-policy refactor
651881a  RMA-142/143: implement OpenAI-compatible cloud LLM adapter
6a128cc  docs: correct stale RMA-140/141/144/145/146/150/151/152/153/154 checkboxes against verified code+tests
b162983  docs: correct stale RMA-161/162 checkboxes; partial RMA-184/194 evidence notes
984731c  docs: correct stale RMA-120/121/122/123/124/125 checkboxes against verified code+tests
```

### 3.1 TODO-doc staleness sweep (`984731c`, `b162983`, `6a128cc`)

A large number of tickets (RMA-120–125, RMA-140/141/144–146, RMA-150–154, RMA-161/162, plus partial evidence notes for RMA-184/194) had their implementation already complete in code/tests but the TODO doc's checkboxes were never updated. Each was individually spot-checked against real code/test evidence before editing — this was *not* a blind find-and-replace. If you find another stale-looking checkbox, verify against actual code before trusting the doc; the doc had a real, non-trivial backlog of this kind of drift.

### 3.2 RMA-142/143 — OpenAI-compatible cloud LLM adapter (`651881a`)

A brand-new subsystem (~1,000+ lines across 6 new files), implemented from scratch this session:

- `Assets/ReachyMini/Runtime/Core/Providers/ReachyLlmContracts.cs` — request/response/capability contracts (`ReachyLlmGenerationRequest`, `ReachyLlmGenerationResult`, `ReachyLlmCapabilities`, `ICloudLlmProviderCapability`).
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleLlmOptions.cs` — endpoint-style options (OpenAI Responses vs. Chat Completions defaults).
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyLlmResponseProtocolParser.cs` — hand-rolled bounded JSON scanner (this codebase never uses `System.Text.Json` in production code — every protocol adapter, ASR/TTS/LLM alike, hand-rolls its own narrow recursive-descent parser with depth bounds and duplicate-field rejection; follow that convention if you add another one).
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleLlmProvider.Core.cs` / `.Helpers.cs` — the provider implementation (`OpenAiResponsesLlmProvider`, `OpenAiChatCompletionsLlmProvider`).
- `managed/ReachyMini.Core.Tests/Rma142Rma143OpenAiLlmAdapterContractTests.cs` — 8 mock-HTTP-server contract tests.

`ReachyLlmGenerationResult` deliberately reuses RMA-151's `ReachyBehaviorIntentValidationResult`/`ReachyBehaviorIntentJsonParser.Validate(string)` as its structured-output representation rather than inventing a second schema.

Two real bugs were caught purely by the mock-server tests (not hypothetical — both reproduced before the fix):
1. The test double never set `response.RequestMessage = request`, which made the shared HTTP transport's redirect-safety check reject every mock response as an untrusted redirect.
2. The `transportOverride` test constructor seam bypasses the provider's normal bearer-credential header-injection logic (that logic only runs in the "build our own transport" constructor branch) — tests using the override seam must manually call `ReachyBearerCredentialTransportBinding.Create(...)` themselves, mirroring what the real constructor does.

Deliberately out of scope for RMA-142/143: image/vision input (belongs to RMA-115), and any composition/settings wiring (that's RMA-195 Phase D, done partially next).

### 3.3 RMA-132 drive-by fix (`3426700`)

Found while triaging RMA-142/143's own CI run — a pre-existing, unrelated static-assertion bug in `scripts/tests/test_rma132_local_model_packages.py`: it checked for the literal string `Uri.UriSchemeHttps`, but RMA-163 had already refactored the actual code to a centralized `ReachyNetworkEndpointSecurity.RequirePublicHttpsUri(...)` helper. One-line fix to check for the current string instead. Confirmed via `gh run` history that this predated this session and was unrelated to any of this session's changes.

### 3.4 RMA-195 Phase D — cloud LLM composition wiring (`aa9f6ce`, fixed by `660d174`)

**Scope decision, made explicitly by the user via a direct question, not inferred:** "composition wiring only." A prior scoping investigation found that full end-to-end wiring would require product/UX decisions (a settings text-input UI for cloud credentials, privacy-confirmation copy, and a live conversational-turn trigger) that are not engineering-only calls. The user chose to land composition wiring now and defer the UI/UX-dependent pieces.

What shipped:

- `ReachyLocalLlmProviderApplicationService` (in `Assets/ReachyMini/Runtime/Application/`, despite its name — it now owns both local and cloud LLM) additionally implements `ICloudLlmProviderCapability` alongside the existing `ILocalLlmProviderCapability`, using the optional-castable-capability pattern already established for `IReachyProviderGovernorDiagnosticsSource`. This pattern was required, not chosen for style: `ReachyApplicationComposition.CreateComplete` enforces one registration per `ReachyServiceKind` via `Dictionary.TryAdd` — a second `Provider`-kind registration would throw `ArgumentException` at composition-build time. A dedicated investigation confirmed this constraint before writing any code.
- New `GenerateAsync(ReachyLlmGenerationRequest, CancellationToken)` overload lazily resolves a `ReachyProviderProfile` (from `ReachyProviderProfilePersistenceStore`), an Android-Keystore-backed secret store, and — per the ticket's privacy requirement — actually drives the two-phase `ReachyProviderFallbackPolicyEngine` authorization flow (`EvaluateProviderSwitch(request, confirmation: null)` → `Denied`/`RequiresPrivacyConfirmation`/`Authorized`; if the latter, `ConfirmPrivacyBoundaryChange` then a second `EvaluateProviderSwitch` call) before constructing an `OpenAiResponsesLlmProvider`/`OpenAiChatCompletionsLlmProvider`.
- **Important fail-closed consequence, not a bug**: every workload's default policy is `ReachyFallbackPolicy.NoFallback()` (all flags false). Since no settings UI exists yet to grant cloud-switch authorization, `EvaluateProviderSwitch` will always return `Denied` on a real unconfigured device today. That is correct fail-closed behavior, not something to "fix" — it will start working once a settings surface exists to grant the authorization (part of the deferred scope below).
- `PublishCurrentHealth()`/`OnDispose()` extended to recognize the Cloud LLM selection.
- 3 new EditMode tests in `Assets/ReachyMini/Tests/Editor/ReachyLocalLlmProviderApplicationServiceTests.cs`: `CloudCapabilitiesAreTextOnly`, `InitializeReportsDegradedWhenCloudLlmIsSelected`, `CloudGenerateAsyncFailsClosedOffAndroidWithoutThrowing`.

**Explicitly deferred, not started:**
1. Settings UI to create/edit a cloud LLM provider profile + credential (no text-input widget exists anywhere in the app's `OnGUI`-based settings system today — confirmed by investigation, not assumed).
2. Settings UI / user flow to grant the fallback-policy privacy-boundary authorization (the "privacy-confirmation copy" gap — there's no UI to present or confirm the OnDevice→Cloud privacy-boundary change).
3. Any live call site — `ICloudLlmProviderCapability.GenerateAsync` is reachable only from unit tests right now, not from a real conversational turn. This mirrors the same still-open gap on the local-LLM path.
4. VLM-based cloud perception (`ReachyVlmScheduler` / `ReachyOpenAiVisionLanguageProviders`) — also part of Phase D's stated scope, entirely unstarted.

**Also flagged but not fixed:** `ReachyProductionApplicationCompositionProvider` was found to be confirmed dead code (never referenced anywhere) during this investigation. It needs either removal or a documented reason to keep it. Nobody has picked this up yet.

The Phase D TODO checkbox is deliberately left unchecked — only the composition-wiring slice is done.

### 3.5 CS1998 compile fix (`660d174`)

The `aa9f6ce` push broke "Local Unity Android Validation" (the self-hosted Unity Editor compile check — the *only* real compiler for `Runtime/Application/**` files). Root cause: `LoadCloudAsync` (added in `aa9f6ce`) was declared `async` but had no `await` anywhere in its body — every path returns synchronously. This repo's warnings-as-errors policy turns CS1998 into a hard build failure. Hosted `CI` (which only builds the `dotnet`-buildable subset) stayed green the whole time, which is exactly why §1 item 10 above matters — this bug was invisible to every local check available in a non-Unity environment. Fix: removed `async`, changed the return type to plain `Task<(...)>`, wrapped every return with `Task.FromResult<(...)>(...)`. The caller (`EnsureCloudLoadedAsync`) already did `await LoadCloudAsync(...)`, which works identically against a non-`async` `Task`-returning method — no caller changes needed.

## 4. CI status at handoff time

As of this document's baseline commit, `gh run list` for `660d174` showed:

- `CI` (hosted, dotnet-buildable subset) — **success**.
- `Local Unity Android Validation` (self-hosted, the real compile gate for Unity-only files) — **in progress** at write time. This is the one that matters most given §3.5 — **check this first** before doing anything else. If it's still red, the CS1998 fix in `660d174` may not have been sufficient, or a new issue was introduced; read the failure log with `gh run view <run-id> --log-failed` rather than guessing.
- RMA-134/RMA-135 Android acceptance workflows were `skipped` on the immediately-prior commit (`aa9f6ce`) — this is a `workflow_run`-triggered gate that only fires after Local Unity Android Validation succeeds, so it hadn't run yet for `660d174` either at write time. A `skipped` conclusion here is not itself concerning; it just means the upstream gate hadn't finished.

To check current status yourself:

```bash
gh run list --commit "$(git rev-parse 660d174)" --limit 20
# or, for the very latest master commit:
gh run list --commit "$(git rev-parse master)" --limit 20
```

Use the **full** SHA with `gh run list --commit` — an abbreviated short SHA silently returns an empty list with no error, which wasted real debugging time earlier this session.

If Local Unity Android Validation is green and RMA-134/135 have reported: RMA-134 is expected green; RMA-135 may legitimately fail/skip due to the known SM-A546E thermal characteristic (§2) — that alone is not a regression to chase.

## 5. Status changes vs. the 2026-08-15 handoff

For anyone who read the previous handoff (`docs/CLAUDE_CODE_HANDOFF_2026-08-15.md`) first:

- **RMA-135**: the postload-stabilization pacing fix it described (`db05da6...`) held up under further physical testing. The remaining RMA-135 friction is now understood to be a genuine SM-A546E thermal-throttling characteristic (see `docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md`), not an acceptance-harness or governor bug. A governor-cadence hysteresis bug that was masking this finding was found and fixed the same day it was discovered. Treat further RMA-135 device-acceptance flakiness as expected/non-actionable unless you find new evidence otherwise — don't relitigate the fail/pass threshold without real data.
- **RMA-183**: the roadmap bookkeeping gap the 2026-08-15 handoff flagged has been resolved — RMA-183 is marked complete in the TODO doc.
- **RMA-184**: still open, as predicted — mid/high physical long-run evidence is still pending. No change in substance, just confirmed still-accurate.
- **The "next priority" from 2026-08-15 was RMA-135 physical acceptance.** That's no longer the frontier — RMA-195 (which didn't exist as a tracked ticket on 2026-08-15) is now the active frontier, per §2/§3 above.

## 6. Recommended execution order from this handoff

### Priority 1 — confirm `660d174` is fully green

Check §4. If Local Unity Android Validation is still red, diagnose via `gh run view <id> --log-failed` and fix following the same pattern as §3.5 (find the exact compiler diagnostic, fix minimally, re-verify locally where possible, push, re-watch).

### Priority 2 — decide and execute the next slice of RMA-195 Phase D

Two real options, and this is a genuine decision point — don't just pick one:

- **(a)** Build the settings UI pieces deferred in §3.4 (cloud-credential input, privacy-confirmation copy, a live trigger) to actually complete Phase D. This is real UX/product work, not pure plumbing — the app's settings screen (`ReachyMainScreen.SettingsPanel.cs` / `.SettingsSections.cs`) is `OnGUI`-based with no existing text-input widget, so this may also be a good moment to ask whether the settings UI framework itself needs new primitives.
- **(b)** Start the VLM half of Phase D (cloud perception composition wiring) instead, since it's independent of the settings-UI gap and might be pure composition wiring like the LLM half was.

Either way, resolve or document the dead-code flag on `ReachyProductionApplicationCompositionProvider` (§3.4) while you're in this area — it's small and easy to forget.

### Priority 3 — RMA-190 (once RMA-195 unblocks it)

Automated E2E scenarios. Explicitly blocked on RMA-195 today per the TODO doc; re-check whether that block is fully lifted once Phase D (or whatever slice of it is needed for RMA-190) lands.

### Priority 4 — the remaining physical-evidence-gated tickets

RMA-074 (first calibrated profile), RMA-125 (offline ASR proof — needs an API-31 phone), RMA-184 (mid/high device long-run qualification) all require physical hardware/data that may not be available in every environment. Don't fabricate evidence to close these; leave them open with an honest status if the hardware isn't available to you.

### Priority 5 — RMA-012, RMA-173, RMA-191, RMA-192, RMA-193, RMA-194

Round out Phase 20. RMA-012 and RMA-193 involve user-facing copy (license/attribution notices, docs) — reasonable to draft but worth a human read-through before considering them final, since they're product-facing statements, not just code.

## 7. Validation philosophy (carried forward, still applies)

1. Identify the exact source SHA first.
2. Distinguish hosted/static, Local Unity, and physical-device failures — they have very different diagnostic paths and different tools can see different subsets (§1 item 10, §3.5).
3. For physical-device acceptance failures, use the uploaded evidence artifact/checkpoints, not just the final exception.
4. Preserve the production fail-closed safety invariants — never weaken a threshold or add a silent fallback just to make a gate pass.
5. Add a regression contract for every acceptance bug you fix.
6. Verify a "stale doc checkbox" against real code/test evidence before flipping it — don't blind-fix based on ticket age or title alone (§3.1).
7. Keep the TODO doc honest: implemented != physically qualified != closed. Several tickets in this repo are intentionally left open despite complete code, because physical evidence is the actual gate.

## 8. One-sentence current status

**RMA-195 (application-composition wiring) is the active frontier — Phases A/B/C are complete and Phase D's cloud-LLM composition wiring just landed (pending final Local Unity Android Validation confirmation on `660d174`), with the cloud-credential settings UI, privacy-confirmation UX, live trigger wiring, and VLM half of Phase D still explicitly deferred; RMA-135's remaining friction is a known real SM-A546E thermal characteristic rather than a bug; RMA-184's mid/high physical device qualification remains genuinely incomplete; and Phase 20's RMA-190 through RMA-194 release-readiness work is otherwise unstarted.**
