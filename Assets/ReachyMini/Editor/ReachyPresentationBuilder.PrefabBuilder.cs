using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
        private static void CreatePrefab(
            RenderManifest manifest,
            IReadOnlyDictionary<string, Material> materials,
            IReadOnlyDictionary<string, Mesh> meshes)
        {
            GameObject root = new GameObject("ReachyMiniPresentation");
            try
            {
                ReachyPresentationRoot metadata =
                    root.AddComponent<ReachyPresentationRoot>();
                metadata.ConfigureGeneratedPresentation(
                    manifest.schema_version,
                    manifest.source.model_sha256,
                    manifest.bodies.Length,
                    manifest.visual_geoms.Length);

                Dictionary<string, Transform> bodies =
                    new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (BodyEntry body in manifest.bodies)
                {
                    ValidateBody(body, bodies);
                    Transform parent = string.Equals(
                        body.parent_path,
                        "/world",
                        StringComparison.Ordinal)
                        ? root.transform
                        : bodies[body.parent_path];
                    GameObject bodyObject = new GameObject(
                        $"Body_{body.index:D2}_{SanitizeName(body.name)}");
                    bodyObject.transform.SetParent(parent, false);
                    ApplyPose(bodyObject.transform, body.local_pose_unity);
                    ReachyPresentationBody bodyMetadata =
                        bodyObject.AddComponent<ReachyPresentationBody>();
                    bodyMetadata.ConfigureGeneratedBody(
                        body.index,
                        body.path,
                        body.name);
                    bodies.Add(body.path, bodyObject.transform);
                }

                foreach (VisualGeomEntry geom in manifest.visual_geoms)
                {
                    if (geom == null || !bodies.TryGetValue(
                            geom.body_path,
                            out Transform parent) ||
                        !meshes.TryGetValue(geom.mesh, out Mesh mesh) ||
                        !materials.TryGetValue(
                            geom.material,
                            out Material material))
                    {
                        throw new InvalidDataException(
                            $"Unity visual geometry {geom?.path} has an unresolved reference.");
                    }
                    GameObject visual = new GameObject(
                        $"Visual_{geom.index:D3}_{SanitizeName(geom.mesh)}");
                    visual.transform.SetParent(parent, false);
                    ApplyPose(visual.transform, geom.local_pose_unity);
                    MeshFilter filter = visual.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                }

                AssertNoUnityPhysics(root);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    PrefabPath,
                    out bool success);
                if (!success || prefab == null)
                {
                    throw new IOException(
                        $"Unity could not save generated Reachy prefab: {PrefabPath}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidateBody(
            BodyEntry body,
            IReadOnlyDictionary<string, Transform> existingBodies)
        {
            if (body == null || body.index < 0 ||
                string.IsNullOrWhiteSpace(body.path) ||
                string.IsNullOrWhiteSpace(body.parent_path) ||
                body.local_pose_unity == null)
            {
                throw new InvalidDataException(
                    "Unity render manifest contains a malformed body.");
            }
            if (existingBodies.ContainsKey(body.path))
            {
                throw new InvalidDataException(
                    $"Duplicate Unity presentation body path: {body.path}");
            }
            if (!string.Equals(
                    body.parent_path,
                    "/world",
                    StringComparison.Ordinal) &&
                !existingBodies.ContainsKey(body.parent_path))
            {
                throw new InvalidDataException(
                    $"Unity presentation body parent precedes no known body: " +
                    $"{body.parent_path}");
            }
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Anonymous";
            }
            char[] result = value
                .Select(character => char.IsLetterOrDigit(character) ||
                    character == '_' || character == '-'
                    ? character
                    : '_')
                .ToArray();
            return new string(result);
        }

        private static void ApplyPose(Transform transform, PoseEntry pose)
        {
            if (pose.position_metres == null ||
                pose.position_metres.Length != 3 ||
                pose.quaternion_wxyz == null ||
                pose.quaternion_wxyz.Length != 4 ||
                !pose.position_metres.All(IsFinite) ||
                !pose.quaternion_wxyz.All(IsFinite))
            {
                throw new InvalidDataException(
                    "Unity render manifest contains a malformed local pose.");
            }

            Vector3 position = new Vector3(
                (float)pose.position_metres[0],
                (float)pose.position_metres[1],
                (float)pose.position_metres[2]);
            Quaternion rotation = new Quaternion(
                (float)pose.quaternion_wxyz[1],
                (float)pose.quaternion_wxyz[2],
                (float)pose.quaternion_wxyz[3],
                (float)pose.quaternion_wxyz[0]);
            float magnitudeSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (!IsFinite(position.x) || !IsFinite(position.y) ||
                !IsFinite(position.z) || !IsFinite(magnitudeSquared) ||
                magnitudeSquared <= 0f)
            {
                throw new InvalidDataException(
                    "Unity render manifest pose cannot be represented as finite Unity floats.");
            }
            transform.localPosition = position;
            transform.localRotation = rotation.normalized;
        }

        private static void AssertNoUnityPhysics(GameObject root)
        {
            if (root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                root.GetComponentsInChildren<Joint>(true).Length != 0 ||
                root.GetComponentsInChildren<ArticulationBody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Generated Reachy presentation contains forbidden Unity physics components.");
            }
        }
    }
}
