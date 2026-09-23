#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.AppState;
using ReachyMini.Providers;

namespace ReachyMini.Application.Tests
{
    internal static class Rma190IndependentProviderCombinationTests
    {
        private static readonly ReachyProviderKind[] ProviderKinds =
        {
            ReachyProviderKind.Asr,
            ReachyProviderKind.Tts,
            ReachyProviderKind.Llm,
            ReachyProviderKind.Vlm,
        };

        private static readonly ReachyProviderWorkloadKind[] Workloads =
        {
            ReachyProviderWorkloadKind.Asr,
            ReachyProviderWorkloadKind.Tts,
            ReachyProviderWorkloadKind.Llm,
            ReachyProviderWorkloadKind.Vlm,
        };

        internal static void RunAll()
        {
            AllSixteenLocalCloudCombinationsRoundTripIndependently();
            CyclingOneProviderNeverMutatesAnotherWorkload();
            DurableProfileReferencesRemainWorkloadScoped();
            FallbackAuthorizationRemainsWorkloadScoped();
        }

        private static void AllSixteenLocalCloudCombinationsRoundTripIndependently()
        {
            const int combinationCount = 1 << 4;
            for (int mask = 0; mask < combinationCount; ++mask)
            {
                ReachyDurableSettings durable = CreateCombination(mask);
                var store = new ReachySettingsStateStore();
                store.ApplyDurableSettings(durable);

                ReachyDurableSettings roundTrip = store.CaptureDurableSettings();
                for (int index = 0; index < ProviderKinds.Length; ++index)
                {
                    bool cloud = (mask & (1 << index)) != 0;
                    ReachyProviderExecution expected = cloud
                        ? ReachyProviderExecution.Cloud
                        : ReachyProviderExecution.OnDevice;
                    ReachyProviderSelection actual = store.Current.GetProvider(
                        ProviderKinds[index]);

                    Equal(expected, actual.Execution, $"combination {mask} {ProviderKinds[index]} execution");
                    Equal(
                        cloud
                            ? ReachyConnectivityRequirement.NetworkRequired
                            : ReachyConnectivityRequirement.OfflineCapable,
                        actual.Connectivity,
                        $"combination {mask} {ProviderKinds[index]} connectivity");
                    Equal(cloud, actual.SendsDataOffDevice, $"combination {mask} {ProviderKinds[index]} off-device disclosure");
                    Equal(
                        (int)expected,
                        GetDurableExecution(roundTrip, ProviderKinds[index]),
                        $"combination {mask} {ProviderKinds[index]} durable round trip");
                }

                bool anyCloud = mask != 0;
                Equal(
                    anyCloud,
                    store.Current.PrivacyCloudSummary.StartsWith(
                        "Cloud/network-bound selections",
                        StringComparison.Ordinal),
                    $"combination {mask} privacy summary");
            }
        }

        private static void CyclingOneProviderNeverMutatesAnotherWorkload()
        {
            foreach (ReachyProviderKind targetKind in ProviderKinds)
            {
                var store = new ReachySettingsStateStore();
                ReachyProviderExecution[] before = CopyExecutions(store);

                int guard = 0;
                while (store.Current.GetProvider(targetKind).Execution != ReachyProviderExecution.Cloud)
                {
                    store.CycleProvider(targetKind);
                    ++guard;
                    if (guard > 4)
                    {
                        throw new InvalidOperationException(
                            $"Managed test failed: {targetKind} did not reach Cloud within the bounded provider cycle.");
                    }
                }

                for (int index = 0; index < ProviderKinds.Length; ++index)
                {
                    ReachyProviderKind kind = ProviderKinds[index];
                    ReachyProviderExecution actual = store.Current.GetProvider(kind).Execution;
                    Equal(
                        kind == targetKind ? ReachyProviderExecution.Cloud : before[index],
                        actual,
                        $"cycling {targetKind} preserves {kind}");
                }
            }
        }

        private static void DurableProfileReferencesRemainWorkloadScoped()
        {
            var references = new ReachySettingsStorageReferences(
                "asr-profile",
                "tts-profile",
                "llm-profile",
                "vlm-profile",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

            Equal("asr-profile", references.AsrProviderProfileId, "ASR profile reference");
            Equal("tts-profile", references.TtsProviderProfileId, "TTS profile reference");
            Equal("llm-profile", references.LlmProviderProfileId, "LLM profile reference");
            Equal("vlm-profile", references.VlmProviderProfileId, "VLM profile reference");
        }

        private static void FallbackAuthorizationRemainsWorkloadScoped()
        {
            var engine = new ReachyProviderFallbackPolicyEngine();
            for (int index = 0; index < Workloads.Length; ++index)
            {
                ReachyProviderWorkloadKind target = Workloads[index];
                string targetProviderId = $"{target.ToString().ToLowerInvariant()}-cloud-test";
                engine.SetPolicy(
                    target,
                    new ReachyFallbackPolicy(
                        $"{target.ToString().ToLowerInvariant()}-cloud-authorized",
                        allowLocalQualityReduction: false,
                        allowSameProviderRetry: false,
                        allowCrossProviderSwitch: true,
                        allowNetworkProviderSwitch: true,
                        new[] { targetProviderId }));

                for (int otherIndex = 0; otherIndex < Workloads.Length; ++otherIndex)
                {
                    ReachyProviderWorkloadKind other = Workloads[otherIndex];
                    ReachyFallbackPolicy policy = engine.GetPolicy(other);
                    if (other == target)
                    {
                        Equal(true, policy.AllowCrossProviderSwitch, $"{target} cross-provider authorization");
                        Equal(true, policy.AllowNetworkProviderSwitch, $"{target} network authorization");
                        Equal(true, policy.IsTargetAuthorized(targetProviderId), $"{target} target authorization");
                    }
                    else if (otherIndex > index)
                    {
                        Equal(
                            ReachyFallbackPolicy.DefaultPolicyName,
                            policy.Name,
                            $"authorizing {target} preserves untouched {other}");
                    }
                }
            }
        }

        private static ReachyDurableSettings CreateCombination(int mask)
        {
            return new ReachyDurableSettings
            {
                AsrExecution = ExecutionFor(mask, 0),
                TtsExecution = ExecutionFor(mask, 1),
                LlmExecution = ExecutionFor(mask, 2),
                VlmExecution = ExecutionFor(mask, 3),
            };
        }

        private static int ExecutionFor(int mask, int bit)
        {
            return (int)(((mask & (1 << bit)) != 0)
                ? ReachyProviderExecution.Cloud
                : ReachyProviderExecution.OnDevice);
        }

        private static int GetDurableExecution(
            ReachyDurableSettings durable,
            ReachyProviderKind kind)
        {
            return kind switch
            {
                ReachyProviderKind.Asr => durable.AsrExecution,
                ReachyProviderKind.Tts => durable.TtsExecution,
                ReachyProviderKind.Llm => durable.LlmExecution,
                ReachyProviderKind.Vlm => durable.VlmExecution,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static ReachyProviderExecution[] CopyExecutions(
            ReachySettingsStateStore store)
        {
            var result = new ReachyProviderExecution[ProviderKinds.Length];
            for (int index = 0; index < ProviderKinds.Length; ++index)
            {
                result[index] = store.Current.GetProvider(ProviderKinds[index]).Execution;
            }
            return result;
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }
    }
}
