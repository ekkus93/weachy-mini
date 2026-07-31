#!/usr/bin/env python3
"""Verify and apply the chunked one-shot RMA-073 implementation archive."""
from __future__ import annotations

import base64
import hashlib
import io
import shutil
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
PAYLOAD = ROOT / "scripts/rma073-bootstrap-payload"
ARCHIVE_SHA256 = "f9e79db4d202b01db1a06b1c061bbeea6df804b5a23e5cf1ceb4a5614421cfe1"
CHUNKS = [
    ("chunk-000.b64", "9fcb614db530d956fe343e6fa209320432ff36820bb98f8cfb99105cf794c235"),
    ("chunk-001.b64", "b85bc01799e4e579b91dbef7d6f88a27f0299183ecb6254db58a2cdaadaa1f00"),
    ("chunk-002.b64", "a6d346df8e6615ec0264fb8015fc9a0606194a6370ee19d888984e314bcd0db7"),
    ("chunk-003.b64", "c9bb21a5f45a382319b86c49b189d99e4a30683bbf51fe68aeeb9ea2694e116b"),
    ("chunk-004.b64", "195497dcec30660c37f43d3626cde6824f60fa809a078c005d42d653d4a848cb"),
    ("chunk-005.b64", "6cd464db4294c550f4d38b846991e40f81cf7658e5bb73795febb4d02346e343"),
    ("chunk-006.b64", "fc08d087ea53cf89bf568d3fbb1515426088c7d45bbdaae442abc2c409042fe3"),
    ("chunk-007.b64", "51427ff91419e493a8ff456549474a1a6522bb11d66f000cab3c439e1e7fbccf"),
    ("chunk-008.b64", "f55acd2ed5dbc50c0717934056af9d18b38b38b9df44c8c044d6ad7534156bce"),
    ("chunk-009.b64", "a69d59e49ccb11ac92d0e4996e080cd3dc7ff3da0133466331b62627dc52a5f1"),
]
EXPECTED_FILES = {
    ".github/workflows/rma073-calibration-fitting.yml",
    "calibration/fitting/rma073-compatibility.json",
    "calibration/fixtures/keys/rma073-test-ed25519-private.pem",
    "calibration/fixtures/keys/rma073-test-ed25519-public.pem",
    "calibration/schemas/calibration-fit-plan-v1.schema.json",
    "calibration/schemas/calibration-profile-manifest-v1.schema.json",
    "docs/architecture/CALIBRATION_PARAMETER_FITTING.md",
    "docs/validation/RMA_073_PARAMETER_FITTING_VALIDATION_2026-07-31.md",
    "scripts/calibration_fitting.py",
    "scripts/fit_calibration_profile.py",
    "scripts/generate_rma073_synthetic_data.py",
    "scripts/tests/test_calibration_fitting.py",
    "scripts/verify_calibration_profile.py",
}

encoded_parts: list[str] = []
for name, expected_sha in CHUNKS:
    path = PAYLOAD / name
    raw = path.read_bytes()
    actual_sha = hashlib.sha256(raw).hexdigest()
    if actual_sha != expected_sha:
        raise RuntimeError(f"RMA-073 payload chunk {name} hash mismatch: {actual_sha}")
    encoded_parts.append(raw.decode("ascii").strip())
archive = base64.b64decode("".join(encoded_parts), validate=True)
actual_archive_sha = hashlib.sha256(archive).hexdigest()
if actual_archive_sha != ARCHIVE_SHA256:
    raise RuntimeError(f"RMA-073 archive hash mismatch: {actual_archive_sha}")

with zipfile.ZipFile(io.BytesIO(archive), "r") as package:
    names = set(package.namelist())
    if names != EXPECTED_FILES:
        raise RuntimeError(f"RMA-073 archive file set mismatch: {sorted(names ^ EXPECTED_FILES)}")
    for name in sorted(names):
        pure = PurePosixPath(name)
        if pure.is_absolute() or ".." in pure.parts:
            raise RuntimeError(f"unsafe archive path: {name}")
        destination = ROOT.joinpath(*pure.parts)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(package.read(name))

