using System.Collections;
using System.Linq;
using NUnit.Framework;
using ReachyMini.Core;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ReachyMini.Tests
{
    public sealed class BootstrapScenePlayModeTests
    {
        private const string PresentationScenePath =
            "Assets/Generated/ReachyMini/UnityPresentation/" +
            "ReachyMiniPresentation.unity";

        [UnityTest]
        public IEnumerator GeneratedPresentationSceneLoadsWithoutPhysicsFallback()
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
                PresentationScenePath,
                LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null);

            yield return loadOperation;
            yield return null;
            yield return null;

            Scene activeScene = SceneManager.GetActiveScene();
            Assert.That(activeScene.path, Is.EqualTo(PresentationScenePath));
            Assert.That(
                ProjectMetadata.InitialFidelity,
                Is.EqualTo(SimulationFidelity.Unavailable));

            ReachyPresentationRoot[] roots =
                UnityEngine.Object.FindObjectsByType<ReachyPresentationRoot>(
                    FindObjectsInactive.Include);
            Assert.That(roots, Has.Length.EqualTo(1));
            ReachyPresentationRoot root = roots[0];
            Assert.That(root.BodyCount, Is.EqualTo(18));
            Assert.That(root.VisualGeometryCount, Is.GreaterThan(0));
            Assert.That(root.GetCanonicalBodies(), Has.Length.EqualTo(root.BodyCount));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<ReachyPresentationBody>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(root.BodyCount));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(root.VisualGeometryCount));

            ReachyAuthoritativeRenderer[] authoritativeRenderers =
                UnityEngine.Object.FindObjectsByType<ReachyAuthoritativeRenderer>(
                    FindObjectsInactive.Include);
            Assert.That(authoritativeRenderers, Has.Length.EqualTo(1));
            Assert.That(
                authoritativeRenderers[0].AuthoritativeBodyCount,
                Is.EqualTo(root.BodyCount));
            Assert.That(
                authoritativeRenderers[0].Status,
                Is.EqualTo(ReachyAuthoritativeRendererStatus.Unbound));
            Assert.That(authoritativeRenderers[0].enabled, Is.False);
            Assert.That(
                authoritativeRenderers[0].ValidateAuthoritativeStructure(),
                Is.True);

            ReachyProductionAuthoritativeRuntime[] runtimes =
                UnityEngine.Object.FindObjectsByType<
                    ReachyProductionAuthoritativeRuntime>(
                    FindObjectsInactive.Include);
            Assert.That(runtimes, Has.Length.EqualTo(1));
            Assert.That(
                runtimes[0].Status,
                Is.EqualTo(ReachyProductionRuntimeStatus.Unavailable));
            Assert.That(runtimes[0].Fault, Is.Empty);

            ReachyPresentationCamera[] presentationCameras =
                UnityEngine.Object.FindObjectsByType<ReachyPresentationCamera>(
                    FindObjectsInactive.Include);
            Assert.That(presentationCameras, Has.Length.EqualTo(1));
            Assert.That(
                presentationCameras[0].Framing,
                Is.EqualTo("fixed_front_three_quarter"));
            Assert.That(presentationCameras[0].AcceptsUserNavigation, Is.False);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                activeScene.GetRootGameObjects().Select(gameObject => gameObject.name),
                Does.Not.Contain("studio_close"));
            Assert.That(
                activeScene.GetRootGameObjects().Select(gameObject => gameObject.name),
                Does.Not.Contain("eye_camera"));

            Assert.That(
                UnityEngine.Object.FindObjectsByType<Rigidbody>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Joint>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<ArticulationBody>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Animator>(
                    FindObjectsInactive.Include),
                Is.Empty);
        }
    }
}
