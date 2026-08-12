using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
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
    }
}