TODO_PATH = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
todo = TODO_PATH.read_text(encoding="utf-8")
old = """## RMA-073 — Implement parameter fitting and held-out validation

- [ ] Separate training/fitting datasets from held-out validation datasets.
- [ ] Fit friction, backlash, latency, controller, voltage, compliance, and thermal parameters where data supports them.
- [ ] Report confidence or sensitivity.
- [ ] Generate a signed/hashed calibration profile manifest.
- [ ] Reject profiles incompatible with model or simulator versions.
"""
new = """## RMA-073 — Implement parameter fitting and held-out validation

**Status:** Complete (2026-07-31)

- [x] Separate training/fitting datasets from held-out validation datasets.
- [x] Fit friction, backlash, latency, controller, voltage, compliance, and thermal parameters where data supports them.
- [x] Report confidence or sensitivity.
- [x] Generate a signed/hashed calibration profile manifest.
- [x] Reject profiles incompatible with model or simulator versions.

**Completion evidence**

- `rma073_calibration_fit_plan_v1` binds each RMA-070 dataset by canonical
  SHA-256 and immutable `fitting` or `heldout` role. IDs, paths, and hashes
  cannot be reused across the split, all datasets must describe the same robot
  and register configuration, and unsafe or out-of-root paths fail closed.
- The fitting stage consumes only fitting-role datasets and freezes its output
  before held-out data is loaded. Friction, backlash, command latency,
  controller gains, supply voltage/source impedance, compliance, and thermal
  parameters are estimated only when their required streams and observation
  counts exist; unsupported families retain an explicit reason and no value.
- Every fitted family reports observation count, training error or robust
  spread, a dataset-qualified confidence label, and leave-one-out or robust
  sensitivity. Held-out validation records the independent metric, threshold,
  sample count, and pass/fail result for each supported family.
- `rma073_calibration_profile_manifest_v1` preserves exact fit-plan, fitting
  dataset, held-out dataset, model, MuJoCo, ABI, and RMA-061 through RMA-064
  contract identities. It carries a canonical SHA-256 and Ed25519 signature.
  Verification rejects content drift, the wrong public key, or any exact
  compatibility mismatch.
- RMA-073 can emit only `fit_candidate_unapproved` manifests with
  `calibrated=false`; attempts to sign a calibrated claim fail closed. The
  committed key pair is an explicitly non-secret synthetic test fixture.
- Deterministic synthetic training and held-out data validate all seven
  estimators without claiming physical Reachy measurements. Physical data,
  unit-specific fitting, profile approval, and the calibrated label remain
  RMA-074 work.
- Detailed design and accepted automated evidence are in
  `docs/architecture/CALIBRATION_PARAMETER_FITTING.md` and
  `docs/validation/RMA_073_PARAMETER_FITTING_VALIDATION_2026-07-31.md`.
"""
if todo.count(old) != 1:
    raise RuntimeError(f"expected exactly one unfinished RMA-073 block, found {todo.count(old)}")
TODO_PATH.write_text(todo.replace(old, new), encoding="utf-8", newline="\n")

for relative in [
    ".github/workflows/rma073-apply-once.yml",
    "scripts/apply_rma073_once.py",
    "scripts/apply_rma073_part2_once.py",
    "scripts/apply_rma073_part3_once.py",
    "docs/validation/RMA_073_BOOTSTRAP_TRIGGER.tmp",
    "scripts/apply_rma073_archive_once.py",
]:
    path = ROOT / relative
    if path.exists():
        path.unlink()
if PAYLOAD.exists():
    shutil.rmtree(PAYLOAD)
print(f"applied verified RMA-073 archive {ARCHIVE_SHA256}")
