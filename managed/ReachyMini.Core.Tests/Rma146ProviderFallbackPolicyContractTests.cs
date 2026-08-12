#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReachyMini.Providers;
using ReachyMini.Speech;

namespace ReachyMini.Core.Tests
{
    internal static class Rma146ProviderFallbackPolicyContractTests
    {
        private static readonly string[] TtsCloudBackupCandidates = { "tts-cloud" };

        [ModuleInitializer]
        internal static void Run()
        {
            MockFailureCannotActivateUnauthorizedProvider();
            AuthorizedSameBoundaryFallbackConsumesOneTimeToken();
            PrivacyBoundaryChangeRequiresMatchingConfirmation();
            WorkloadPoliciesRemainIndependent();
            RetryAndQualityReductionRemainExplicit();
        }

        private static void MockFailureCannotActivateUnauthorizedProvider()
        {
            SpeechProviderDescriptor source = Asr(
                "asr-local",
                "asr-local-instance",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            SpeechProviderDescriptor target = Asr(
                "asr-backup",
                "asr-backup-instance",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            var selection = new SpeechProviderSelection(source);
            var engine = new ReachyProviderFallbackPolicyEngine();
            var request = new ReachyProviderSwitchRequest(
                Endpoint(ReachyProviderWorkloadKind.Asr, source.ProviderId, ReachyProviderPrivacyBoundary.OnDevice),
                Endpoint(ReachyProviderWorkloadKind.Asr, target.ProviderId, ReachyProviderPrivacyBoundary.OnDevice),
                "mock-provider-failure");

            ReachyFallbackDecision decision = engine.EvaluateProviderSwitch(request);
            Equal(ReachyFallbackDecisionStatus.Denied, decision.Status, "default fallback decision");
            Equal(null, decision.Authorization, "default fallback authorization");
            Equal(source.InstanceId, selection.Current.ProviderInstanceId, "selection after denied fallback");
        }

        private static void AuthorizedSameBoundaryFallbackConsumesOneTimeToken()
        {
            SpeechProviderDescriptor source = Asr(
                "asr-primary",
                "asr-primary-instance",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            SpeechProviderDescriptor target = Asr(
                "asr-secondary",
                "asr-secondary-instance",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            var selection = new SpeechProviderSelection(source);
            var engine = new ReachyProviderFallbackPolicyEngine();
            engine.SetPolicy(
                ReachyProviderWorkloadKind.Asr,
                new ReachyFallbackPolicy(
                    "asr-local-backup",
                    false,
                    false,
                    true,
                    false,
                    new[] { target.ProviderId }));
            var request = new ReachyProviderSwitchRequest(
                Endpoint(ReachyProviderWorkloadKind.Asr, source.ProviderId, ReachyProviderPrivacyBoundary.OnDevice),
                Endpoint(ReachyProviderWorkloadKind.Asr, target.ProviderId, ReachyProviderPrivacyBoundary.OnDevice),
                "mock-provider-failure");

            ReachyFallbackDecision decision = engine.EvaluateProviderSwitch(request);
            Equal(ReachyFallbackDecisionStatus.Authorized, decision.Status, "same-boundary fallback decision");
            ReachyAuthorizedProviderSwitch authorization = decision.Authorization ??
                throw new InvalidOperationException("Authorized fallback omitted authorization token.");
            SpeechProviderSelectionSnapshot snapshot = selection.SelectFallback(
                source,
                target,
                authorization);
            Equal(target.InstanceId, snapshot.ProviderInstanceId, "authorized fallback selection");
            Throws<InvalidOperationException>(() => authorization.Consume(
                ReachyProviderWorkloadKind.Asr,
                source.ProviderId,
                target.ProviderId));
        }

        private static void PrivacyBoundaryChangeRequiresMatchingConfirmation()
        {
            var engine = new ReachyProviderFallbackPolicyEngine();
            engine.SetPolicy(
                ReachyProviderWorkloadKind.Tts,
                new ReachyFallbackPolicy(
                    "tts-cloud-backup",
                    false,
                    false,
                    true,
                    true,
                    TtsCloudBackupCandidates));
            var request = new ReachyProviderSwitchRequest(
                Endpoint(ReachyProviderWorkloadKind.Tts, "tts-local", ReachyProviderPrivacyBoundary.OnDevice),
                Endpoint(ReachyProviderWorkloadKind.Tts, "tts-cloud", ReachyProviderPrivacyBoundary.Cloud),
                "mock-provider-failure");

            ReachyFallbackDecision initial = engine.EvaluateProviderSwitch(request);
            Equal(
                ReachyFallbackDecisionStatus.RequiresPrivacyConfirmation,
                initial.Status,
                "privacy confirmation requirement");
            ReachyPrivacyBoundaryConfirmation confirmation =
                engine.ConfirmPrivacyBoundaryChange(request);
            ReachyFallbackDecision confirmed = engine.EvaluateProviderSwitch(
                request,
                confirmation);
            Equal(ReachyFallbackDecisionStatus.Authorized, confirmed.Status, "confirmed privacy fallback");
            ReachyFallbackDecision reused = engine.EvaluateProviderSwitch(
                request,
                confirmation);
            Equal(
                ReachyFallbackDecisionStatus.RequiresPrivacyConfirmation,
                reused.Status,
                "one-time privacy confirmation reuse");
        }

        private static void WorkloadPoliciesRemainIndependent()
        {
            var engine = new ReachyProviderFallbackPolicyEngine();
            var asrPolicy = new ReachyFallbackPolicy(
                "asr-retry-only",
                false,
                true,
                false,
                false,
                Array.Empty<string>());
            engine.SetPolicy(ReachyProviderWorkloadKind.Asr, asrPolicy);

            Equal("asr-retry-only", engine.GetPolicy(ReachyProviderWorkloadKind.Asr).Name, "ASR policy name");
            Equal(ReachyFallbackPolicy.DefaultPolicyName, engine.GetPolicy(ReachyProviderWorkloadKind.Tts).Name, "TTS policy name");
            Equal(ReachyFallbackPolicy.DefaultPolicyName, engine.GetPolicy(ReachyProviderWorkloadKind.Llm).Name, "LLM policy name");
            Equal(ReachyFallbackPolicy.DefaultPolicyName, engine.GetPolicy(ReachyProviderWorkloadKind.Vlm).Name, "VLM policy name");
        }

        private static void RetryAndQualityReductionRemainExplicit()
        {
            var engine = new ReachyProviderFallbackPolicyEngine();
            ReachyProviderEndpointIdentity provider = Endpoint(
                ReachyProviderWorkloadKind.Llm,
                "llm-local",
                ReachyProviderPrivacyBoundary.OnDevice);
            Equal(
                ReachyFallbackDecisionStatus.Denied,
                engine.EvaluateSameProviderRetry(provider, "mock-timeout").Status,
                "default retry policy");
            Equal(
                ReachyFallbackDecisionStatus.Denied,
                engine.EvaluateLocalQualityReduction(provider, "mock-memory-pressure").Status,
                "default local quality policy");

            engine.SetPolicy(
                ReachyProviderWorkloadKind.Llm,
                new ReachyFallbackPolicy(
                    "llm-local-degrade",
                    true,
                    true,
                    false,
                    false,
                    Array.Empty<string>()));
            Equal(
                ReachyFallbackDecisionStatus.Authorized,
                engine.EvaluateSameProviderRetry(provider, "mock-timeout").Status,
                "authorized retry policy");
            Equal(
                ReachyFallbackDecisionStatus.Authorized,
                engine.EvaluateLocalQualityReduction(provider, "mock-memory-pressure").Status,
                "authorized local quality policy");
        }

        private static SpeechProviderDescriptor Asr(
            string providerId,
            string instanceId,
            SpeechProviderLocation location,
            SpeechNetworkRequirement networkRequirement)
        {
            return new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                providerId,
                instanceId,
                providerId,
                "test-v1",
                location,
                networkRequirement);
        }

        private static ReachyProviderEndpointIdentity Endpoint(
            ReachyProviderWorkloadKind workload,
            string providerId,
            ReachyProviderPrivacyBoundary boundary)
        {
            return new ReachyProviderEndpointIdentity(workload, providerId, boundary);
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-146 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"RMA-146 contract expected {typeof(TException).Name}.");
        }
    }
}
