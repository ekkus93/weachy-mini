using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReachyMini.Editor
{
    public static class ReachyPresentationBuilder
    {
        public const string GeneratedRoot =
            "Assets/Generated/ReachyMini/UnityPresentation";
        public const string PrefabPath =
            GeneratedRoot + "/Resources/ReachyMiniPresentation.prefab";
        public const string ScenePath =
            GeneratedRoot + "/ReachyMiniPresentation.unity";

        private const string RenderRootEnvironmentVariable =
            "REACHY_UNITY_RENDER_ROOT";
        private const string ManifestFileName = "UNITY_RENDER_MAP.json";
        private const string MeshAssetDirectory = GeneratedRoot + "/Meshes";
        private const string MaterialAssetDirectory = GeneratedRoot + "/Materials";

        public static void BuildFromCommandLine()
        {
            BuildPresentation();
        }

        public static void BuildPresentation()
        {
            string renderRoot = ResolveRenderRoot();
            RenderManifest manifest = ReadManifest(renderRoot);
            ValidateManifest(manifest);

            ReplaceGeneratedRoot();
            Dictionary<string, Material> materials = CreateMaterials(manifest.materials);
            Dictionary<string, Mesh> meshes = CreateMeshes(renderRoot, manifest.meshes);
            CreatePrefab(manifest, materials, meshes);
            CreatePresentationScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"Generated Reachy presentation: bodies={manifest.bodies.Length}, " +
                $"visual_geoms={manifest.visual_geoms.Length}, scene={ScenePath}");
        }

        private static string ResolveRenderRoot()
        {
            string configured = Environment.GetEnvironmentVariable(
                RenderRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                throw new InvalidOperationException(
                    $"{RenderRootEnvironmentVariable} must identify the prepared " +
                    "Reachy Unity render directory.");
            }

            string fullPath = Path.GetFullPath(configured);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(
                    $"Prepared Reachy Unity render directory does not exist: {fullPath}");
            }
            return fullPath;
        }

        private static RenderManifest ReadManifest(string renderRoot)
        {
            string manifestPath = Path.Combine(renderRoot, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException(
                    "Prepared Reachy Unity render manifest is missing.",
                    manifestPath);
            }

            string json = File.ReadAllText(manifestPath);
            RenderManifest manifest = JsonUtility.FromJson<RenderManifest>(json);
            if (manifest == null)
            {
                throw new InvalidDataException(
                    "Prepared Reachy Unity render manifest could not be parsed.");
            }
            return manifest;
        }

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

        private static void ReplaceGeneratedRoot()
        {
            if (AssetDatabase.IsValidFolder(GeneratedRoot) &&
                !AssetDatabase.DeleteAsset(GeneratedRoot))
            {
                throw new IOException(
                    $"Unity could not delete previous generated assets: {GeneratedRoot}");
            }

            Directory.CreateDirectory(Path.Combine(GeneratedRoot, "Resources"));
            Directory.CreateDirectory(MeshAssetDirectory);
            Directory.CreateDirectory(MaterialAssetDirectory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Dictionary<string, Material> CreateMaterials(
            MaterialEntry[] entries)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Unity Standard shader is unavailable for Reachy materials.");
            }

            Dictionary<string, Material> result =
                new Dictionary<string, Material>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                MaterialEntry entry = entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) ||
                    entry.rgba == null || entry.rgba.Length != 4 ||
                    !entry.rgba.All(IsFinite))
                {
                    throw new InvalidDataException(
                        $"Unity render material {index} is malformed.");
                }
                if (result.ContainsKey(entry.name))
                {
                    throw new InvalidDataException(
                        $"Duplicate Unity render material: {entry.name}");
                }

                Material material = new Material(shader)
                {
                    name = entry.name,
                    color = new Color(
                        (float)entry.rgba[0],
                        (float)entry.rgba[1],
                        (float)entry.rgba[2],
                        (float)entry.rgba[3]),
                };
                if (material.color.a < 0.999f)
                {
                    ConfigureTransparentMaterial(material);
                }

                string path =
                    $"{MaterialAssetDirectory}/Material_{index:D3}.mat";
                AssetDatabase.CreateAsset(material, path);
                result.Add(entry.name, material);
            }
            return result;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Dictionary<string, Mesh> CreateMeshes(
            string renderRoot,
            MeshEntry[] entries)
        {
            Dictionary<string, Mesh> result =
                new Dictionary<string, Mesh>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                MeshEntry entry = entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) ||
                    string.IsNullOrWhiteSpace(entry.output_path) ||
                    !IsSha256(entry.output_sha256) || entry.triangle_count <= 0)
                {
                    throw new InvalidDataException(
                        $"Unity render mesh {index} is malformed.");
                }
                if (result.ContainsKey(entry.name))
                {
                    throw new InvalidDataException(
                        $"Duplicate Unity render mesh: {entry.name}");
                }

                string sourcePath = ResolveContainedPath(
                    renderRoot,
                    entry.output_path);
                string actualHash = ComputeSha256(sourcePath);
                if (!string.Equals(
                        actualHash,
                        entry.output_sha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Converted mesh hash mismatch: {entry.output_path}");
                }

                Mesh mesh = ParseGeneratedObj(
                    sourcePath,
                    entry.name,
                    entry.triangle_count);
                string assetPath = $"{MeshAssetDirectory}/Mesh_{index:D3}.asset";
                AssetDatabase.CreateAsset(mesh, assetPath);
                result.Add(entry.name, mesh);
            }
            return result;
        }

        private static string ResolveContainedPath(
            string root,
            string relativePath)
        {
            if (Path.IsPathRooted(relativePath) ||
                relativePath.Split('/', '\\').Any(part => part == ".."))
            {
                throw new InvalidDataException(
                    $"Unsafe generated mesh path: {relativePath}");
            }

            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(
                Path.Combine(fullRoot, relativePath));
            if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal) ||
                !File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Generated mesh path is missing or escapes its root.",
                    fullPath);
            }
            return fullPath;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = algorithm.ComputeHash(stream);
                return BitConverter.ToString(digest)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static Mesh ParseGeneratedObj(
            string path,
            string meshName,
            int expectedTriangleCount)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<ObjFace> faces = new List<ObjFace>();
            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(path))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' ||
                    line.StartsWith("o ", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    vertices.Add(ParseVector3(line.Substring(2), path, lineNumber));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    normals.Add(ParseVector3(line.Substring(3), path, lineNumber));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    faces.Add(ParseFace(line.Substring(2), path, lineNumber));
                }
                else
                {
                    throw new InvalidDataException(
                        $"Unsupported generated OBJ syntax at {path}:{lineNumber}: {line}");
                }
            }

            if (faces.Count != expectedTriangleCount ||
                vertices.Count != expectedTriangleCount * 3 ||
                normals.Count != expectedTriangleCount)
            {
                throw new InvalidDataException(
                    $"Generated OBJ counts do not match manifest for {meshName}.");
            }

            Vector3[] vertexArray = vertices.ToArray();
            Vector3[] normalArray = new Vector3[vertexArray.Length];
            int[] triangles = new int[faces.Count * 3];
            for (int index = 0; index < faces.Count; index++)
            {
                ObjFace face = faces[index];
                ValidateObjIndex(face.VertexA, vertexArray.Length, path);
                ValidateObjIndex(face.VertexB, vertexArray.Length, path);
                ValidateObjIndex(face.VertexC, vertexArray.Length, path);
                ValidateObjIndex(face.Normal, normals.Count, path);
                int a = face.VertexA - 1;
                int b = face.VertexB - 1;
                int c = face.VertexC - 1;
                Vector3 normal = normals[face.Normal - 1];
                normalArray[a] = normal;
                normalArray[b] = normal;
                normalArray[c] = normal;
                triangles[index * 3] = a;
                triangles[index * 3 + 1] = b;
                triangles[index * 3 + 2] = c;
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertexArray.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.vertices = vertexArray;
            mesh.normals = normalArray;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Vector3 ParseVector3(
            string value,
            string path,
            int lineNumber)
        {
            string[] fields = value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3)
            {
                throw new InvalidDataException(
                    $"Generated OBJ vector must contain three values at " +
                    $"{path}:{lineNumber}.");
            }
            return new Vector3(
                ParseFiniteSingle(fields[0], path, lineNumber),
                ParseFiniteSingle(fields[1], path, lineNumber),
                ParseFiniteSingle(fields[2], path, lineNumber));
        }

        private static float ParseFiniteSingle(
            string value,
            string path,
            int lineNumber)
        {
            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float result) ||
                float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new InvalidDataException(
                    $"Generated OBJ contains a non-finite number at " +
                    $"{path}:{lineNumber}.");
            }
            return result;
        }

        private static ObjFace ParseFace(
            string value,
            string path,
            int lineNumber)
        {
            string[] fields = value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3)
            {
                throw new InvalidDataException(
                    $"Generated OBJ face must contain three vertices at " +
                    $"{path}:{lineNumber}.");
            }

            int[] vertices = new int[3];
            int normal = -1;
            for (int index = 0; index < fields.Length; index++)
            {
                string[] indices = fields[index].Split(
                    new[] { "//" },
                    StringSplitOptions.None);
                if (indices.Length != 2 ||
                    !int.TryParse(
                        indices[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out vertices[index]) ||
                    !int.TryParse(
                        indices[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int currentNormal))
                {
                    throw new InvalidDataException(
                        $"Generated OBJ face index is invalid at " +
                        $"{path}:{lineNumber}.");
                }
                if (normal < 0)
                {
                    normal = currentNormal;
                }
                else if (normal != currentNormal)
                {
                    throw new InvalidDataException(
                        $"Generated OBJ face uses inconsistent normals at " +
                        $"{path}:{lineNumber}.");
                }
            }
            return new ObjFace(
                vertices[0],
                vertices[1],
                vertices[2],
                normal);
        }

        private static void ValidateObjIndex(
            int value,
            int maximum,
            string path)
        {
            if (value <= 0 || value > maximum)
            {
                throw new InvalidDataException(
                    $"Generated OBJ index {value} is outside 1..{maximum}: {path}");
            }
        }

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

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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

        private static void CreatePresentationScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    "Generated Reachy prefab was not imported.",
                    PrefabPath);
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Unity could not instantiate the generated Reachy prefab.");
            }
            instance.name = "ReachyMini";

            GameObject cameraObject = new GameObject(
                "FixedFrontThreeQuarterCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            ReachyPresentationCamera cameraMetadata =
                cameraObject.AddComponent<ReachyPresentationCamera>();
            cameraMetadata.ConfigureFixedPresentationCamera();
            cameraObject.tag = "MainCamera";
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.065f, 0.08f, 1f);
            camera.transform.position = new Vector3(0.62f, 0.36f, -0.62f);
            camera.transform.LookAt(new Vector3(0f, 0.16f, 0f));

            GameObject lightObject = new GameObject("PresentationKeyLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.4f, 1f);

            if (!EditorSceneManager.SaveScene(scene, ScenePath, true))
            {
                throw new IOException(
                    $"Unity could not save generated presentation scene: {ScenePath}");
            }
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }

        private readonly struct ObjFace
        {
            public ObjFace(
                int vertexA,
                int vertexB,
                int vertexC,
                int normal)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                VertexC = vertexC;
                Normal = normal;
            }

            public int VertexA { get; }

            public int VertexB { get; }

            public int VertexC { get; }

            public int Normal { get; }
        }

        [Serializable]
        private sealed class RenderManifest
        {
            public int schema_version;
            public SourceEntry source = new SourceEntry();
            public MeshEntry[] meshes = Array.Empty<MeshEntry>();
            public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
            public BodyEntry[] bodies = Array.Empty<BodyEntry>();
            public VisualGeomEntry[] visual_geoms = Array.Empty<VisualGeomEntry>();
            public SourceCameraEntry[] source_cameras =
                Array.Empty<SourceCameraEntry>();
            public PresentationEntry presentation = new PresentationEntry();
        }

        [Serializable]
        private sealed class SourceEntry
        {
            public string model_sha256 = string.Empty;
        }

        [Serializable]
        private sealed class MeshEntry
        {
            public string name = string.Empty;
            public string source_path = string.Empty;
            public string source_sha256 = string.Empty;
            public double[] source_scale = Array.Empty<double>();
            public bool scale_baked_into_vertices;
            public string output_path = string.Empty;
            public string output_sha256 = string.Empty;
            public int triangle_count;
        }

        [Serializable]
        private sealed class MaterialEntry
        {
            public string name = string.Empty;
            public double[] rgba = Array.Empty<double>();
        }

        [Serializable]
        private sealed class BodyEntry
        {
            public int index;
            public string name = string.Empty;
            public string path = string.Empty;
            public string parent_path = string.Empty;
            public PoseEntry local_pose_unity = new PoseEntry();
        }

        [Serializable]
        private sealed class VisualGeomEntry
        {
            public int index;
            public string path = string.Empty;
            public string body_path = string.Empty;
            public string mesh = string.Empty;
            public string mesh_output_path = string.Empty;
            public string material = string.Empty;
            public PoseEntry local_pose_unity = new PoseEntry();
        }

        [Serializable]
        private sealed class PoseEntry
        {
            public double[] position_metres = Array.Empty<double>();
            public double[] quaternion_wxyz = Array.Empty<double>();
        }

        [Serializable]
        private sealed class SourceCameraEntry
        {
            public string name = string.Empty;
            public bool included_in_presentation;
        }

        [Serializable]
        private sealed class PresentationEntry
        {
            public bool source_cameras_included;
            public string authoritative_transform_source = string.Empty;
        }
    }
}
