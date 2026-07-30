using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Editor
{
    public static class ReachyPresentationPipeline
    {
        private const string ModelMapEnvironmentVariable =
            "REACHY_MODEL_MAP_PATH";

        public static void BuildFromCommandLine()
        {
            ReachyPresentationBuilder.BuildPresentation();
            ConfigureDebugOverlay();
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureDebugOverlay()
        {
            string modelMapPath = Environment.GetEnvironmentVariable(
                ModelMapEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(modelMapPath))
            {
                throw new InvalidOperationException(
                    $"{ModelMapEnvironmentVariable} must identify the imported " +
                    "Reachy MODEL_MAP.json file.");
            }

            string fullPath = Path.GetFullPath(modelMapPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Imported Reachy model map is missing.",
                    fullPath);
            }

            ModelMap modelMap = JsonUtility.FromJson<ModelMap>(
                File.ReadAllText(fullPath));
            ValidateModelMap(modelMap);

            GameObject contents = PrefabUtility.LoadPrefabContents(
                ReachyPresentationBuilder.PrefabPath);
            try
            {
                ReachyPresentationBody[] bodies = contents
                    .GetComponentsInChildren<ReachyPresentationBody>(true)
                    .OrderBy(body => body.BodyIndex)
                    .ToArray();
                if (bodies.Length != modelMap.counts.bodies)
                {
                    throw new InvalidDataException(
                        $"Generated prefab contains {bodies.Length} body mappings, " +
                        $"MODEL_MAP.json requires {modelMap.counts.bodies}.");
                }

                Dictionary<string, ReachyPresentationBody> bodiesByPath =
                    bodies.ToDictionary(
                        body => body.BodyPath,
                        body => body,
                        StringComparer.Ordinal);
                string[] jointNames = new string[modelMap.joints.Length];
                ReachyPresentationBody[] jointBodies =
                    new ReachyPresentationBody[modelMap.joints.Length];
                for (int index = 0; index < modelMap.joints.Length; ++index)
                {
                    JointEntry joint = modelMap.joints[index];
                    if (!bodiesByPath.TryGetValue(
                            joint.body_path,
                            out ReachyPresentationBody body))
                    {
                        throw new InvalidDataException(
                            $"Joint {joint.name} references unknown body path " +
                            $"{joint.body_path}.");
                    }
                    jointNames[index] = joint.name;
                    jointBodies[index] = body;
                }

                ReachyPresentationDebugOverlay overlay =
                    contents.GetComponent<ReachyPresentationDebugOverlay>();
                if (overlay == null)
                {
                    overlay = contents.AddComponent<ReachyPresentationDebugOverlay>();
                }
                overlay.ConfigureGeneratedOverlay(
                    bodies,
                    jointNames,
                    jointBodies);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    ReachyPresentationBuilder.PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new IOException(
                        "Unity could not save the generated Reachy debug overlay.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            VerifySavedOverlay(modelMap);
        }

        private static void ValidateModelMap(ModelMap modelMap)
        {
            if (modelMap == null || modelMap.schema_version != 1 ||
                !string.Equals(
                    modelMap.model,
                    "reachy_mini",
                    StringComparison.Ordinal) ||
                modelMap.counts == null || modelMap.counts.bodies != 18 ||
                modelMap.counts.joints != 16 || modelMap.joints == null ||
                modelMap.joints.Length != modelMap.counts.joints)
            {
                throw new InvalidDataException(
                    "Imported Reachy MODEL_MAP.json has an unexpected schema or topology.");
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < modelMap.joints.Length; ++index)
            {
                JointEntry joint = modelMap.joints[index];
                if (joint == null || joint.index != index ||
                    string.IsNullOrWhiteSpace(joint.name) ||
                    string.IsNullOrWhiteSpace(joint.body_path) ||
                    !names.Add(joint.name))
                {
                    throw new InvalidDataException(
                        $"Imported Reachy joint entry {index} is malformed.");
                }
            }
        }

        private static void VerifySavedOverlay(ModelMap modelMap)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(
                ReachyPresentationBuilder.PrefabPath);
            try
            {
                ReachyPresentationDebugOverlay overlay =
                    contents.GetComponent<ReachyPresentationDebugOverlay>();
                if (overlay == null || overlay.IsVisible ||
                    overlay.BodyCount != modelMap.counts.bodies ||
                    overlay.JointCount != modelMap.counts.joints)
                {
                    throw new InvalidDataException(
                        "Generated Reachy debug overlay did not serialize correctly.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Serializable]
        private sealed class ModelMap
        {
            public int schema_version;
            public string model = string.Empty;
            public CountEntry counts = new CountEntry();
            public JointEntry[] joints = Array.Empty<JointEntry>();
        }

        [Serializable]
        private sealed class CountEntry
        {
            public int bodies;
            public int joints;
        }

        [Serializable]
        private sealed class JointEntry
        {
            public int index;
            public string name = string.Empty;
            public string body_path = string.Empty;
        }
    }
}
