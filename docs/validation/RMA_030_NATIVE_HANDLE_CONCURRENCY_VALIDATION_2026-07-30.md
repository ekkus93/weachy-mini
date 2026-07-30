# RMA-030 native handle concurrency and output-buffer validation

**Date:** 2026-07-30  
**Branch:** `master`  
**Implementation head:** `c109b13b7909efee017d32352f4ba2a973cf1447`

## Scope

RMA-030 hardens the public `reachy_sim` ABI without changing ABI version 2.
The implementation provides nonblocking exclusive operation ownership for every
live handle and formalizes caller-owned output behavior.

The public contract now guarantees:

- at most one handle-scoped operation reaches a backend at a time;
- same-handle contention returns retryable `REACHY_SIM_STATUS_HANDLE_BUSY`;
- unrelated handles remain independent;
- destruction cannot race a live backend operation;
- invalid and stale handles retain their specific status;
- contended calls do not overwrite retained backend diagnostics;
- state and snapshot size outputs change only on success or
  `BUFFER_TOO_SMALL`;
- wrapper-rejected and undersized copy calls leave byte buffers unchanged;
- optional creation diagnostics require initialized ABI and structure size.

## Native adversarial coverage

The existing contract suite remains intact. The RMA-030 extension adds a
blocking fake-backend step and proves every public operation rejects concurrent
same-handle access. It also runs eight threads for 16,000 step attempts and
requires every attempt to return exactly `OK` or `HANDLE_BUSY`, with final state
sequence equal to the number of successful operations.

The suite covers state, snapshot, capability, last-error, reset, command,
wrench, restore, destroy, stale-generation, independent-handle, and output
sentinel behavior. It runs under warnings-as-errors, AddressSanitizer, and
UndefinedBehaviorSanitizer.

## Validation evidence

- Hosted quality run: `30534082373` — all static, native, managed, Android, and
  official-model jobs passed on `c109b13b`.
- Android MuJoCo feasibility run `30533169884`: production-identical code head
  `22fdd1f4` passed the ARM64 cross-build, architecture/provenance verification,
  and physical LG G6 production probes. The only subsequent change was the
  physical-rendering foreground wrapper.
- Self-hosted Unity/Android run `30534082314`: production staging, Unity tests,
  IL2CPP APK build/verification, RMA-022 lifecycle acceptance, authoritative
  rendering acceptance, evidence uploads, and APK upload passed on exact head
  `c109b13b`.

The physical authoritative-rendering gate now uses the same deterministic
wake, unlock, immersive-confirmation, stay-awake, exact-focus, and restoration
preflight as the RMA-022 lifecycle gate. A prior timeout on production-identical
code was traced to the phone being asleep, not to native handle contention.

## Result

RMA-030 is accepted. Same-handle backend operations are nonblocking and
exclusive, output ownership is explicit, native contention/sanitizer tests are
green, the production ARM64 library and probes are green, and the installed
Unity application passed both physical lifecycle and authoritative-rendering
regressions.
