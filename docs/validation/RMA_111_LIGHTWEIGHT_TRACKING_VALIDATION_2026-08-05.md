# RMA-111 Lightweight Tracking Validation

**Milestone:** RMA-111

**Date:** 2026-08-05

**Status:** Complete

**Validated implementation baseline:** `7a050598766ae7a1742e863b5865a377249ae29c`

## Completed contract

RMA-111 installs bundled on-device Google ML Kit face detection and selfie
segmentation behind the RMA-110 `IVisualTracker` boundary. The production path:

- consumes only owned transformed Reachy-eye frames and their validity metadata;
- converts detector output into transformed Reachy-eye coordinates;
- suppresses detections whose transformed centers are invalid;
- assigns deterministic local face and person identifiers;
- preserves identifiers across consecutive observations;
- expires stale tracks and clears them across continuity resets;
- permits only one active native request and one bounded staged managed request;
- propagates cancellation and provider failures without substituting data;
- does not invoke a VLM;
- does not download a model at runtime; and
- leaves generic object and motion tracking disabled because no physical-device
  performance evidence justifies adding them to the bounded RMA-111 path.

The pinned Android dependencies are:

- ML Kit face detection `16.1.7`; and
- ML Kit selfie segmentation `16.0.0-beta6`.

## Fail-closed implementation evidence

The managed and Android bridge contracts verify that:

- only transformed owned frames can enter the tracker;
- invalid-center filtering occurs after coordinate transformation;
- tracker IDs are stable and deterministic rather than provider-owned handles;
- stale observations cannot survive expiry or continuity reset;
- a synchronous exception while creating `InputImage` releases the active
  request and recycles the bitmap;
- a synchronous face-detector start failure releases ownership immediately;
- person-segmentation start and result failures drain through the already
  attached face listeners without leaking request state;
- face listeners are attached before person segmentation starts;
- Java compilation, Android lint, and managed builds use warnings-as-errors;
- all physical scripts consume the one pinned `REACHY_ANDROID_SERIAL`; and
- the authoritative rendering harness cannot hide installation, package-state,
  launch, or rendering failures.

The final authoritative harness also prevents a device-test sequencing defect:
it does not destructively uninstall the exact APK installed by the preceding
physical gates. It pulls the installed base APK, compares its SHA-256 against
the candidate, reuses it only on an exact match, clears application data, and
launches authoritative acceptance. If the package is absent or mismatched, the
only fallback is a bounded, visible `adb install -r -g`; the final installed APK
must still hash exactly to the candidate before launch.

## Rejected candidates and Ralph-loop repairs

- Candidate `3b8b9440f60ff8a79de42e22537a273515b81567` was rejected when permanent
  workflow run `31064376991` failed during Unity script compilation. Four new
  runtime files declared `ReachyMini.Application`, shadowing
  `UnityEngine.Application` throughout sibling namespaces.
- Repair `f833e50cb25578499a1366eea6c015e1286baa1f` moved those types into the
  existing `ReachyMini.AppState` namespace and removed the non-Android dead
  `disposed` assignment.
- Candidate `33452be00d74e70aa52fd98b26d92c69a04797c9` was rejected when workflow
  run `31067334232` reached the editor-test assembly and found a `Color[]` passed
  to a helper requiring `Color32[]`.
- Repair `9af54646567537ea642b811b7f337a9c934088d1` corrected the validity fixture
  and hardened synchronous ML Kit request-start cleanup. One-use repair run
  `31067797579` passed its exact-pattern and scoped-diff gates.
- Candidate `baed5596f5aae474429ac0c3cd41dc4991ee8dae` proved the RMA-111 path but
  exposed cross-device contamination: the final rendering stage could select a
  different connected phone. The permanent workflow now accepts exactly one
  ARM64/API-26+ device and exports one serial consumed by every physical script.
- Candidate `834d79d7ff0a60eff23a61156527d61a4b43d982` strengthened mandatory person
  detection and stable person IDs, then correctly failed its physical invalid
  region check because a single invalid pixel did not tolerate detector-box
  jitter. The acceptance fixture was repaired to invalidate an expanded prior
  face region; production filtering was not weakened.
- Candidate `a6dba8ceb2bb61b486c3e09a11beda32f2196fa7` passed hosted run
  `31069963506` and the RMA-090/RMA-091/RMA-092/RMA-111/RMA-022 stages of local
  run `31069963484`. It was rejected because authoritative installation failed
  before rendering and captured only standard output, leaving the Package
  Manager error unavailable.
- Candidate `ecbd18883bba7a3fc05b3d1fccadead233f4a651` added bounded installation,
  but hosted run `31073903479` and local run `31073903436` exposed a second
  harness defect: a live `tee` pipeline could remain open after timeout killed
  `adb`, so no install status was written.
