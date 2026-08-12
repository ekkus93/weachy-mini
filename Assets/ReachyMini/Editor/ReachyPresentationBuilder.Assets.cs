using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
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
    }
}
