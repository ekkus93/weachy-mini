#nullable enable

using NUnit.Framework;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyNativeLifecycleAcceptanceTests
    {
        [Test]
        public void PresentationRootSerializesAllProductionLifecycleComponents()
        {
            GameObject root = new GameObject("LifecycleComponentContract");
            try
            {
                root.AddComponent<ReachyPresentationRoot>();

                Assert.That(
                    root.GetComponent<ReachyAuthoritativeRenderer>(),
                    Is.Not.Null);
                Assert.That(
                    root.GetComponent<ReachyProductionAuthoritativeRuntime>(),
                    Is.Not.Null);
                Assert.That(
                    root.GetComponent<ReachyAuthoritativePhysicalAcceptance>(),
                    Is.Not.Null);
                Assert.That(
                    root.GetComponent<ReachyNativeLifecycleAcceptance>(),
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LifecycleAcceptanceUsesStableIntentAndEvidenceNames()
        {
            Assert.That(
                ReachyNativeLifecycleAcceptance.LaunchExtraName,
                Is.EqualTo("weachy_lifecycle_acceptance"));
            Assert.That(
                ReachyNativeLifecycleAcceptance.ResultFileName,
                Is.EqualTo("weachy-native-lifecycle-acceptance.json"));
        }
    }
}
