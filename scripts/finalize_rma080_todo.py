#!/usr/bin/env python3
"""Record the already-validated RMA-080 completion in the authoritative TODO."""

from pathlib import Path

TODO = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
START = "## RMA-080 — Create application state architecture\n"
END = "## RMA-081 — Build the main screen\n"
REPLACEMENT = """## RMA-080 — Create application state architecture

**Status:** Complete (2026-07-31)

- [x] Define app-level services and dependency construction.
- [x] Separate simulation, camera, audio, provider, perception, behavior, persistence, and UI interfaces.
- [x] Ensure services are explicitly initialized and disposed.
- [x] Add a top-level health/status model.

**Completion evidence**

- `ReachyApplicationComposition` validates exactly one service for each of the
  eight application boundaries, rejects incomplete/duplicate/cyclic graphs,
  constructs in deterministic dependency order, and restricts factories to
  explicitly declared dependencies.
- `ReachyApplicationHost` separates construction from initialization, rolls
  back failures in reverse order, disposes exhaustively and idempotently, and
  publishes immutable application/service health snapshots with monotonic
  revisions and required-versus-optional degradation rules.
- The shared contracts live under `Assets/ReachyMini/Runtime/Core/Application`
  without a Unity dependency. `ReachyApplicationHostBehaviour` is the narrow
  explicit Unity lifecycle bridge and has no fallback composition.
- The managed warnings-as-errors contract covers valid dependency order,
  malformed graphs, undeclared dependencies, factory mismatches, startup
  rollback, exhaustive disposal, health aggregation, immutable snapshots, and
  one-shot lifecycle behavior.
- Self-hosted run `30677109292` passed 71 Unity edit-mode tests, one play-mode
  test, the ARM64 API-26 IL2CPP build, installed LG-phone native lifecycle
  acceptance, and installed authoritative-rendering acceptance on exact commit
  `da0418c95bd1278976b5cacc4683775a78f1a395`.
- Detailed design and evidence are in
  `docs/architecture/APPLICATION_STATE_ARCHITECTURE.md` and
  `docs/validation/RMA_080_APPLICATION_STATE_ARCHITECTURE_VALIDATION_2026-07-31.md`.

"""


def main() -> None:
    text = TODO.read_text(encoding="utf-8")
    if "**Status:** Complete (2026-07-31)" in text[text.index(START) :]:
        return
    start = text.index(START)
    end = text.index(END, start)
    TODO.write_text(text[:start] + REPLACEMENT + text[end:], encoding="utf-8")


if __name__ == "__main__":
    main()
