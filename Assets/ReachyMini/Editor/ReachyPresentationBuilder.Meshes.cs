using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
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
    }
}
