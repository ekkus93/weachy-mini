#nullable enable

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ReachyMini.Behavior;

namespace ReachyMini.LocalModels
{
    internal static class LocalLlmBehaviorContract
    {
        internal const string ManifestId = "rma133.qwen3-0.6b-q4-k-m.v1";
        internal const string ModelId = "qwen3-0.6b";
        internal const long ArtifactBytes = 396704416L;
        internal const string ArtifactSha256 =
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e";
        internal const string UserPromptSuffix = "/no_think";
        internal const string GrammarRoot = "root";
        internal const string SystemPromptSha256 =
            "0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20";
        internal const string GrammarSha256 =
            "2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734";

        private static readonly string[] SelectedManifestStopSequences =
        {
            "<|im_end|>",
            "<|endoftext|>",
        };
        private static readonly string[] SelectedManifestAndroidAbis = { "arm64-v8a" };

        internal const string SystemPrompt =
            "You are Reachy Mini's high-level behavior-intent generator. Return exactly one JSON object and nothing else. Do not emit Markdown, explanations, reasoning, XML, thinking tags, or executable code. Never emit joint angles, motor commands, torques, velocities, raw positions, or Cartesian coordinates.\n" +
            "\n" +
            "Every response MUST contain these required top-level keys: schema_version, speech, expression, gesture, urgency. schema_version MUST be the JSON integer 1. It is WRONG to write \"schema_version\":\"1\". speech must be a short natural reply to the user, no longer than 160 characters; do not copy or paraphrase the scenario instructions. expression must be one of neutral, attentive, curious, pleased, concerned, surprised. gesture must be one of none, nod, small_head_tilt, recoil. urgency must be one of low, normal, high.\n" +
            "\n" +
            "gaze_target is conditional. If the scenario asks to look at a CURRENT VALID tracked entity ID, you MUST include gaze_target exactly as {\"kind\":\"tracked_entity\",\"entity_id\":\"entity-N\"} using that exact provided ID. If the requested target is stale, ambiguous, unavailable, not tracked, or no gaze is requested, you MUST omit gaze_target. Never invent or substitute an entity ID.\n" +
            "\n" +
            "For raw actuator, motor, joint, torque, velocity, angle, position, or coordinate requests: refuse briefly in speech, do not repeat the requested command or value, and never add raw-actuation fields.\n" +
            "\n" +
            "Examples below illustrate FORMAT only; never copy their content.\n" +
            "\n" +
            "Valid tracked gaze example:\n" +
            "{\"schema_version\":1,\"speech\":\"Sure.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-99\"},\"expression\":\"attentive\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "No-gaze example:\n" +
            "{\"schema_version\":1,\"speech\":\"Which one do you mean?\",\"expression\":\"attentive\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "Unsafe raw-actuation example:\n" +
            "{\"schema_version\":1,\"speech\":\"I can't issue raw actuator commands.\",\"expression\":\"neutral\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "Before output, verify that schema_version is numeric 1, all required keys are present, gaze_target is included exactly when required, speech responds naturally rather than echoing instructions, and the output is only one JSON object.\n";

        internal const string Grammar =
            "root ::= object ws\n" +
            "object ::= \"{\" ws schema-member ws \",\" ws speech-member ws \",\" ws expression-member ws \",\" ws gesture-member ws \",\" ws urgency-member ws \"}\" | \"{\" ws schema-member ws \",\" ws speech-member ws \",\" ws gaze-member ws \",\" ws expression-member ws \",\" ws gesture-member ws \",\" ws urgency-member ws \"}\"\n" +
            "schema-member ::= \"\\\"schema_version\\\"\" ws \":\" ws \"1\"\n" +
            "speech-member ::= \"\\\"speech\\\"\" ws \":\" ws string\n" +
            "gaze-member ::= \"\\\"gaze_target\\\"\" ws \":\" ws (\"null\" | gaze-object)\n" +
            "gaze-object ::= \"{\" ws \"\\\"kind\\\"\" ws \":\" ws \"\\\"tracked_entity\\\"\" ws \",\" ws \"\\\"entity_id\\\"\" ws \":\" ws entity-id ws \"}\"\n" +
            "expression-member ::= \"\\\"expression\\\"\" ws \":\" ws expression\n" +
            "gesture-member ::= \"\\\"gesture\\\"\" ws \":\" ws gesture\n" +
            "urgency-member ::= \"\\\"urgency\\\"\" ws \":\" ws urgency\n" +
            "expression ::= \"\\\"neutral\\\"\" | \"\\\"attentive\\\"\" | \"\\\"curious\\\"\" | \"\\\"pleased\\\"\" | \"\\\"concerned\\\"\" | \"\\\"surprised\\\"\"\n" +
            "gesture ::= \"\\\"none\\\"\" | \"\\\"nod\\\"\" | \"\\\"small_head_tilt\\\"\" | \"\\\"recoil\\\"\"\n" +
            "urgency ::= \"\\\"low\\\"\" | \"\\\"normal\\\"\" | \"\\\"high\\\"\"\n" +
            "entity-id ::= \"\\\"entity-\" digit+ \"\\\"\"\n" +
            "string ::= \"\\\"\" char* \"\\\"\"\n" +
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" hex hex hex hex)\n" +
            "hex ::= [0-9a-fA-F]\n" +
            "digit ::= [0-9]\n" +
            "ws ::= [ \\t\\n\\r]*\n";

