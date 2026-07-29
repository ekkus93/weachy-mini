#!/usr/bin/env python3
"""Convert the imported Reachy MJCF/STL package into Unity-ready render assets."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import shutil
import struct
import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

SCHEMA_VERSION = 1
TRANSFORMATION_ID = "reachy_stl_to_unity_obj_v1"
FLOAT_PATTERN = r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?"


class UnityAssetError(RuntimeError):
    """Raised when source render assets cannot be converted safely."""


@dataclass(frozen=True)
class Triangle:
    """One STL triangle in MuJoCo-local coordinates."""

    normal: tuple[float, float, float]
    vertices: tuple[
        tuple[float, float, float],
        tuple[float, float, float],
        tuple[float, float, float],
    ]


def sha256(path: Path) -> str:
    """Return a file's SHA-256 digest."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    """Read a JSON object."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise UnityAssetError(f"Cannot read JSON object {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise UnityAssetError(f"JSON root must be an object: {path}")
    return value


def checked_relative_file(root: Path, relative_text: str) -> Path:
    """Resolve one required file without allowing traversal or symlink escape."""
    relative = Path(relative_text)
    if relative.is_absolute() or ".." in relative.parts:
        raise UnityAssetError(f"Unsafe source-relative path: {relative_text}")
    resolved = (root / relative).resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError as exc:
        raise UnityAssetError(f"Source path escapes imported package: {relative_text}") from exc
    if not resolved.is_file():
        raise UnityAssetError(f"Required source file does not exist: {relative_text}")
    return resolved


def finite_vector(values: Iterable[float], label: str) -> tuple[float, ...]:
    """Return finite float values or fail visibly."""
    result = tuple(float(value) for value in values)
    if not all(math.isfinite(value) for value in result):
        raise UnityAssetError(f"{label} contains NaN or infinity")
    return result


def parse_vector(text: str | None, count: int, default: tuple[float, ...], label: str) -> tuple[float, ...]:
    """Parse a fixed-width MJCF vector."""
    if text is None:
        return default
    fields = text.split()
    if len(fields) != count:
        raise UnityAssetError(f"{label} must contain {count} values, found {len(fields)}")
    try:
        values = finite_vector((float(field) for field in fields), label)
    except ValueError as exc:
        raise UnityAssetError(f"{label} contains a non-numeric value") from exc
    return values


def coordinate_vector(vector: tuple[float, float, float]) -> tuple[float, float, float]:
    """Map MuJoCo (x, y, z) to Unity (x, z, y)."""
    return vector[0], vector[2], vector[1]


def matrix_from_quaternion(quaternion: tuple[float, float, float, float]) -> tuple[tuple[float, ...], ...]:
    """Convert a normalized wxyz quaternion to a 3x3 rotation matrix."""
    w, x, y, z = quaternion
    norm = math.sqrt(w * w + x * x + y * y + z * z)
    if norm <= 0.0 or not math.isfinite(norm):
        raise UnityAssetError("MJCF quaternion has zero or invalid norm")
    w, x, y, z = (value / norm for value in quaternion)
    return (
        (1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y - z * w), 2.0 * (x * z + y * w)),
        (2.0 * (x * y + z * w), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z - x * w)),
        (2.0 * (x * z - y * w), 2.0 * (y * z + x * w), 1.0 - 2.0 * (x * x + y * y)),
    )


