#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace ReachyMini.Providers
{
    public sealed class ReachyProviderFallbackPolicyEngine
    {
        public const int MaximumDiagnostics = 128;

        private readonly object sync = new object();
        private readonly ReachyFallbackPolicy[] policies =
        {
            ReachyFallbackPolicy.NoFallback(),
            ReachyFallbackPolicy.NoFallback(),
            ReachyFallbackPolicy.NoFallback(),
            ReachyFallbackPolicy.NoFallback(),
        };
        private readonly Queue<ReachyFallbackDiagnostic> diagnostics =
            new Queue<ReachyFallbackDiagnostic>();
        private ulong nextConfirmationId;

        public ReachyFallbackPolicy GetPolicy(ReachyProviderWorkloadKind workload)
        {
            ValidateWorkload(workload);
            lock (sync)
            {
                return policies[(int)workload];
            }
        }

        public void SetPolicy(
            ReachyProviderWorkloadKind workload,
            ReachyFallbackPolicy policy)
        {
            ValidateWorkload(workload);
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            lock (sync)
            {
                policies[(int)workload] = policy;
            }
        }

        public ReachyFallbackDecision EvaluateProviderSwitch(
            ReachyProviderSwitchRequest request,
            ReachyPrivacyBoundaryConfirmation? confirmation = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (sync)
            {
                ReachyFallbackPolicy policy = policies[(int)request.Source.Workload];
                if (!policy.AllowCrossProviderSwitch)
                {
                    return Decision(
                        request,
                        policy,
                        ReachyFallbackDecisionStatus.Denied,
                        "cross-provider-switch-disabled",
                        null);
                }
                if (!policy.IsTargetAuthorized(request.Target.ProviderId))
                {
                    return Decision(
                        request,
                        policy,
                        ReachyFallbackDecisionStatus.Denied,
                        "target-provider-not-authorized",
                        null);
                }
                if (request.TargetsNetworkBoundary &&
                    !policy.AllowNetworkProviderSwitch)
                {
                    return Decision(
                        request,
                        policy,
                        ReachyFallbackDecisionStatus.Denied,
                        "network-provider-switch-disabled",
                        null);
                }
                if (request.ChangesPrivacyBoundary)
                {
                    if (!MatchesConfirmation(policy, request, confirmation) ||
                        confirmation == null || !confirmation.TryConsume())
                    {
                        return Decision(
                            request,
                            policy,
                            ReachyFallbackDecisionStatus.RequiresPrivacyConfirmation,
                            "privacy-boundary-confirmation-required",
                            null);
                    }
                }

                var authorization = new ReachyAuthorizedProviderSwitch(
                    policy.Name,
                    request,
                    privacyBoundaryConfirmed: request.ChangesPrivacyBoundary);
                return Decision(
                    request,
                    policy,
                    ReachyFallbackDecisionStatus.Authorized,
                    "provider-switch-authorized",
                    authorization);
            }
        }

        public ReachyPrivacyBoundaryConfirmation ConfirmPrivacyBoundaryChange(
            ReachyProviderSwitchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!request.ChangesPrivacyBoundary)
            {
                throw new ArgumentException(
                    "Privacy confirmation is only valid for a boundary-changing provider switch.",
                    nameof(request));
            }

            lock (sync)
            {
                ReachyFallbackPolicy policy = policies[(int)request.Source.Workload];
                if (!policy.AllowCrossProviderSwitch ||
                    !policy.IsTargetAuthorized(request.Target.ProviderId) ||
                    request.TargetsNetworkBoundary && !policy.AllowNetworkProviderSwitch)
                {
                    throw new InvalidOperationException(
                        "A denied provider switch cannot be privacy-confirmed into an allowed switch.");
                }
                nextConfirmationId = checked(nextConfirmationId + 1UL);
                return new ReachyPrivacyBoundaryConfirmation(
                    policy.Name,
                    request,
                    nextConfirmationId);
            }
        }

        public ReachyFallbackDecision EvaluateSameProviderRetry(
            ReachyProviderEndpointIdentity provider,
            string reasonCode)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            string reason = ReachyFallbackPolicy.RequireIdentifier(
                reasonCode,
                nameof(reasonCode),
                96);
            lock (sync)
            {
                ReachyFallbackPolicy policy = policies[(int)provider.Workload];
                bool allowed = policy.AllowSameProviderRetry;
                RecordDiagnostic(
                    ReachyFallbackActionKind.SameProviderRetry,
                    provider.Workload,
                    policy.Name,
                    provider.ProviderId,
                    provider.ProviderId,
                    reason,
                    allowed ? "same-provider-retry-authorized" : "same-provider-retry-disabled");
                return new ReachyFallbackDecision(
                    allowed
                        ? ReachyFallbackDecisionStatus.Authorized
                        : ReachyFallbackDecisionStatus.Denied,
                    allowed
                        ? "same-provider-retry-authorized"
                        : "same-provider-retry-disabled",
                    null);
            }
        }

        public ReachyFallbackDecision EvaluateLocalQualityReduction(
            ReachyProviderEndpointIdentity provider,
            string reasonCode)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            string reason = ReachyFallbackPolicy.RequireIdentifier(
                reasonCode,
                nameof(reasonCode),
                96);
            lock (sync)
            {
                ReachyFallbackPolicy policy = policies[(int)provider.Workload];
                bool allowed = policy.AllowLocalQualityReduction &&
                    provider.PrivacyBoundary == ReachyProviderPrivacyBoundary.OnDevice;
                string decisionCode = allowed
                    ? "local-quality-reduction-authorized"
                    : "local-quality-reduction-disabled";
                RecordDiagnostic(
                    ReachyFallbackActionKind.LocalQualityReduction,
                    provider.Workload,
                    policy.Name,
                    provider.ProviderId,
                    provider.ProviderId,
                    reason,
                    decisionCode);
                return new ReachyFallbackDecision(
                    allowed
                        ? ReachyFallbackDecisionStatus.Authorized
                        : ReachyFallbackDecisionStatus.Denied,
                    decisionCode,
                    null);
            }
        }

        public IReadOnlyList<ReachyFallbackDiagnostic> CopyDiagnostics()
        {
            lock (sync)
            {
                return new List<ReachyFallbackDiagnostic>(diagnostics).AsReadOnly();
            }
        }

        private ReachyFallbackDecision Decision(
            ReachyProviderSwitchRequest request,
            ReachyFallbackPolicy policy,
            ReachyFallbackDecisionStatus status,
            string diagnosticCode,
            ReachyAuthorizedProviderSwitch? authorization)
        {
            RecordDiagnostic(
                ReachyFallbackActionKind.ProviderSwitch,
                request.Source.Workload,
                policy.Name,
                request.Source.ProviderId,
                request.Target.ProviderId,
                request.ReasonCode,
                diagnosticCode);
            return new ReachyFallbackDecision(
                status,
                diagnosticCode,
                authorization);
        }

        private static bool MatchesConfirmation(
            ReachyFallbackPolicy policy,
            ReachyProviderSwitchRequest request,
            ReachyPrivacyBoundaryConfirmation? confirmation)
        {
            return confirmation != null &&
                confirmation.ConfirmationId != 0UL &&
                string.Equals(
                    confirmation.PolicyName,
                    policy.Name,
                    StringComparison.Ordinal) &&
                confirmation.Workload == request.Source.Workload &&
                string.Equals(
                    confirmation.SourceProviderId,
                    request.Source.ProviderId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    confirmation.TargetProviderId,
                    request.Target.ProviderId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    confirmation.ReasonCode,
                    request.ReasonCode,
                    StringComparison.Ordinal) &&
                confirmation.SourceBoundary == request.Source.PrivacyBoundary &&
                confirmation.TargetBoundary == request.Target.PrivacyBoundary;
        }

        private void RecordDiagnostic(
            ReachyFallbackActionKind action,
            ReachyProviderWorkloadKind workload,
            string policyName,
            string sourceProviderId,
            string targetProviderId,
            string reasonCode,
            string decisionCode)
        {
            diagnostics.Enqueue(new ReachyFallbackDiagnostic(
                DateTimeOffset.UtcNow,
                action,
                workload,
                policyName,
                sourceProviderId,
                targetProviderId,
                reasonCode,
                decisionCode));
            while (diagnostics.Count > MaximumDiagnostics)
            {
                diagnostics.Dequeue();
            }
        }

        private static void ValidateWorkload(ReachyProviderWorkloadKind workload)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderWorkloadKind), workload))
            {
                throw new ArgumentOutOfRangeException(nameof(workload));
            }
        }
    }
}
