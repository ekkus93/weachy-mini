using System;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ReachyPresentationRoot : MonoBehaviour
    {
        [SerializeField]
        private int schemaVersion;

        [SerializeField]
        private string sourceModelSha256 = string.Empty;

        [SerializeField]
        private int bodyCount;

        [SerializeField]
        private int visualGeometryCount;

        public int SchemaVersion => schemaVersion;

        public string SourceModelSha256 => sourceModelSha256;

        public int BodyCount => bodyCount;

        public int VisualGeometryCount => visualGeometryCount;

        public void ConfigureGeneratedPresentation(
            int generatedSchemaVersion,
            string generatedSourceModelSha256,
            int generatedBodyCount,
            int generatedVisualGeometryCount)
        {
            if (generatedSchemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedSchemaVersion),
                    generatedSchemaVersion,
                    "Presentation schema version must be positive.");
            }
            if (string.IsNullOrWhiteSpace(generatedSourceModelSha256))
            {
                throw new ArgumentException(
                    "Source model SHA-256 must be present.",
                    nameof(generatedSourceModelSha256));
            }
            if (generatedBodyCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedBodyCount),
                    generatedBodyCount,
                    "Presentation must contain at least one body.");
            }
            if (generatedVisualGeometryCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedVisualGeometryCount),
                    generatedVisualGeometryCount,
                    "Presentation must contain at least one visual geometry.");
            }
            if (generatedBodyCount == 18)
            {
                ReachyPinnedJointDebugMap.ValidateSourceModel(
                    generatedSourceModelSha256);
            }

            schemaVersion = generatedSchemaVersion;
            sourceModelSha256 = generatedSourceModelSha256;
            bodyCount = generatedBodyCount;
            visualGeometryCount = generatedVisualGeometryCount;
            TryConfigureAuthoritativeRenderer();
        }

        internal void TryConfigureAuthoritativeRenderer()
        {
            if (bodyCount <= 0)
            {
                return;
            }

            ReachyPresentationBody[] discoveredBodies =
                GetComponentsInChildren<ReachyPresentationBody>(true);
            if (discoveredBodies.Length < bodyCount)
            {
                return;
            }
            if (discoveredBodies.Length > bodyCount)
            {
                throw new InvalidOperationException(
                    $"Generated presentation contains {discoveredBodies.Length} " +
                    $"body mappings, expected {bodyCount}.");
            }

            ReachyPresentationBody[] canonicalBodies =
                new ReachyPresentationBody[bodyCount];
            for (int index = 0; index < discoveredBodies.Length; ++index)
            {
                ReachyPresentationBody body = discoveredBodies[index];
                if (body.BodyIndex < 0 || body.BodyIndex >= bodyCount)
                {
                    throw new InvalidOperationException(
                        $"Generated presentation body index {body.BodyIndex} " +
                        $"is outside 0..{bodyCount - 1}.");
                }
                if (canonicalBodies[body.BodyIndex] != null)
                {
                    throw new InvalidOperationException(
                        $"Generated presentation contains duplicate body index " +
                        $"{body.BodyIndex}.");
                }
                canonicalBodies[body.BodyIndex] = body;
            }

            for (int index = 0; index < canonicalBodies.Length; ++index)
            {
                if (canonicalBodies[index] == null)
                {
                    throw new InvalidOperationException(
                        $"Generated presentation is missing body index {index}.");
                }
            }

            ReachyAuthoritativeRenderer renderer =
                GetComponent<ReachyAuthoritativeRenderer>();
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<ReachyAuthoritativeRenderer>();
            }
            renderer.ConfigureBodies(canonicalBodies);
            if (!renderer.ValidateAuthoritativeStructure())
            {
                throw new InvalidOperationException(
                    $"Generated authoritative renderer is invalid: " +
                    $"{renderer.Fault}");
            }
            renderer.enabled = false;

            ReachyAuthoritativeDebugOverlay debugOverlay =
                GetComponent<ReachyAuthoritativeDebugOverlay>();
            if (debugOverlay == null)
            {
                debugOverlay =
                    gameObject.AddComponent<ReachyAuthoritativeDebugOverlay>();
            }
            debugOverlay.ConfigureBodies(canonicalBodies);
            debugOverlay.enabled = false;
        }
    }
}
