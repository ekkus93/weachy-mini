using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReachyMini.Tests
{
    public sealed class ReachyPresentationAssetTests
    {
        private const string PrefabPath =
            "Assets/Generated/ReachyMini/UnityPresentation/Resources/" +
            "ReachyMiniPresentation.prefab";
        private const string ScenePath =
            "Assets/Generated/ReachyMini/UnityPresentation/" +
            "ReachyMiniPresentation.unity";
        private const string ExpectedModelSha256 =
            "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46";

        [Test]
        public void GeneratedPrefabMatchesPinnedRenderContract()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Generated prefab is missing: {PrefabPath}");

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationRoot root =
                    contents.GetComponent<ReachyPresentationRoot>();
                Assert.That(root, Is.Not.Null);
                Assert.That(root.SchemaVersion, Is.EqualTo(1));
                Assert.That(root.SourceModelSha256, Is.EqualTo(ExpectedModelSha256));
                Assert.That(root.BodyCount, Is.EqualTo(18));
                Assert.That(root.VisualGeometryCount, Is.EqualTo(161));

                ReachyPresentationBody[] bodies =
                    contents.GetComponentsInChildren<ReachyPresentationBody>(true);
                Assert.That(bodies, Has.Length.EqualTo(root.BodyCount));
                Assert.That(
                    bodies.Select(body => body.BodyIndex),
                    Is.EquivalentTo(Enumerable.Range(0, root.BodyCount)));
                Assert.That(
                    bodies.Select(body => body.BodyPath)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    Is.EqualTo(root.BodyCount));
                Assert.That(
                    bodies.All(body => !string.IsNullOrWhiteSpace(body.BodyName)),
                    Is.True);
                Assert.That(
                    bodies.Select(body => body.BodyName)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    Is.EqualTo(root.BodyCount));
                Assert.That(
                    bodies.Count(body => !body.BodyName.StartsWith(
                        "__body_",
                        StringComparison.Ordinal)),
                    Is.EqualTo(17));
                Assert.That(
                    bodies.Single(body => body.BodyIndex == 15).BodyName,
                    Is.EqualTo("__body_15"));
                Assert.That(
                    bodies.Any(body => string.Equals(
                        body.BodyName,
                        "xl_330",
                        StringComparison.Ordinal)),
                    Is.True);

                Dictionary<string, ReachyPresentationBody> bodiesByPath =
                    bodies.ToDictionary(
                        body => body.BodyPath,
                        body => body,
                        StringComparer.Ordinal);
                foreach (ReachyPresentationBody body in bodies)
                {
                    int separator = body.BodyPath.LastIndexOf('/');
                    Assert.That(separator, Is.GreaterThan(0));
                    string parentPath = body.BodyPath.Substring(0, separator);
                    Transform expectedParent = string.Equals(
                        parentPath,
                        "/world",
                        StringComparison.Ordinal)
                        ? contents.transform
                        : bodiesByPath[parentPath].transform;
                    Assert.That(body.transform.parent, Is.SameAs(expectedParent));
                    Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
                }

                ReachyAuthoritativeRenderer authoritativeRenderer =
                    contents.GetComponent<ReachyAuthoritativeRenderer>();
                Assert.That(authoritativeRenderer, Is.Not.Null);
                Assert.That(
                    authoritativeRenderer.AuthoritativeBodyCount,
                    Is.EqualTo(root.BodyCount));
                Assert.That(
                    authoritativeRenderer.Status,
                    Is.EqualTo(ReachyAuthoritativeRendererStatus.Unbound));
                Assert.That(authoritativeRenderer.enabled, Is.False);
                Assert.That(
                    authoritativeRenderer.ValidateAuthoritativeStructure(),
                    Is.True);

                ReachyPresentationDebugOverlay[] overlays =
                    contents.GetComponents<ReachyPresentationDebugOverlay>();
                Assert.That(overlays, Has.Length.EqualTo(1));
                Assert.That(overlays[0].BodyCount, Is.EqualTo(root.BodyCount));
                Assert.That(overlays[0].JointCount, Is.EqualTo(16));
                Assert.That(overlays[0].IsVisible, Is.False);

                MeshRenderer[] renderers =
                    contents.GetComponentsInChildren<MeshRenderer>(true);
                MeshFilter[] filters =
                    contents.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(renderers, Has.Length.EqualTo(root.VisualGeometryCount));
                Assert.That(filters, Has.Length.EqualTo(root.VisualGeometryCount));
                Assert.That(
                    renderers.All(renderer => renderer.sharedMaterial != null),
                    Is.True);
                Assert.That(
                    filters.All(filter => filter.sharedMesh != null),
                    Is.True);
                Assert.That(
                    filters.Select(filter => filter.sharedMesh).Distinct().Count(),
                    Is.EqualTo(41));
                Assert.That(
                    renderers.Select(renderer => renderer.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(41));
                Assert.That(
                    renderers.Any(renderer => renderer.sharedMaterial.color.a < 0.999f),
                    Is.True);

                foreach (MeshFilter filter in filters)
                {
                    string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    Assert.That(
                        meshPath,
                        Does.StartWith(
                            "Assets/Generated/ReachyMini/UnityPresentation/Meshes/"));
                    Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
                    Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(1));
                    Assert.That(filter.sharedMesh.GetIndexCount(0), Is.GreaterThan(0));
                    Assert.That(filter.transform.localScale, Is.EqualTo(Vector3.one));
                    Bounds bounds = filter.sharedMesh.bounds;
                    Assert.That(IsFinite(bounds.center), Is.True);
                    Assert.That(IsFinite(bounds.extents), Is.True);
                }

                foreach (MeshRenderer renderer in renderers)
                {
                    string materialPath = AssetDatabase.GetAssetPath(
                        renderer.sharedMaterial);
                    Assert.That(
                        materialPath,
                        Does.StartWith(
                            "Assets/Generated/ReachyMini/UnityPresentation/Materials/"));
                    Color color = renderer.sharedMaterial.color;
                    Assert.That(IsFinite(color), Is.True);
                    Assert.That(color.r, Is.InRange(0f, 1f));
                    Assert.That(color.g, Is.InRange(0f, 1f));
                    Assert.That(color.b, Is.InRange(0f, 1f));
                    Assert.That(color.a, Is.InRange(0f, 1f));
                }

                Assert.That(
                    contents.GetComponentsInChildren<Camera>(true),
                    Is.Empty,
                    "MuJoCo cameras must not be embedded in the Reachy prefab.");
                Assert.That(
                    contents.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    contents.GetComponentsInChildren<Joint>(true),
                    Is.Empty);
                Assert.That(
                    contents.GetComponentsInChildren<ArticulationBody>(true),
                    Is.Empty);
                Assert.That(
                    contents.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
                Assert.That(
                    contents.GetComponentsInChildren<Transform>(true)
                        .Select(transform => transform.name),
                    Does.Not.Contain("studio_close"));
                Assert.That(
                    contents.GetComponentsInChildren<Transform>(true)
                        .Select(transform => transform.name),
                    Does.Not.Contain("eye_camera"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void PresentationSetupRunsBeforeRuntimeBinding()
        {
            DefaultExecutionOrder rootOrder = Attribute.GetCustomAttribute(
                typeof(ReachyPresentationRoot),
                typeof(DefaultExecutionOrder)) as DefaultExecutionOrder;
            Assert.That(rootOrder, Is.Not.Null);
            Assert.That(
                rootOrder.order,
                Is.LessThan(0),
                "Presentation setup must disable/configure the renderer before the " +
                "production runtime binds and enables its pose source.");
        }

        [Test]
        public void GeneratedPresentationSceneIsTheOnlyEnabledBuildScene()
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Assert.That(sceneAsset, Is.Not.Null, $"Generated scene is missing: {ScenePath}");
            Assert.That(EditorBuildSettings.scenes, Has.Length.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ScenePath));
        }

        [Test]
        public void GeneratedSceneLocksCameraLightingAndIndependence()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(SceneManager.SetActiveScene(scene), Is.True);
                GameObject[] roots = scene.GetRootGameObjects();
                ReachyPresentationRoot presentation = roots
                    .SelectMany(root => root.GetComponentsInChildren<ReachyPresentationRoot>(true))
                    .Single();
                ReachyPresentationCamera cameraMetadata = roots
                    .SelectMany(root => root.GetComponentsInChildren<ReachyPresentationCamera>(true))
                    .Single();
                Camera camera = cameraMetadata.GetComponent<Camera>();
                Assert.That(camera, Is.Not.Null);
                Assert.That(cameraMetadata.transform.parent, Is.Null);
                Assert.That(cameraMetadata.transform.IsChildOf(presentation.transform), Is.False);
                Assert.That(cameraMetadata.gameObject.tag, Is.EqualTo("MainCamera"));
                Assert.That(cameraMetadata.Framing, Is.EqualTo("fixed_front_three_quarter"));
                Assert.That(cameraMetadata.AcceptsUserNavigation, Is.False);
                Assert.That(camera.fieldOfView, Is.EqualTo(35f));
                Assert.That(camera.nearClipPlane, Is.EqualTo(0.01f));
                Assert.That(camera.farClipPlane, Is.EqualTo(20f));
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(
                    Vector3.Distance(
                        camera.transform.position,
                        new Vector3(0.62f, 0.36f, -0.62f)),
                    Is.LessThan(1e-6f));
                Vector3 targetDirection =
                    (new Vector3(0f, 0.16f, 0f) - camera.transform.position).normalized;
                Assert.That(
                    Vector3.Angle(camera.transform.forward, targetDirection),
                    Is.LessThan(1e-4f));
                Assert.That(
                    Vector4.Distance(
                        camera.backgroundColor,
                        new Color(0.055f, 0.065f, 0.08f, 1f)),
                    Is.LessThan(1e-6f));
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                        .ToArray(),
                    Has.Length.EqualTo(1));

                Light light = roots
                    .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                    .Single();
                Assert.That(light.transform.parent, Is.Null);
                Assert.That(light.transform.IsChildOf(presentation.transform), Is.False);
                Assert.That(light.type, Is.EqualTo(LightType.Directional));
                Assert.That(light.intensity, Is.EqualTo(1.15f));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(
                    Quaternion.Angle(
                        light.transform.rotation,
                        Quaternion.Euler(38f, -32f, 0f)),
                    Is.LessThan(1e-4f));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                Assert.That(
                    Vector4.Distance(
                        RenderSettings.ambientLight,
                        new Color(0.34f, 0.36f, 0.4f, 1f)),
                    Is.LessThan(1e-6f));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) &&
                IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
