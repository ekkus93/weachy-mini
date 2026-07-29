#!/usr/bin/env python3
"""Import pinned Reachy Mini model assets from a clean upstream checkout."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any

from reachy_model_map import (
    ModelMapError,
    build_model_map,
    load_mjcf,
    sha256,
    validate_model_requirements,
    write_model_map,
)
from source_checkout import SourceCheckoutError, validate_clean_checkout

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOCK_PATH = ROOT / "third_party" / "reachy-mini-source.lock.json"
DEFAULT_OUTPUT_ROOT = ROOT / "Assets" / "Generated"


class AssetImportError(RuntimeError):
    """Raised when source provenance or asset import validation fails."""


def read_json(path: Path) -> dict[str, Any]:
    """Read a JSON object from disk."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise AssetImportError(f"Cannot read JSON file {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise AssetImportError(f"JSON root must be an object: {path}")
    return value


def validate_lock(lock: dict[str, Any]) -> None:
    """Validate required lock-file fields."""
    required = {
        "schema_version",
        "repository",
        "commit",
        "license_file",
        "model_file",
        "output_subdirectory",
        "asset_license",
        "software_license",
        "model_requirements",
    }
    missing = sorted(required.difference(lock))
    if missing:
        raise AssetImportError(f"Source lock is missing fields: {', '.join(missing)}")
    if lock["schema_version"] != 1:
        raise AssetImportError(f"Unsupported source-lock schema: {lock['schema_version']}")


def checked_source_path(source_root: Path, relative_text: str) -> Path:
    """Resolve a manifest/XML path while preventing traversal and symlink escape."""
    relative = Path(relative_text)
    if relative.is_absolute() or ".." in relative.parts:
        raise AssetImportError(f"Unsafe source-relative path: {relative_text}")
    resolved = (source_root / relative).resolve()
    try:
        resolved.relative_to(source_root.resolve())
    except ValueError as exc:
        raise AssetImportError(f"Source path escapes checkout: {relative_text}") from exc
    if not resolved.is_file():
        raise AssetImportError(f"Required source file does not exist: {relative_text}")
    return resolved


def discover_model_files(
    source: Path,
    model_relative: str,
    root: Any,
) -> list[tuple[str, Path]]:
    """Discover the model, license-independent mesh inputs, and output paths."""
    model_path = checked_source_path(source, model_relative)
    compiler = root.find("compiler")
    mesh_directory = ""
    if compiler is not None:
        mesh_directory = compiler.attrib.get("meshdir", "")

    model_parent = Path(model_relative).parent
    discovered: dict[str, Path] = {Path(model_relative).name: model_path}
    for mesh in root.findall("./asset/mesh"):
        mesh_file = mesh.attrib.get("file")
        if not mesh_file:
            raise AssetImportError("Reachy MJCF contains a mesh without a file attribute.")
        relative_source = model_parent / mesh_directory / mesh_file
        source_path = checked_source_path(source, relative_source.as_posix())
        output_relative = (Path(mesh_directory) / mesh_file).as_posix()
        if output_relative in discovered and discovered[output_relative] != source_path:
            raise AssetImportError(f"Conflicting mesh output path: {output_relative}")
        discovered[output_relative] = source_path

    return sorted(discovered.items())


def write_import(
    source: Path,
    lock_path: Path,
    lock: dict[str, Any],
    output_root: Path,
) -> Path:
    """Create a deterministic imported asset directory and provenance report."""
    output_subdirectory = Path(lock["output_subdirectory"])
    if output_subdirectory.is_absolute() or ".." in output_subdirectory.parts:
        raise AssetImportError("Output subdirectory must be relative and must not contain '..'.")
    destination = (output_root / output_subdirectory).resolve()
    try:
        destination.relative_to(output_root.resolve())
    except ValueError as exc:
        raise AssetImportError("Output directory escapes configured output root.") from exc

    model_path = checked_source_path(source, lock["model_file"])
    model_root = load_mjcf(model_path, lock["model_file"])
    model_map = build_model_map(model_root, model_path, lock["model_file"])
    validate_model_requirements(model_map, lock["model_requirements"])

    inputs = discover_model_files(source, lock["model_file"], model_root)
    license_source = checked_source_path(source, lock["license_file"])
    inputs.append(("UPSTREAM_LICENSE", license_source))
    inputs.sort()

    with tempfile.TemporaryDirectory(
        prefix="reachy-import-",
        dir=output_root.parent,
    ) as temp_text:
        staging = Path(temp_text) / output_subdirectory
        staging.mkdir(parents=True, exist_ok=False)
        provenance_files: list[dict[str, object]] = []
        for relative_output, source_path in inputs:
            destination_path = staging / relative_output
            destination_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source_path, destination_path)
            provenance_files.append(
                {
                    "path": relative_output,
                    "size": destination_path.stat().st_size,
                    "sha256": sha256(destination_path),
                }
            )

        attribution = (
            "# Reachy Mini imported assets\n\n"
            "Source: Pollen Robotics Reachy Mini repository.\n\n"
            f"Pinned commit: `{lock['commit']}`.\n\n"
            f"Software license: {lock['software_license']}.\n\n"
            f"Hardware/model asset license: {lock['asset_license']}.\n\n"
            "Files were copied without content modification by the deterministic import script. "
            "This project is unofficial and is not endorsed by Pollen Robotics or Hugging Face.\n"
        )
        attribution_path = staging / "ATTRIBUTION.md"
        attribution_path.write_text(
            attribution,
            encoding="utf-8",
            newline="\n",
        )
        provenance_files.append(
            {
                "path": "ATTRIBUTION.md",
                "size": attribution_path.stat().st_size,
                "sha256": sha256(attribution_path),
            }
        )

        model_map_path = staging / "MODEL_MAP.json"
        write_model_map(model_map_path, model_map)
        provenance_files.append(
            {
                "path": "MODEL_MAP.json",
                "size": model_map_path.stat().st_size,
                "sha256": sha256(model_map_path),
            }
        )

        report = {
            "schema_version": 1,
            "source_repository": lock["repository"],
            "source_commit": lock["commit"],
            "source_lock": lock_path.name,
            "content_modified": False,
            "files": sorted(provenance_files, key=lambda item: str(item["path"])),
        }
        (staging / "PROVENANCE.json").write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )

        if destination.exists():
            shutil.rmtree(destination)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(staging), destination)

    return destination


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source",
        required=True,
        type=Path,
        help="Clean upstream Git checkout.",
    )
    parser.add_argument("--lock", type=Path, default=DEFAULT_LOCK_PATH)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    return parser.parse_args()


def main() -> int:
    """Validate provenance and import pinned Reachy assets."""
    args = parse_args()
    try:
        lock_path = args.lock.resolve()
        lock = read_json(lock_path)
        validate_lock(lock)
        source = args.source.resolve()
        validate_clean_checkout(source, lock["commit"])
        output_root = args.output_root.resolve()
        output_root.mkdir(parents=True, exist_ok=True)
        destination = write_import(source, lock_path, lock, output_root)
    except (AssetImportError, ModelMapError, SourceCheckoutError) as exc:
        print(f"Reachy asset import failed: {exc}", file=sys.stderr)
        return 1

    print(f"Reachy asset import completed: {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
