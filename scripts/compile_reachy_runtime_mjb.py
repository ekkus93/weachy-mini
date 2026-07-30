#!/usr/bin/env python3
"""Compile the pinned Reachy MJCF to MJB and emit a runtime manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import mujoco

EXPECTED_MUJOCO_VERSION = "3.9.0"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()

    if mujoco.__version__ != EXPECTED_MUJOCO_VERSION:
        raise SystemExit(
            f"MuJoCo version mismatch: expected {EXPECTED_MUJOCO_VERSION}, "
            f"found {mujoco.__version__}"
        )
    model_path = args.model.resolve(strict=True)
    output_path = args.output.resolve()
    manifest_path = args.manifest.resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)

    spec = mujoco.MjSpec.from_file(str(model_path))
    model = spec.compile()
    spec.encode(str(output_path), model=model)
    if not output_path.is_file() or output_path.stat().st_size <= 0:
        raise SystemExit(f"MuJoCo did not produce an MJB: {output_path}")

    manifest = {
        "schema_version": 1,
        "mujoco_version": EXPECTED_MUJOCO_VERSION,
        "source_model_sha256": sha256(model_path),
        "mjb_sha256": sha256(output_path),
        "model_byte_count": output_path.stat().st_size,
        "body_pose_count": int(model.nbody) - 1,
        "actuator_count": int(model.nu),
        "qpos_count": int(model.nq),
        "qvel_count": int(model.nv),
    }
    if manifest["body_pose_count"] != 18:
        raise SystemExit(f"Unexpected non-world body count: {manifest}")
    if manifest["actuator_count"] != 9:
        raise SystemExit(f"Unexpected actuator count: {manifest}")
    if manifest["qpos_count"] != 37 or manifest["qvel_count"] != 30:
        raise SystemExit(f"Unexpected state dimensions: {manifest}")

    manifest_path.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(manifest, sort_keys=True))


if __name__ == "__main__":
    main()
