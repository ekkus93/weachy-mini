from pathlib import Path

path = Path("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
text = path.read_text(encoding="utf-8")
old = """## RMA-104 — Build reprojection test suite

- [ ] Identity transform golden image.
- [ ] Known yaw/pitch/roll synthetic grid images.
- [ ] Camera-intrinsic scaling tests.
- [ ] Front-camera mirroring tests.
- [ ] Portrait/landscape tests.
- [ ] GPU output versus double-precision CPU reference.
- [ ] Invalid-mask boundary and stale-pixel tests.
- [ ] Actual-versus-target head orientation test.

**Acceptance criteria — camera gate**

- [ ] Actual MuJoCo head rotation changes the transformed image correctly.
- [ ] X/Y/Z translation is intentionally ignored and labeled `rotation_only`.
- [ ] Invalid coverage is explicit and testable.
- [ ] CV/VLM receive the transformed frame, not the raw phone frame, unless a debug tool explicitly requests raw input.
"""
new = """## RMA-104 — Build reprojection test suite

**Status:** Complete (2026-08-05)

- [x] Identity transform golden image.
- [x] Known yaw/pitch/roll synthetic grid images.
- [x] Camera-intrinsic scaling tests.
- [x] Front-camera mirroring tests.
- [x] Portrait/landscape tests.
- [x] GPU output versus double-precision CPU reference.
- [x] Invalid-mask boundary and stale-pixel tests.
- [x] Actual-versus-target head orientation test.

**Acceptance criteria — camera gate**

- [x] Actual MuJoCo head rotation changes the transformed image correctly.
- [x] X/Y/Z translation is intentionally ignored and labeled `rotation_only`.
- [x] Invalid coverage is explicit and testable.
- [x] CV/VLM receive the transformed frame, not the raw phone frame, unless a debug tool explicitly requests raw input.

**Completion evidence**

- An asymmetric deterministic image and a test-only CPU oracle cover identity,
  positive X/Y/Z rotations, nonuniform intrinsic scaling, front mirroring,
  90/270-degree orientation, invalid boundaries, stale target poisoning, and
  actual authoritative MuJoCo orientation versus a different requested target.
- The CPU oracle consumes the same float matrix payload as the Unity shader and
  performs projection in double precision. Production rendering retains no GPU
  readback. The identity homography is canonicalized only within `1e-12` of
  `0`, `1`, or `-1` and must report all `187/187` pixels valid.
- RMA-103 coverage now counts the final float shader payload, preventing
  coverage metadata from disagreeing with the emitted validity texture at
  numerical boundaries.
- `ReachyVisionFrameRoutingPolicy` requires transformed, validity-bearing,
  observation-eligible Reachy-eye frames for tracking, VLM, world-model,
  behavior, and diagnostics. Raw phone frames are limited to the distinct
  `ExplicitRawDebug` purpose.
- Repeated physical RMA-092 failures exposed that CameraX stop previously
  published `Stopped` before the device reached `CLOSED`. Java and Unity now
  preserve `Stopping`, retain teardown ownership and the camera observer until
  `CLOSED`, fail critical close errors visibly, and queue switches rather than
  racing a closing camera. No sleep, blind retry, or silent fallback was added.
- Hosted CI run `31035832714` passed static, managed warnings-as-errors, native
  and sanitizer, Android, and pinned Reachy-model jobs on accepted
  implementation SHA `90a9a5390ce8c893899779c89d035eb3262965e6`.
- Self-hosted run `31035832853`, job `92407563209`, passed real OpenGL Core
  Unity tests (`125/125` EditMode and `1/1` PlayMode), ARM64 API-26 APK build
  and verification, RMA-090, RMA-091, repaired RMA-092 rear rotation restart
  and front switch, RMA-022 lifecycle, authoritative rendering, every evidence
  upload, APK upload, and final status publication on the same SHA.
- Physical evidence recorded CameraX `CLOSED` before both subsequent starts,
  valid Vulkan rear output at 90 and 0 degrees, a non-uniform mirrored front
  capture, monotonic metadata, exact timestamp correspondence, and zero stale
  accepted or uploaded texture frames.
- Detailed design and evidence are in
  `docs/architecture/REPROJECTION_TEST_SUITE.md` and
  `docs/validation/RMA_104_REPROJECTION_TEST_SUITE_VALIDATION_2026-08-05.md`.
"""
if text.count(old) != 1:
    raise SystemExit("Expected exactly one incomplete RMA-104 section.")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
Path(__file__).unlink()