def quaternion_from_matrix(matrix: tuple[tuple[float, ...], ...]) -> tuple[float, float, float, float]:
    """Convert a proper 3x3 rotation matrix to canonical wxyz quaternion form."""
    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        quaternion = (
            0.25 * scale,
            (matrix[2][1] - matrix[1][2]) / scale,
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[1][0] - matrix[0][1]) / scale,
        )
    elif matrix[0][0] > matrix[1][1] and matrix[0][0] > matrix[2][2]:
        scale = math.sqrt(1.0 + matrix[0][0] - matrix[1][1] - matrix[2][2]) * 2.0
        quaternion = (
            (matrix[2][1] - matrix[1][2]) / scale,
            0.25 * scale,
            (matrix[0][1] + matrix[1][0]) / scale,
            (matrix[0][2] + matrix[2][0]) / scale,
        )
    elif matrix[1][1] > matrix[2][2]:
        scale = math.sqrt(1.0 + matrix[1][1] - matrix[0][0] - matrix[2][2]) * 2.0
        quaternion = (
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[0][1] + matrix[1][0]) / scale,
            0.25 * scale,
            (matrix[1][2] + matrix[2][1]) / scale,
        )
    else:
        scale = math.sqrt(1.0 + matrix[2][2] - matrix[0][0] - matrix[1][1]) * 2.0
        quaternion = (
            (matrix[1][0] - matrix[0][1]) / scale,
            (matrix[0][2] + matrix[2][0]) / scale,
            (matrix[1][2] + matrix[2][1]) / scale,
            0.25 * scale,
        )
    norm = math.sqrt(sum(value * value for value in quaternion))
    result = tuple(value / norm for value in quaternion)
    for value in result:
        if abs(value) > 1e-15:
            if value < 0.0:
                result = tuple(-component for component in result)
            break
    return result


