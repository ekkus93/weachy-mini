using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
        private static void ValidateManifest(RenderManifest manifest)
        {
            if (manifest == null || manifest.schema_version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported Unity render manifest schema: {manifest?.schema_version}");
            }
            if (manifest.source == null ||
                !IsSha256(manifest.source.model_sha256))
            {
                throw new InvalidDataException(
                    "Unity render manifest source model SHA-256 is invalid.");
            }
            if (manifest.presentation == null ||
                manifest.presentation.source_cameras_included ||
                !string.Equals(
                    manifest.presentation.authoritative_transform_source,
                    "MuJoCo body snapshots only",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unity render manifest presentation authority is invalid.");
            }
            if (manifest.bodies == null || manifest.bodies.Length != 18)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain exactly 18 model bodies.");
            }

            HashSet<string> bodyPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bodies.Length; ++index)
            {
                BodyEntry body = manifest.bodies[index];
                bool anonymousBody = index == 15;
                if (body == null || body.index != index ||
                    string.IsNullOrWhiteSpace(body.path) ||
                    string.IsNullOrWhiteSpace(body.parent_path) ||
                    body.local_pose_unity == null ||
                    !bodyPaths.Add(body.path) ||
                    (anonymousBody
                        ? !string.IsNullOrEmpty(body.name)
                        : string.IsNullOrWhiteSpace(body.name)))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} is malformed.");
                }
                if (!string.Equals(
                        body.parent_path,
                        "/world",
                        StringComparison.Ordinal) &&
                    !bodyPaths.Contains(body.parent_path))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} has an unknown parent.");
                }
                if (!body.path.StartsWith(
                        body.parent_path + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} path is outside its parent.");
                }
            }

            if (manifest.meshes == null || manifest.meshes.Length == 0 ||
                manifest.materials == null || manifest.materials.Length == 0 ||
                manifest.visual_geoms == null || manifest.visual_geoms.Length == 0)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain meshes, materials, and visual geoms.");
            }

            Dictionary<string, MeshEntry> meshEntries =
                new Dictionary<string, MeshEntry>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.meshes.Length; ++index)
            {
                MeshEntry mesh = manifest.meshes[index];
                if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) ||
                    string.IsNullOrWhiteSpace(mesh.source_path) ||
                    !IsSha256(mesh.source_sha256) ||
                    mesh.source_scale == null || mesh.source_scale.Length != 3 ||
                    !mesh.source_scale.All(IsFinite) ||
                    !mesh.scale_baked_into_vertices ||
                    string.IsNullOrWhiteSpace(mesh.output_path) ||
                    !IsSha256(mesh.output_sha256) || mesh.triangle_count <= 0 ||
                    !meshEntries.TryAdd(mesh.name, mesh))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest mesh {index} is malformed.");
                }
            }

            HashSet<string> materialNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.materials.Length; ++index)
            {
                MaterialEntry material = manifest.materials[index];
                if (material == null || string.IsNullOrWhiteSpace(material.name) ||
                    material.rgba == null || material.rgba.Length != 4 ||
                    material.rgba.Any(value =>
                        !IsFinite(value) || value < 0.0 || value > 1.0) ||
                    !materialNames.Add(material.name))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest material {index} is malformed.");
                }
            }

            HashSet<string> visualPaths =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.visual_geoms.Length; ++index)
            {
                VisualGeomEntry geom = manifest.visual_geoms[index];
                if (geom == null || geom.index != index ||
                    string.IsNullOrWhiteSpace(geom.path) ||
                    string.IsNullOrWhiteSpace(geom.body_path) ||
                    string.IsNullOrWhiteSpace(geom.mesh) ||
                    string.IsNullOrWhiteSpace(geom.mesh_output_path) ||
                    string.IsNullOrWhiteSpace(geom.material) ||
                    geom.local_pose_unity == null ||
                    !visualPaths.Add(geom.path) ||
                    !bodyPaths.Contains(geom.body_path) ||
                    !meshEntries.TryGetValue(geom.mesh, out MeshEntry mesh) ||
                    !string.Equals(
                        geom.mesh_output_path,
                        mesh.output_path,
                        StringComparison.Ordinal) ||
                    !materialNames.Contains(geom.material))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest visual geometry {index} is malformed.");
                }
            }

            if (manifest.source_cameras == null ||
                manifest.source_cameras.Length != 2 ||
                manifest.source_cameras.Any(camera =>
                    camera == null || camera.included_in_presentation))
            {
                throw new InvalidDataException(
                    "MuJoCo source cameras must remain excluded from Unity presentation.");
            }
            HashSet<string> sourceCameraNames = new HashSet<string>(
                manifest.source_cameras.Select(camera => camera.name),
                StringComparer.Ordinal);
            if (!sourceCameraNames.SetEquals(
                    new[] { "studio_close", "eye_camera" }))
            {
                throw new InvalidDataException(
                    "Unity render manifest contains an unexpected source camera set.");
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }
            return value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f'));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
