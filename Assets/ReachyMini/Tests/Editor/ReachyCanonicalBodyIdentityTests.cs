#nullable enable

using System.Linq;
using NUnit.Framework;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Tests
{
    public sealed class ReachyCanonicalBodyIdentityTests
    {
        private const string PrefabPath =
            "Assets/Generated/ReachyMini/UnityPresentation/Resources/" +
            "ReachyMiniPresentation.prefab";

        [Test]
        public void UnnamedBodyUsesStableCanonicalIndexIdentity()
        {
            Assert.That(
                ReachyPresentationBody.ResolveCanonicalBodyName(15, string.Empty),
                Is.EqualTo("__body_15"));
            Assert.That(
                ReachyPresentationBody.ResolveCanonicalBodyName(15, "   "),
                Is.EqualTo("__body_15"));
            Assert.That(
                ReachyPresentationBody.ResolveCanonicalBodyName(2, "xl_330"),
                Is.EqualTo("xl_330"));
        }

        [Test]
        public void GeneratedPrefabHasCompleteUniqueCanonicalBodyNames()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ReachyPresentationBody[] bodies = contents
                    .GetComponentsInChildren<ReachyPresentationBody>(true)
                    .OrderBy(body => body.BodyIndex)
                    .ToArray();

                Assert.That(bodies, Has.Length.EqualTo(18));
                Assert.That(
                    bodies.All(body => !string.IsNullOrWhiteSpace(body.BodyName)),
                    Is.True);
                Assert.That(
                    bodies.Select(body => body.BodyName).Distinct().Count(),
                    Is.EqualTo(bodies.Length));
                Assert.That(bodies[15].BodyIndex, Is.EqualTo(15));
                Assert.That(bodies[15].BodyName, Is.EqualTo("__body_15"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
