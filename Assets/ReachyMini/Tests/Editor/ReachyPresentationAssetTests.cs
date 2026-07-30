using System;
using System.Linq;
using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEditor;
using UnityEngine;

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
                Assert.That(root.VisualGeometryCount, Is.GreaterThan(0));

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
                    bodies.Count(body => !string.IsNullOrEmpty(body.BodyName)),
                    Is.EqualTo(17));
                Assert.That(
                    bodies.Any(body => string.Equals(
                        body.BodyName,
                        "xl_330",
                        StringComparison.Ordinal)),
                    Is.True);

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
        public void GeneratedPresentationSceneIsTheOnlyEnabledBuildScene()
        {
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Assert.That(scene, Is.Not.Null, $"Generated scene is missing: {ScenePath}");
            Assert.That(EditorBuildSettings.scenes, Has.Length.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ScenePath));
        }
    }
}