- Candidate `73844456c4ae9d3afa8eaa9ce895e0d73819bb76` made timeout evidence direct
  and deterministic. Local run `31076087367` then showed Android 8 returns
  status `1` with empty output when `pm path` confirms an absent package; the
  fail-closed probe had incorrectly classified that exact state as a transport
  failure.
- Candidate `f8243a6b9a9caa36b96562310c6f687445a4e4cb` accepted only the verified
  Android 8 absence state and kept all other probe failures fatal. Local run
  `31076997476` reached the actual defect: a fresh streaming install hung for
  the full 180-second deadline and exited with status `124`, despite a healthy
  ADB transport and a successful prior uninstall.
- Repair commit `823a331327abf42c749133a250dcbcd2916f2455` removed the destructive
  uninstall and added exact installed-APK digest verification. Clean baseline
  `7a050598766ae7a1742e863b5865a377249ae29c` crossed every permanent physical
  acceptance boundary.
- Evidence candidate `50db48909f8fee2b56c43a2470f4f5019d46368c` was rejected by hosted run
  `31079434227`, job `92544635569`, before physical signoff. Warnings-as-errors
  found `CA1865` in the source-contract helper, and the managed executable then
  exposed two stale literal assertions that did not match the production
  script's dynamic installed-APK digest filename and quoted equality gate.
- Repair `e8f1c02c97c5210fbb3bec5387615e5e01ef5066` changed the one-character
  prefix check to the analyzer-approved char overload and aligned both source
  assertions with the actual fail-closed shell expressions. One-use repair run
  `31079835614`, job `92545898672`, passed the complete managed executable and
  its one-file scoped-diff gate before pushing the repair.

## Exact physical validation

Permanent workflow run `31078197317`, job `92540794256`, completed successfully
on the exact clean baseline. Every step passed, including:

- generated Unity presentation preparation;
- production MuJoCo runtime staging;
- the complete Unity test suite and managed source contracts;
- ARM64/API-26 APK build and verification;
- exact-one-device pinning;
- RMA-090 camera discovery;
- RMA-091 camera acquisition;
- RMA-092 transformed texture and stale-frame acceptance;
- RMA-111 bundled lightweight tracking;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- every evidence upload;
- APK artifact upload; and
- final commit-status publication.

The pinned device was:

- serial: `LGH87250967ab9`;
- manufacturer/model: `LGE LG-H872`;
- Android: `8.0.0`;
- SDK: `26`; and
- ABI: `arm64-v8a`.

## RMA-111 artifact

Artifact `8958559677` has digest
`sha256:22545228824b4763bcbdd0e9dea3098ff7152e7339e2b76e624101747e7e1234`.
Its acceptance report records:

- status `passed`;
- backend `google-mlkit-bundled-face-selfie`;
- backend version
  `face-detection-16.1.7+segmentation-selfie-16.0.0-beta6`;
- licensed fixture SHA-256
  `bfbc798f321699c95c708476f744ba52e9faccdb0d131b8ce878f47d7704c8de`;
- one face and one person on both frames;
- stable face ID `face-000001`;
- stable person ID `person-000001`;
- `invalid_center_suppressed: true`;
- object tracking disabled;
- motion tracking disabled;
- zero VLM invocations;
- no network model download; and
- an empty error field.

## Authoritative rendering regression evidence

Artifact `8958602880` has digest
`sha256:a2fbb2e71186aadf9911bde0fb18a7388748602175a3ac2fdc685fd72c5ad166`.
It proves that the device APK selected for reuse was exactly the candidate:

- install mode: `reuse_exact_installed_apk`;
- install status: `0`;
- candidate APK SHA-256:
  `16e6d120585cec9a132f757541f7ef0ad364979f8ad439b44a45ed2cdc213590`;
- pre-launch installed APK SHA-256: the same value;
- final installed APK SHA-256: the same value;
- installed-APK verification: `true`; and
- launch status: `0`.

The authoritative report records:

- status `ok`;
- 18 model bodies;
- 17 moved bodies;
- all 6 Stewart links moved;
- body yaw, head, and both antennas moved;
- renderer structure valid;
- renderer status `Rendering`;
- runtime status `Running`;
- continuity advanced from `1` to `2` after reset; and
- `hidden_kinematic_fallback: false`.

## Final repository validation boundary

The permanent hosted camera/managed workflow includes this validation file and
the authoritative TODO in its push-path contract. Therefore the commit carrying
this completed record is required to pass both hosted warnings-as-errors checks
and the complete self-hosted Unity/Android physical suite. Repository commit
statuses and workflow artifacts for that exact commit are the authoritative
final signoff; no documentation-only exception is permitted.
