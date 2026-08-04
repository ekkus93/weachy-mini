#!/usr/bin/env python3
from pathlib import Path

TODO_PATH = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")

OLD_BLOCK = """## RMA-091 — Implement CameraX frame acquisition

- [ ] Bind preview and `ImageAnalysis` lifecycle-aware use cases.
- [ ] Use a bounded backpressure strategy that discards stale analysis frames.
- [ ] Carry timestamp, sensor orientation, lens facing, crop, pixel format, and intrinsics with each frame.
- [ ] Close every `ImageProxy` exactly once.
- [ ] Avoid copying to CPU formats unless a consumer requires it.
- [ ] Support explicit front/rear switching with orderly teardown.

**Tests**

- [ ] rapid start/stop;
- [ ] repeated front/rear switch;
- [ ] pause/resume;
- [ ] permission revoke;
- [ ] analyzer overrun;
- [ ] device rotation;
- [ ] camera unavailable.
"""

NEW_BLOCK = """## RMA-091 — Implement CameraX frame acquisition

**Status:** Complete (2026-08-04)

- [x] Bind preview and `ImageAnalysis` lifecycle-aware use cases.
- [x] Use a bounded backpressure strategy that discards stale analysis frames.
- [x] Carry timestamp, sensor orientation, lens facing, crop, pixel format, and intrinsics with each frame.
- [x] Close every `ImageProxy` exactly once.
- [x] Avoid copying to CPU formats unless a consumer requires it.
- [x] Support explicit front/rear switching with orderly teardown.

**Tests**

- [x] rapid start/stop;
- [x] repeated front/rear switch;
- [x] pause/resume;
- [x] permission revoke;
- [x] analyzer overrun;
- [x] device rotation;
- [x] camera unavailable.

**Completion evidence**

- CameraX 1.6.1 binds exact-camera `Preview` and `ImageAnalysis` use cases to
  an explicit lifecycle owner. Analysis remains `YUV_420_888`, uses
  `STRATEGY_KEEP_ONLY_LATEST`, and publishes metadata without accessing image
  planes or copying pixels into Unity.
- Generation and session identities reject callbacks from stopped or replaced
  streams. `ImageProxy.close()` has one explicit call site in the analyzer
  `finally` block, and orderly teardown clears the analyzer, unbinds both use
  cases, destroys lifecycle state, closes the private preview surface, and
  stops its executor.
- Permanent managed, Unity, and static contracts cover rapid start/stop,
  repeated front/rear switching, pause/resume, revocation, stale/analyzer
  metadata, unavailable cameras, exact Camera2 selection, and the no-CPU-copy
  boundary retained for RMA-092.
- Self-hosted run `30934825724`, job `92078267747`, passed 85 edit-mode tests,
  one play-mode test, ARM64/API-26 IL2CPP build and verification, RMA-090
  discovery, RMA-091 physical acquisition, RMA-022 lifecycle acceptance,
  authoritative rendering, and APK upload on exact implementation commit
  `25b496917d47f53e217d67ae7d996b91fa5dce81`.
- The LG-H872/API-26 sequence recorded four sessions and 58 frame observations,
  rear and front frames, continuous analyzer progress, pause/resume recovery,
  orderly stop/restart and switching, output rotation changing from 90 to 0
  degrees after display rotation, zero stale/faulted transitions, and final
  `PermissionRevoked` state.
- Camera evidence artifact `8902852311` has digest
  `sha256:5e5251a39a8dc14bf7da91f828ad0d56cbe0ee1667a49950c4273f671a73e462`.
- Detailed design and evidence are in
  `docs/architecture/ANDROID_CAMERAX_FRAME_ACQUISITION.md` and
  `docs/validation/RMA_091_CAMERA_ACQUISITION_VALIDATION_2026-08-04.md`.
"""


def main() -> None:
    original = TODO_PATH.read_text(encoding="utf-8")
    occurrences = original.count(OLD_BLOCK)
    if occurrences != 1:
        raise SystemExit(
            f"Expected exactly one unchanged RMA-091 block, found {occurrences}."
        )
    updated = original.replace(OLD_BLOCK, NEW_BLOCK, 1)
    if updated == original:
        raise SystemExit("RMA-091 TODO replacement produced no change.")
    TODO_PATH.write_text(updated, encoding="utf-8")


if __name__ == "__main__":
    main()