def coordinate_quaternion(quaternion: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    """Conjugate a MuJoCo rotation by the x/z/y reflection basis change."""
    source = matrix_from_quaternion(quaternion)
    order = (0, 2, 1)
    converted = tuple(tuple(source[order[row]][order[column]] for column in range(3)) for row in range(3))
    return quaternion_from_matrix(converted)


def parse_binary_stl(data: bytes, path: Path) -> list[Triangle] | None:
    """Parse an exact-length binary STL, or return None for an ASCII candidate."""
    if len(data) < 84:
        return None
    triangle_count = struct.unpack_from("<I", data, 80)[0]
    expected_size = 84 + triangle_count * 50
    if expected_size != len(data):
        return None
    triangles: list[Triangle] = []
    offset = 84
    for index in range(triangle_count):
        values = finite_vector(struct.unpack_from("<12f", data, offset), f"{path} triangle {index}")
        triangles.append(
            Triangle(
                normal=(values[0], values[1], values[2]),
                vertices=(
                    (values[3], values[4], values[5]),
                    (values[6], values[7], values[8]),
                    (values[9], values[10], values[11]),
                ),
            )
        )
        offset += 50
    return triangles


def parse_ascii_stl(data: bytes, path: Path) -> list[Triangle]:
    """Parse a strict ASCII STL."""
    try:
        text = data.decode("ascii")
    except UnicodeDecodeError as exc:
        raise UnityAssetError(f"STL is neither exact binary nor ASCII: {path}") from exc
    number = re.compile(FLOAT_PATTERN)
    triangles: list[Triangle] = []
    current_normal: tuple[float, float, float] | None = None
    vertices: list[tuple[float, float, float]] = []
    for line_number, raw_line in enumerate(text.splitlines(), start=1):
        line = raw_line.strip()
        if not line or line.startswith("solid ") or line.startswith("endsolid"):
            continue
        if line.startswith("facet normal "):
            if current_normal is not None:
                raise UnityAssetError(f"Nested ASCII STL facet at {path}:{line_number}")
            fields = number.findall(line.removeprefix("facet normal "))
            if len(fields) != 3:
                raise UnityAssetError(f"Invalid ASCII STL normal at {path}:{line_number}")
            current_normal = tuple(float(field) for field in fields)
            finite_vector(current_normal, f"{path}:{line_number} normal")
            vertices = []
        elif line.startswith("vertex "):
            if current_normal is None:
                raise UnityAssetError(f"ASCII STL vertex outside facet at {path}:{line_number}")
            fields = number.findall(line.removeprefix("vertex "))
            if len(fields) != 3:
                raise UnityAssetError(f"Invalid ASCII STL vertex at {path}:{line_number}")
            vertex = tuple(float(field) for field in fields)
            finite_vector(vertex, f"{path}:{line_number} vertex")
            vertices.append(vertex)
        elif line == "endfacet":
            if current_normal is None or len(vertices) != 3:
                raise UnityAssetError(f"Incomplete ASCII STL facet at {path}:{line_number}")
            triangles.append(
                Triangle(
                    normal=current_normal,
                    vertices=(vertices[0], vertices[1], vertices[2]),
                )
            )
            current_normal = None
            vertices = []
        elif line not in {"outer loop", "endloop"}:
            raise UnityAssetError(f"Unsupported ASCII STL syntax at {path}:{line_number}: {line}")
    if current_normal is not None:
        raise UnityAssetError(f"Unclosed ASCII STL facet: {path}")
    if not triangles:
        raise UnityAssetError(f"STL contains no triangles: {path}")
    return triangles


def read_stl(path: Path) -> list[Triangle]:
    """Read binary or ASCII STL triangles."""
    try:
        data = path.read_bytes()
    except OSError as exc:
        raise UnityAssetError(f"Cannot read STL {path}: {exc}") from exc
    triangles = parse_binary_stl(data, path)
    return triangles if triangles is not None else parse_ascii_stl(data, path)


def normalize(vector: tuple[float, float, float]) -> tuple[float, float, float]:
    """Normalize a vector, retaining zero for a missing STL normal."""
    length = math.sqrt(sum(value * value for value in vector))
    if length <= 0.0:
        return 0.0, 0.0, 0.0
    return tuple(value / length for value in vector)


def cross(left: tuple[float, float, float], right: tuple[float, float, float]) -> tuple[float, float, float]:
    """Return the vector cross product."""
    return (
        left[1] * right[2] - left[2] * right[1],
        left[2] * right[0] - left[0] * right[2],
        left[0] * right[1] - left[1] * right[0],
    )


def subtract(left: tuple[float, float, float], right: tuple[float, float, float]) -> tuple[float, float, float]:
    """Subtract two vectors."""
    return tuple(left[index] - right[index] for index in range(3))


def format_number(value: float) -> str:
    """Render deterministic finite OBJ numeric text."""
    if not math.isfinite(value):
        raise UnityAssetError("Attempted to write non-finite OBJ coordinate")
    if value == 0.0:
        value = 0.0
    return format(value, ".17g")


def write_obj(path: Path, source_path: Path, scale: tuple[float, float, float]) -> int:
    """Convert one STL to a Unity-coordinate OBJ and return triangle count."""
    triangles = read_stl(source_path)
    lines = [
        f"# Generated by {TRANSFORMATION_ID}",
        f"# Source SHA-256: {sha256(source_path)}",
        "o ReachyMesh",
    ]
    vertex_index = 1
    normal_index = 1
    for triangle in triangles:
        converted_vertices = tuple(
            coordinate_vector(tuple(vertex[axis] * scale[axis] for axis in range(3)))
            for vertex in triangle.vertices
        )
        source_normal = normalize(triangle.normal)
        converted_normal = normalize((-source_normal[0], -source_normal[2], -source_normal[1]))
        if converted_normal == (0.0, 0.0, 0.0):
            converted_normal = normalize(
                cross(
                    subtract(converted_vertices[2], converted_vertices[0]),
                    subtract(converted_vertices[1], converted_vertices[0]),
                )
            )
        for vertex in converted_vertices:
            lines.append("v " + " ".join(format_number(value) for value in vertex))
        lines.append("vn " + " ".join(format_number(value) for value in converted_normal))
        lines.append(
            "f "
            f"{vertex_index}//{normal_index} "
            f"{vertex_index + 2}//{normal_index} "
            f"{vertex_index + 1}//{normal_index}"
        )
        vertex_index += 3
        normal_index += 1
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return len(triangles)


def body_path(parent_path: str, index: int, name: str | None) -> str:
    """Create the stable body path used by MODEL_MAP.json."""
    return f"{parent_path}/{name if name else f'@body[{index}]'}"


def unity_pose(element: ET.Element, label: str) -> dict[str, list[float]]:
    """Convert one MJCF local pose to Unity coordinates."""
    position = parse_vector(element.attrib.get("pos"), 3, (0.0, 0.0, 0.0), f"{label} pos")
    quaternion = parse_vector(element.attrib.get("quat"), 4, (1.0, 0.0, 0.0, 0.0), f"{label} quat")
    converted_position = coordinate_vector((position[0], position[1], position[2]))
    converted_quaternion = coordinate_quaternion(
        (quaternion[0], quaternion[1], quaternion[2], quaternion[3])
    )
    return {
        "position_metres": list(converted_position),
        "quaternion_wxyz": list(converted_quaternion),
    }


def build_render_manifest(source_root: Path, staging: Path) -> dict[str, Any]:
    """Convert meshes and build the body/material/visual-geom render contract."""
    model_path = checked_relative_file(source_root, "reachy_mini.xml")
    try:
        root = ET.fromstring(model_path.read_bytes())
    except (OSError, ET.ParseError) as exc:
        raise UnityAssetError(f"Cannot parse imported Reachy MJCF: {exc}") from exc
    if root.tag != "mujoco" or root.attrib.get("model") != "reachy_mini":
        raise UnityAssetError("Imported MJCF is not the pinned Reachy Mini model")

    compiler = root.find("compiler")
    mesh_directory = compiler.attrib.get("meshdir", "") if compiler is not None else ""
    mesh_assets: dict[str, dict[str, Any]] = {}
    outputs: list[dict[str, Any]] = []
    asset_root = root.find("asset")
    if asset_root is None:
        raise UnityAssetError("Imported MJCF has no asset section")
    for index, mesh in enumerate(asset_root.findall("mesh")):
        mesh_name = mesh.attrib.get("name") or Path(mesh.attrib.get("file", "")).stem
        mesh_file = mesh.attrib.get("file")
        if not mesh_name or not mesh_file:
            raise UnityAssetError(f"Mesh asset {index} is missing name/file identity")
        if mesh_name in mesh_assets:
            raise UnityAssetError(f"Duplicate mesh asset name: {mesh_name}")
        source_relative = (Path(mesh_directory) / mesh_file).as_posix()
        source_path = checked_relative_file(source_root, source_relative)
        scale_values = parse_vector(mesh.attrib.get("scale"), 3, (1.0, 1.0, 1.0), f"mesh {mesh_name} scale")
        output_relative = (Path("Meshes") / Path(mesh_file).with_suffix(".obj")).as_posix()
        output_path = staging / output_relative
        triangle_count = write_obj(
            output_path,
            source_path,
            (scale_values[0], scale_values[1], scale_values[2]),
        )
        entry = {
            "name": mesh_name,
            "source_path": source_relative,
            "source_sha256": sha256(source_path),
            "output_path": output_relative,
            "output_sha256": sha256(output_path),
            "triangle_count": triangle_count,
        }
        mesh_assets[mesh_name] = entry
        outputs.append(entry)

    materials: list[dict[str, Any]] = []
    material_names: set[str] = set()
    for index, material in enumerate(asset_root.findall("material")):
        name = material.attrib.get("name")
        if not name or name in material_names:
            raise UnityAssetError(f"Material {index} has missing or duplicate name")
        material_names.add(name)
        rgba = parse_vector(material.attrib.get("rgba"), 4, (1.0, 1.0, 1.0, 1.0), f"material {name} rgba")
        materials.append({"name": name, "rgba": list(rgba)})

    bodies: list[dict[str, Any]] = []
    visual_geoms: list[dict[str, Any]] = []

    def walk_body(body: ET.Element, parent_path: str, sibling_index: int) -> None:
        name = body.attrib.get("name")
        path = body_path(parent_path, sibling_index, name)
        bodies.append(
            {
                "index": len(bodies),
                "name": name,
                "path": path,
                "parent_path": parent_path,
                "local_pose_unity": unity_pose(body, f"body {path}"),
            }
        )
        geoms = [child for child in body if child.tag == "geom"]
        for geom_index, geom in enumerate(geoms):
            if geom.attrib.get("class") != "visual":
                continue
            mesh_name = geom.attrib.get("mesh")
            material_name = geom.attrib.get("material")
            if not mesh_name or mesh_name not in mesh_assets:
                raise UnityAssetError(f"Visual geom {path}[{geom_index}] has unknown mesh {mesh_name!r}")
            if not material_name or material_name not in material_names:
                raise UnityAssetError(
                    f"Visual geom {path}[{geom_index}] has unknown material {material_name!r}"
                )
            visual_geoms.append(
                {
                    "index": len(visual_geoms),
                    "path": f"{path}/@visual_geom[{geom_index}]",
                    "body_path": path,
                    "mesh": mesh_name,
                    "mesh_output_path": mesh_assets[mesh_name]["output_path"],
                    "material": material_name,
                    "local_pose_unity": unity_pose(geom, f"visual geom {path}[{geom_index}]"),
                }
            )
        child_bodies = [child for child in body if child.tag == "body"]
        for child_index, child in enumerate(child_bodies):
            walk_body(child, path, child_index)

    worldbody = root.find("worldbody")
    if worldbody is None:
        raise UnityAssetError("Imported MJCF has no worldbody")
    for index, body in enumerate(worldbody.findall("body")):
        walk_body(body, "/world", index)

    source_cameras = []
    for camera in root.findall(".//camera"):
        source_cameras.append(
            {
                "name": camera.attrib.get("name"),
                "included_in_presentation": False,
                "reason": "MuJoCo source camera is solver/model metadata; Unity presentation camera is independent.",
            }
        )

    provenance_path = checked_relative_file(source_root, "PROVENANCE.json")
    source_provenance = read_json(provenance_path)
    return {
        "schema_version": SCHEMA_VERSION,
        "transformation": {
            "id": TRANSFORMATION_ID,
            "source_content_modified": False,
            "coordinate_mapping": {
                "mujoco": "right-handed; +Z up; quaternion wxyz",
                "unity": "left-handed; +Y up; quaternion stored wxyz in manifest",
                "vector_rule": "unity(x,y,z) = mujoco(x,z,y)",
                "rotation_rule": "R_unity = M * R_mujoco * inverse(M), M maps (x,y,z) to (x,z,y)",
                "mesh_winding_reversed": True,
            },
        },
        "source": {
            "model_path": "reachy_mini.xml",
            "model_sha256": sha256(model_path),
            "provenance_sha256": sha256(provenance_path),
            "source_commit": source_provenance.get("source_commit"),
        },
        "meshes": sorted(outputs, key=lambda item: item["name"]),
        "materials": sorted(materials, key=lambda item: item["name"]),
        "bodies": bodies,
        "visual_geoms": visual_geoms,
        "source_cameras": source_cameras,
        "presentation": {
            "camera_source": "Unity-only fixed presentation camera",
            "source_cameras_included": False,
            "authoritative_transform_source": "MuJoCo body snapshots only",
        },
    }


def write_conversion(source_root: Path, output_root: Path) -> Path:
    """Write a transactional conversion, preserving prior output on failure."""
    with tempfile.TemporaryDirectory(prefix="reachy-unity-", dir=output_root.parent) as temp_text:
        staging = Path(temp_text) / output_root.name
        staging.mkdir(parents=True)
        manifest = build_render_manifest(source_root, staging)
        (staging / "UNITY_RENDER_MAP.json").write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        if output_root.exists():
            shutil.rmtree(output_root)
        output_root.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(staging), output_root)
    return output_root


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    """Convert imported Reachy assets for Unity."""
    args = parse_args()
    try:
        source = args.source.resolve()
        if not source.is_dir():
            raise UnityAssetError(f"Imported Reachy source directory does not exist: {source}")
        output = args.output.resolve()
        if output == source or source in output.parents:
            raise UnityAssetError("Unity conversion output must not overwrite the imported source package")
        destination = write_conversion(source, output)
    except (UnityAssetError, OSError) as exc:
        print(f"Reachy Unity asset preparation failed: {exc}", file=sys.stderr)
        return 1
    print(f"Reachy Unity asset preparation completed: {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
