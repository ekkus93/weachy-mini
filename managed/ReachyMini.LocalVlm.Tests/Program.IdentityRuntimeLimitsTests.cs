#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void IdentityRejectsUnsafeIdentifiers()
        {
            Throws<ArgumentException>(
                () => Identity(manifestId: "Bad Id"),
                "unsafe manifest id");
            Throws<ArgumentException>(
                () => Identity(manifestId: "-leading"),
                "leading punctuation");
            Throws<ArgumentException>(
                () => Identity(modelId: "org/model"),
                "slash in model id");
        }

        private static void IdentityRequiresHttpsProvenance()
        {
            Throws<ArgumentException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    "Model",
                    "1",
                    new Uri("http://example.com/model", UriKind.Absolute),
                    "revision",
                    "Apache-2.0"),
                "http provenance");
            Throws<ArgumentException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    "Model",
                    "1",
                    new Uri("model", UriKind.Relative),
                    "revision",
                    "Apache-2.0"),
                "relative provenance");
        }


        private static void IdentityRejectsOverlongMetadata()
        {
            Throws<ArgumentOutOfRangeException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    new string('x', 129),
                    "1",
                    new Uri("https://example.com/model", UriKind.Absolute),
                    "revision",
                    "Apache-2.0"),
                "overlong display name");
        }

        private static void RuntimeRejectsNetworkDependence()
        {
            Throws<ArgumentException>(
                () => Runtime(requiresNetworkAccess: true),
                "network runtime");
        }

        private static void RuntimeRejectsZeroParameters()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Runtime(parameterCount: 0L),
                "zero parameters");
        }

        private static void LimitsRejectInvalidTokenRelationships()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Limits(contextWindowTokens: 0),
                "zero context");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(contextWindowTokens: 128, maximumOutputTokens: 129),
                "output exceeds context");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(maximumImageWidth: 0),
                "zero image width");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(minimumRamBytes: 0L),
                "zero ram");
        }
    }
}
