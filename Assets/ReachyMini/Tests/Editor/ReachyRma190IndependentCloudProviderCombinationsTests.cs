#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Providers;

namespace ReachyMini.Tests
{
    public sealed class ReachyRma190IndependentCloudProviderCombinationsTests
    {
        [Test]
        public void AllCloudLocalSelectionMasksRoundTripIndependently()
        {
            string root = TemporaryRoot("selection-masks");
            try
            {
                for (int mask = 0; mask < 16; ++mask)
                {
                    string caseRoot = Path.Combine(root, "mask-" + mask);
                    Directory.CreateDirectory(caseRoot);
                    string settingsPath = Path.Combine(caseRoot, "settings.json");

                    var writer =
                        new ReachySettingsPersistenceApplicationService(settingsPath);
                    writer.Initialize();
                    try
                    {
                        Configure(
                            writer.Settings,
                            ReachyProviderKind.Asr,
                            IsCloud(mask, ReachyProviderKind.Asr));
                        Configure(
                            writer.Settings,
                            ReachyProviderKind.Tts,
                            IsCloud(mask, ReachyProviderKind.Tts));
                        Configure(
                            writer.Settings,
                            ReachyProviderKind.Llm,
                            IsCloud(mask, ReachyProviderKind.Llm));
                        Configure(
                            writer.Settings,
                            ReachyProviderKind.Vlm,
                            IsCloud(mask, ReachyProviderKind.Vlm));

                        ReachySettingsStorageReferences references =
                            ReferencesForMask(mask);
                        writer.UpdateReferences(references);
                        AssertMask(writer.Settings.Current, references, mask);
                    }
                    finally
                    {
                        writer.Dispose();
                    }

                    var reader =
                        new ReachySettingsPersistenceApplicationService(settingsPath);
                    reader.Initialize();
                    try
                    {
                        AssertMask(reader.Settings.Current, reader.References, mask);
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Test]
        public void ProfilesAndFallbackPoliciesRemainIndependentAcrossReload()
        {
            string root = TemporaryRoot("provider-persistence");
            string profilesPath = Path.Combine(root, "providers.json");
            string policiesPath = Path.Combine(root, "fallback-policies.json");
            try
            {
                var profileStore =
                    new ReachyProviderProfilePersistenceStore(profilesPath);
                var fallbackStore =
                    new ReachyFallbackPolicyPersistenceStore(
                        new ReachyProviderFallbackPolicyEngine(),
                        policiesPath);
                profileStore.Initialize();
                fallbackStore.Initialize();

                ProviderDefinition[] definitions = Definitions();
                for (int index = 0; index < definitions.Length; ++index)
                {
                    ProviderDefinition definition = definitions[index];
                    profileStore.Upsert(definition.Profile);
                    fallbackStore.SetPolicy(
                        definition.Workload,
                        AuthorizedPolicy(definition));
                }

                AssertIndependentState(
                    profileStore,
                    fallbackStore,
                    definitions);

                ReachyFallbackPolicy vlmBefore =
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Vlm);
                fallbackStore.SetPolicy(
                    ReachyProviderWorkloadKind.Llm,
                    ReachyFallbackPolicy.NoFallback());
                Assert.That(
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Llm).Name,
                    Is.EqualTo(ReachyFallbackPolicy.DefaultPolicyName));
                Assert.That(
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Vlm).Name,
                    Is.EqualTo(vlmBefore.Name));
                Assert.That(
                    fallbackStore.GetPolicy(ReachyProviderWorkloadKind.Vlm)
                        .IsTargetAuthorized(definitions[3].Profile.ProviderId),
                    Is.True);

                fallbackStore.SetPolicy(
                    ReachyProviderWorkloadKind.Llm,
                    AuthorizedPolicy(definitions[2]));

                var reloadedProfiles =
                    new ReachyProviderProfilePersistenceStore(profilesPath);
                var reloadedPolicies =
                    new ReachyFallbackPolicyPersistenceStore(
                        new ReachyProviderFallbackPolicyEngine(),
                        policiesPath);
                reloadedProfiles.Initialize();
                reloadedPolicies.Initialize();
                AssertIndependentState(
                    reloadedProfiles,
                    reloadedPolicies,
                    definitions);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void Configure(
            ReachySettingsStateStore store,
            ReachyProviderKind kind,
            bool cloud)
        {
            int cycles;
            if (kind == ReachyProviderKind.Asr ||
                kind == ReachyProviderKind.Tts)
            {
                cycles = cloud ? 3 : 1;
            }
            else
            {
                cycles = cloud ? 2 : 1;
            }

            for (int index = 0; index < cycles; ++index)
            {
                store.CycleProvider(kind);
            }
        }

        private static void AssertMask(
            ReachySettingsSnapshot snapshot,
            ReachySettingsStorageReferences references,
            int mask)
        {
            foreach (ReachyProviderKind kind in
                     (ReachyProviderKind[])Enum.GetValues(
                         typeof(ReachyProviderKind)))
            {
                bool cloud = IsCloud(mask, kind);
                ReachyProviderSelection selection = snapshot.GetProvider(kind);
                Assert.That(
                    selection.Execution,
                    Is.EqualTo(
                        cloud
                            ? ReachyProviderExecution.Cloud
                            : ReachyProviderExecution.OnDevice),
                    $"mask={mask}; workload={kind}");
                Assert.That(
                    selection.SendsDataOffDevice,
                    Is.EqualTo(cloud),
                    $"off-device truth mask={mask}; workload={kind}");
                string profileId = ReferenceFor(references, kind);
                Assert.That(
                    profileId,
                    Is.EqualTo(cloud ? ProfileId(kind) : string.Empty),
                    $"reference mask={mask}; workload={kind}");

                string label = ReachySettingsStateStore.GetProviderKindLabel(kind);
                if (cloud)
                {
                    StringAssert.Contains(label, snapshot.PrivacyCloudSummary);
                }
                else
                {
                    StringAssert.DoesNotContain(
                        label + ": Cloud API preference",
                        snapshot.PrivacyCloudSummary);
                }
            }
        }

        private static ReachySettingsStorageReferences ReferencesForMask(int mask)
        {
            return new ReachySettingsStorageReferences(
                IsCloud(mask, ReachyProviderKind.Asr)
                    ? ProfileId(ReachyProviderKind.Asr)
                    : string.Empty,
                IsCloud(mask, ReachyProviderKind.Tts)
                    ? ProfileId(ReachyProviderKind.Tts)
                    : string.Empty,
                IsCloud(mask, ReachyProviderKind.Llm)
                    ? ProfileId(ReachyProviderKind.Llm)
                    : string.Empty,
                IsCloud(mask, ReachyProviderKind.Vlm)
                    ? ProfileId(ReachyProviderKind.Vlm)
                    : string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static string ReferenceFor(
            ReachySettingsStorageReferences references,
            ReachyProviderKind kind)
        {
            return kind switch
            {
                ReachyProviderKind.Asr => references.AsrProviderProfileId,
                ReachyProviderKind.Tts => references.TtsProviderProfileId,
                ReachyProviderKind.Llm => references.LlmProviderProfileId,
                ReachyProviderKind.Vlm => references.VlmProviderProfileId,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static bool IsCloud(int mask, ReachyProviderKind kind)
        {
            return (mask & (1 << (int)kind)) != 0;
        }

        private static string ProfileId(ReachyProviderKind kind)
        {
            return "rma190-" + kind.ToString().ToLowerInvariant() + "-profile";
        }

        private static ProviderDefinition[] Definitions()
        {
            return new[]
            {
                new ProviderDefinition(
                    ReachyProviderWorkloadKind.Asr,
                    Profile(
                        "rma190-cloud-asr",
                        ReachyProviderEndpointStyle.AudioTranscriptions,
                        ReachyProviderModelRole.Asr,
                        "rma190-asr-model",
                        "rma190-asr-key")),
                new ProviderDefinition(
                    ReachyProviderWorkloadKind.Tts,
                    Profile(
                        "rma190-cloud-tts",
                        ReachyProviderEndpointStyle.AudioSpeech,
                        ReachyProviderModelRole.Tts,
                        "rma190-tts-model",
                        "rma190-tts-key")),
                new ProviderDefinition(
                    ReachyProviderWorkloadKind.Llm,
                    Profile(
                        ReachyCloudLlmCredentialCoordinator.ProviderId,
                        ReachyProviderEndpointStyle.ChatCompletions,
                        ReachyProviderModelRole.Text,
                        "rma190-llm-model",
                        ReachyCloudLlmCredentialCoordinator.CredentialReference)),
                new ProviderDefinition(
                    ReachyProviderWorkloadKind.Vlm,
                    Profile(
                        ReachyCloudVlmCredentialCoordinator.ProviderId,
                        ReachyProviderEndpointStyle.Responses,
                        ReachyProviderModelRole.Vision,
                        "rma190-vlm-model",
                        ReachyCloudVlmCredentialCoordinator.CredentialReference)),
            };
        }

        private static ReachyProviderProfile Profile(
            string providerId,
            ReachyProviderEndpointStyle endpointStyle,
            ReachyProviderModelRole role,
            string modelId,
            string credentialReference)
        {
            return new ReachyProviderProfile(
                providerId,
                providerId,
                new Uri("https://example.invalid"),
                endpointStyle,
                new[]
                {
                    new ReachyProviderModelBinding(role, modelId),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30_000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference);
        }

        private static ReachyFallbackPolicy AuthorizedPolicy(
            ProviderDefinition definition)
        {
            return new ReachyFallbackPolicy(
                "rma190-" + definition.Workload.ToString().ToLowerInvariant(),
                allowLocalQualityReduction: false,
                allowSameProviderRetry: false,
                allowCrossProviderSwitch: true,
                allowNetworkProviderSwitch: true,
                new[] { definition.Profile.ProviderId });
        }

        private static void AssertIndependentState(
            ReachyProviderProfilePersistenceStore profileStore,
            ReachyFallbackPolicyPersistenceStore fallbackStore,
            IReadOnlyList<ProviderDefinition> definitions)
        {
            Assert.That(profileStore.Profiles.Count, Is.EqualTo(4));
            for (int index = 0; index < definitions.Count; ++index)
            {
                ProviderDefinition definition = definitions[index];
                Assert.That(
                    profileStore.TryGet(
                        definition.Profile.ProviderId,
                        out ReachyProviderProfile? profile),
                    Is.True);
                Assert.That(profile, Is.Not.Null);
                Assert.That(
                    profile!.GetModelId(
                        definition.Profile.ModelBindings[0].Role),
                    Is.EqualTo(
                        definition.Profile.ModelBindings[0].ModelId));

                ReachyFallbackPolicy policy =
                    fallbackStore.GetPolicy(definition.Workload);
                Assert.That(policy.AllowCrossProviderSwitch, Is.True);
                Assert.That(policy.AllowNetworkProviderSwitch, Is.True);
                Assert.That(
                    policy.AuthorizedTargetProviderIds.Count,
                    Is.EqualTo(1));
                Assert.That(
                    policy.IsTargetAuthorized(
                        definition.Profile.ProviderId),
                    Is.True);
            }
        }

        private static string TemporaryRoot(string suffix)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma190-provider-combinations-" +
                suffix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteRoot(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private sealed class ProviderDefinition
        {
            internal ProviderDefinition(
                ReachyProviderWorkloadKind workload,
                ReachyProviderProfile profile)
            {
                Workload = workload;
                Profile = profile;
            }

            internal ReachyProviderWorkloadKind Workload { get; }

            internal ReachyProviderProfile Profile { get; }
        }
    }
}
