# RMA-182 local validation — 2026-08-15

## Validation target

RMA-182 hardens background/foreground lifecycle behavior without changing authoritative simulation dynamics.

## Local checks

The focused static contract is `scripts/tests/test_rma182_lifecycle_hardening.py`. It verifies the simulation no-catch-up source invariant, centralized production lifecycle ingress, CameraX pause/resume behavior, speech interruption, HTTP/local-LLM cancellation generations, VLM cancellation/admission blocking, defined conversation/UI recovery, repeated-cycle managed coverage, and roadmap closure.

The repository-wide static suite and `scripts/ci.sh --static-only` are also run before publication. `git diff --check` must remain clean.

## Managed/hosted gate

The sandbox does not provide the .NET SDK or Unity Editor. Publication therefore runs warnings-as-errors managed projects on a hosted runner before `master` can move. At minimum the gate covers Core, Application, SpeechAudioFocus, VlmScheduling, LocalLlm, and Camera managed projects.

## Physical-device claim

No physical Android pause/resume result is fabricated by this local validation. The implementation is structured so a later device acceptance can repeatedly background/foreground the application and inspect simulation step progression, camera reacquisition, and interaction state without changing the RMA-182 contract.