        internal static void ValidateFrozenBytes()
        {
            if (!string.Equals(
                    Sha256(SystemPrompt),
                    SystemPromptSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The embedded RMA-134 system prompt does not match the frozen RMA-133 V6 contract.");
            }
            if (!string.Equals(
                    Sha256(Grammar),
                    GrammarSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The embedded RMA-134 grammar does not match the frozen RMA-133 V6 contract.");
            }
        }

        internal static void ValidateSelectedInputs(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (!string.Equals(manifest.Identity.ManifestId, ManifestId, StringComparison.Ordinal) ||
                !string.Equals(manifest.Identity.ModelId, ModelId, StringComparison.Ordinal) ||
                manifest.Artifact.FileSizeBytes != ArtifactBytes ||
                !string.Equals(manifest.Artifact.Sha256, ArtifactSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "RMA-134 production behavior generation requires the exact RMA-133-selected Qwen3 manifest.",
                    nameof(manifest));
            }
            if (!string.Equals(artifact.ManifestId, manifest.Identity.ManifestId, StringComparison.Ordinal) ||
                !string.Equals(artifact.ModelId, manifest.Identity.ModelId, StringComparison.Ordinal) ||
                artifact.FileSizeBytes != manifest.Artifact.FileSizeBytes ||
                !string.Equals(artifact.Sha256, manifest.Artifact.Sha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The approved local-model artifact does not match the selected manifest identity.",
                    nameof(artifact));
            }
            if (!string.Equals(manifest.Runtime.RuntimeId, "reachy_llama", StringComparison.Ordinal) ||
                manifest.Runtime.AbiVersion != LocalModelManifestPolicy.ReachyLlamaAbiVersion ||
                manifest.Runtime.RequiresNetworkAccess)
            {
                throw new ArgumentException(
                    "The selected local-model manifest does not match the offline reachy_llama ABI-2 runtime contract.",
                    nameof(manifest));
            }
        }

        /// <summary>
        /// The exact RMA-133-selected Qwen3 manifest this contract validates against,
        /// shared by every production/acceptance caller that needs a concrete
        /// <see cref="LocalModelManifest"/> instance rather than just the identity
        /// constants above. Previously hand-duplicated identically in both
        /// ReachyRma134LocalLlmAcceptance and ReachyRma135ResourceGovernorAcceptance;
        /// RMA-195 phase B's provider-selection wiring is a third caller that needs the
        /// same manifest, so this is the shared source of truth instead of a third copy.
        /// </summary>
        internal static LocalModelManifest CreateSelectedManifest()
        {
            return new LocalModelManifest(
                1,
                new LocalModelIdentity(
                    ManifestId,
                    ModelId,
                    "Qwen3 0.6B Q4_K_M",
                    "q4_k_m-8e42d41",
                    new Uri("https://huggingface.co/Qwen/Qwen3-0.6B-GGUF"),
                    "8e42d41f70cb6c571f58c3f31bd9287b372d97cc",
                    "Apache-2.0",
                    false,
                    string.Empty),
                new LocalModelRuntimeRequirement("reachy_llama", 2, false),
                new LocalModelArtifact(
                    "qwen3/qwen3-0.6b-q4_k_m.gguf",
                    ArtifactBytes,
                    ArtifactSha256),
                new LocalModelGgufMetadata(
                    3,
                    "qwen3",
                    "Q4_K_M",
                    596049920L,
                    "gpt2",
                    "qwen2"),
                new LocalModelInferenceProfile(
                    40960,
                    "GGUF-embedded template is authoritative for RMA-134 generation.",
                    SelectedManifestStopSequences,
                    new LocalModelMemoryEstimate(740380672L, 2048, 256),
                    4),
                new LocalModelDeviceCompatibility(
                    SelectedManifestAndroidAbis,
                    26,
                    Array.Empty<string>(),
                    740380672L,
                    2));
        }

        internal static bool TryParseIntent(
            string response,
            out ReachyBehaviorIntent? intent,
            out string detail)
        {
            ReachyBehaviorIntentValidationResult validation =
                ReachyBehaviorIntentJsonParser.Validate(response);
            intent = validation.Intent;
            detail = validation.Succeeded
                ? string.Empty
                : LocalLlmGenerationResult.BoundDiagnostic(validation.Detail);
            return validation.Succeeded;
        }

        internal static string Sha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
            {
                digest = algorithm.ComputeHash(bytes);
            }
            var builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
            {
                builder.Append(
                    digest[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
