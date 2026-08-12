#nullable enable

using System.Linq;
using System.Text.Json;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static readonly string[] RequiredManifestSections =
        {
            "schema_version",
            "identity",
            "runtime",
            "limits",
            "distribution",
            "capabilities",
            "artifacts",
        };

        private static readonly string[] RequiredArtifactFields =
        {
            "relative_path",
            "sha256",
            "size_bytes",
        };

        private static void SchemaDeclaresAllRequiredManifestSections()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement root = schema.RootElement;
            False(root.GetProperty("additionalProperties").GetBoolean(), "root closed schema");
            string[] required = root.GetProperty("required")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray();
            SetEqual(
                RequiredManifestSections,
                required,
                "schema sections");
            Equal(
                1,
                root.GetProperty("properties")
                    .GetProperty("schema_version")
                    .GetProperty("const")
                    .GetInt32(),
                "schema const");
        }

        private static void SchemaForbidsNetworkAndFirstReleaseRequirement()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement properties = schema.RootElement.GetProperty("properties");
            False(
                properties.GetProperty("runtime")
                    .GetProperty("properties")
                    .GetProperty("requires_network_access")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema runtime network");
            JsonElement distribution = properties.GetProperty("distribution")
                .GetProperty("properties");
            False(
                distribution.GetProperty("required_for_first_release")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema first release");
            False(
                distribution.GetProperty("automatic_download_allowed")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema automatic download");
        }

        private static void SchemaRequiresIntegrityMetadataAndSafePaths()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement artifacts = schema.RootElement.GetProperty("properties")
                .GetProperty("artifacts");
            Equal(1, artifacts.GetProperty("minItems").GetInt32(), "schema min artifacts");
            Equal(64, artifacts.GetProperty("maxItems").GetInt32(), "schema max artifacts");
            JsonElement item = artifacts.GetProperty("items");
            string[] required = item.GetProperty("required")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray();
            SetEqual(
                RequiredArtifactFields,
                required,
                "artifact fields");
            Equal(
                "^[0-9a-f]{64}$",
                item.GetProperty("properties")
                    .GetProperty("sha256")
                    .GetProperty("pattern")
                    .GetString() ?? string.Empty,
                "schema hash pattern");
            Contains(
                "\\.{1,2}",
                item.GetProperty("properties")
                    .GetProperty("relative_path")
                    .GetProperty("pattern")
                    .GetString() ?? string.Empty,
                "schema traversal pattern");
        }
    }
}
