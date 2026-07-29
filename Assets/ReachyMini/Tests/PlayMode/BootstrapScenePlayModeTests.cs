using System.Collections;
using NUnit.Framework;
using ReachyMini.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ReachyMini.Tests
{
    public sealed class BootstrapScenePlayModeTests
    {
        private const string BootstrapScenePath =
            "Assets/ReachyMini/Scenes/Bootstrap.unity";

        [UnityTest]
        public IEnumerator BootstrapSceneLoadsWithoutUnityPhysicsFallback()
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
                BootstrapScenePath,
                LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null);

            yield return loadOperation;
            yield return null;

            Scene activeScene = SceneManager.GetActiveScene();
            Assert.That(activeScene.path, Is.EqualTo(BootstrapScenePath));
            Assert.That(
                ProjectMetadata.InitialFidelity,
                Is.EqualTo(SimulationFidelity.Unavailable));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Rigidbody>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Joint>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<ArticulationBody>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None),
                Is.Empty);
        }
    }
}
