using System;
using System.IO;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
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
    }
}
