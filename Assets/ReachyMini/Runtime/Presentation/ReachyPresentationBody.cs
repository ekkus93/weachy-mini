using System;
using UnityEngine;

namespace ReachyMini.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ReachyPresentationBody : MonoBehaviour
    {
        [SerializeField]
        private int bodyIndex = -1;

        [SerializeField]
        private string bodyPath = string.Empty;

        [SerializeField]
        private string bodyName = string.Empty;

        public int BodyIndex => bodyIndex;

        public string BodyPath => bodyPath;

        public string BodyName => bodyName;

        public static string ResolveCanonicalBodyName(
            int generatedBodyIndex,
            string generatedBodyName)
        {
            if (generatedBodyIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedBodyIndex),
                    generatedBodyIndex,
                    "Body index must be nonnegative.");
            }

            return string.IsNullOrWhiteSpace(generatedBodyName)
                ? $"__body_{generatedBodyIndex}"
                : generatedBodyName;
        }

        public void ConfigureGeneratedBody(
            int generatedBodyIndex,
            string generatedBodyPath,
            string generatedBodyName)
        {
            if (generatedBodyIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedBodyIndex),
                    generatedBodyIndex,
                    "Body index must be nonnegative.");
            }
            if (string.IsNullOrWhiteSpace(generatedBodyPath))
            {
                throw new ArgumentException(
                    "Body path must be present.",
                    nameof(generatedBodyPath));
            }

            bodyIndex = generatedBodyIndex;
            bodyPath = generatedBodyPath;
            bodyName = ResolveCanonicalBodyName(
                generatedBodyIndex,
                generatedBodyName);
            ReachyPresentationRoot root =
                GetComponentInParent<ReachyPresentationRoot>();
            if (root != null)
            {
                root.TryConfigureAuthoritativeRenderer();
            }
        }
    }
}
