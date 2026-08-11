#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace ReachyMini.Providers
{
    public enum ReachyProviderWorkloadKind
    {
        Asr = 0,
        Tts = 1,
        Llm = 2,
        Vlm = 3,
    }

    public enum ReachyProviderPrivacyBoundary
    {
        OnDevice = 0,
        DeviceService = 1,
        LocalNetwork = 2,
        Cloud = 3,
    }

    public enum ReachyFallbackDecisionStatus
    {
        Denied = 0,
        RequiresPrivacyConfirmation = 1,
        Authorized = 2,
    }

    public enum ReachyFallbackActionKind
    {
        ProviderSwitch = 0,
        SameProviderRetry = 1,
        LocalQualityReduction = 2,
    }

    public sealed class ReachyFallbackPolicy
    {
        public const string DefaultPolicyName = "no-fallback";
        public const int MaximumAuthorizedTargets = 32;

        private readonly string[] authorizedTargetProviderIds;

        public ReachyFallbackPolicy(
            string name,
            bool allowLocalQualityReduction,
            bool allowSameProviderRetry,
            bool allowCrossProviderSwitch,
            bool allowNetworkProviderSwitch,
            IEnumerable<string> authorizedTargetProviderIds)
        {
            Name = RequireIdentifier(name, nameof(name), 64);
            if (authorizedTargetProviderIds == null)
            {
                throw new ArgumentNullException(nameof(authorizedTargetProviderIds));
            }

            var targets = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? providerId in authorizedTargetProviderIds)
            {
                string validated = RequireIdentifier(
                    providerId,
                    nameof(authorizedTargetProviderIds),
                    96);
                if (!seen.Add(validated))
                {
                    throw new ArgumentException(
                        "Fallback target provider identifiers must be unique.",
                        nameof(authorizedTargetProviderIds));
                }
                targets.Add(validated);
                if (targets.Count > MaximumAuthorizedTargets)
                {
                    throw new ArgumentException(
                        "Fallback policy contains too many authorized targets.",
                        nameof(authorizedTargetProviderIds));
                }
            }

            if (!allowCrossProviderSwitch && targets.Count != 0)
            {
                throw new ArgumentException(
                    "A policy that disables cross-provider switching cannot retain authorized fallback targets.",
                    nameof(authorizedTargetProviderIds));
            }
            if (allowNetworkProviderSwitch && !allowCrossProviderSwitch)
            {
                throw new ArgumentException(
                    "Network provider fallback cannot be enabled while cross-provider switching is disabled.",
                    nameof(allowNetworkProviderSwitch));
            }

            AllowLocalQualityReduction = allowLocalQualityReduction;
            AllowSameProviderRetry = allowSameProviderRetry;
            AllowCrossProviderSwitch = allowCrossProviderSwitch;
            AllowNetworkProviderSwitch = allowNetworkProviderSwitch;
            this.authorizedTargetProviderIds = targets.ToArray();
        }

        public string Name { get; }

        public bool AllowLocalQualityReduction { get; }

        public bool AllowSameProviderRetry { get; }

        public bool AllowCrossProviderSwitch { get; }

        public bool AllowNetworkProviderSwitch { get; }

        public IReadOnlyList<string> AuthorizedTargetProviderIds =>
            Array.AsReadOnly(authorizedTargetProviderIds);

        public bool IsTargetAuthorized(string providerId)
        {
            string validated = RequireIdentifier(providerId, nameof(providerId), 96);
            for (int index = 0; index < authorizedTargetProviderIds.Length; ++index)
            {
                if (string.Equals(
                        authorizedTargetProviderIds[index],
                        validated,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static ReachyFallbackPolicy NoFallback()
        {
            return new ReachyFallbackPolicy(
                DefaultPolicyName,
                allowLocalQualityReduction: false,
                allowSameProviderRetry: false,
                allowCrossProviderSwitch: false,
                allowNetworkProviderSwitch: false,
                Array.Empty<string>());
        }

        internal static string RequireIdentifier(
            string? value,
            string name,
            int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
            {
                throw new ArgumentException(
                    "Fallback policy identifiers must be bounded lowercase identifier text.",
                    name);
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' || character == '_' || character == '-';
                if (!valid)
                {
                    throw new ArgumentException(
                        "Fallback policy identifiers must be bounded lowercase identifier text.",
                        name);
                }
            }
            return value;
        }
    }

    public sealed class ReachyProviderEndpointIdentity
    {
        public ReachyProviderEndpointIdentity(
            ReachyProviderWorkloadKind workload,
            string providerId,
            ReachyProviderPrivacyBoundary privacyBoundary)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderWorkloadKind), workload))
            {
                throw new ArgumentOutOfRangeException(nameof(workload));
            }
            if (!Enum.IsDefined(
                    typeof(ReachyProviderPrivacyBoundary),
                    privacyBoundary))
            {
                throw new ArgumentOutOfRangeException(nameof(privacyBoundary));
            }

            Workload = workload;
            ProviderId = ReachyFallbackPolicy.RequireIdentifier(
                providerId,
                nameof(providerId),
                96);
            PrivacyBoundary = privacyBoundary;
        }

        public ReachyProviderWorkloadKind Workload { get; }

        public string ProviderId { get; }

        public ReachyProviderPrivacyBoundary PrivacyBoundary { get; }
    }

    public sealed class ReachyProviderSwitchRequest
    {
        public ReachyProviderSwitchRequest(
            ReachyProviderEndpointIdentity source,
            ReachyProviderEndpointIdentity target,
            string reasonCode)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (source.Workload != target.Workload)
            {
                throw new ArgumentException(
                    "Provider fallback cannot cross workload kinds.",
                    nameof(target));
            }
            if (string.Equals(
                    source.ProviderId,
                    target.ProviderId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A provider switch request requires distinct source and target providers.",
                    nameof(target));
            }
            ReasonCode = ReachyFallbackPolicy.RequireIdentifier(
                reasonCode,
                nameof(reasonCode),
                96);
        }

        public ReachyProviderEndpointIdentity Source { get; }

        public ReachyProviderEndpointIdentity Target { get; }

        public string ReasonCode { get; }

        public bool ChangesPrivacyBoundary =>
            Source.PrivacyBoundary != Target.PrivacyBoundary;

        public bool TargetsNetworkBoundary =>
            Target.PrivacyBoundary == ReachyProviderPrivacyBoundary.LocalNetwork ||
            Target.PrivacyBoundary == ReachyProviderPrivacyBoundary.Cloud;
    }

    public sealed class ReachyPrivacyBoundaryConfirmation
    {
        private int consumed;

        internal ReachyPrivacyBoundaryConfirmation(
            string policyName,
            ReachyProviderSwitchRequest request,
            ulong confirmationId)
        {
            PolicyName = policyName;
            Workload = request.Source.Workload;
            SourceProviderId = request.Source.ProviderId;
            TargetProviderId = request.Target.ProviderId;
            ReasonCode = request.ReasonCode;
            SourceBoundary = request.Source.PrivacyBoundary;
            TargetBoundary = request.Target.PrivacyBoundary;
            ConfirmationId = confirmationId;
        }

        public string PolicyName { get; }

        public ReachyProviderWorkloadKind Workload { get; }

        public string SourceProviderId { get; }

        public string TargetProviderId { get; }

        public string ReasonCode { get; }

        public ReachyProviderPrivacyBoundary SourceBoundary { get; }

        public ReachyProviderPrivacyBoundary TargetBoundary { get; }

        internal ulong ConfirmationId { get; }

        internal bool TryConsume()
        {
            return Interlocked.Exchange(ref consumed, 1) == 0;
        }
    }

    public sealed class ReachyAuthorizedProviderSwitch
    {
        private int consumed;

        internal ReachyAuthorizedProviderSwitch(
            string policyName,
            ReachyProviderSwitchRequest request,
            bool privacyBoundaryConfirmed)
        {
            PolicyName = policyName;
            Workload = request.Source.Workload;
            SourceProviderId = request.Source.ProviderId;
            TargetProviderId = request.Target.ProviderId;
            ReasonCode = request.ReasonCode;
            PrivacyBoundaryConfirmed = privacyBoundaryConfirmed;
        }

        public string PolicyName { get; }

        public ReachyProviderWorkloadKind Workload { get; }

        public string SourceProviderId { get; }

        public string TargetProviderId { get; }

        public string ReasonCode { get; }

        public bool PrivacyBoundaryConfirmed { get; }

        public void Consume(
            ReachyProviderWorkloadKind workload,
            string sourceProviderId,
            string targetProviderId)
        {
            if (workload != Workload ||
                !string.Equals(
                    sourceProviderId,
                    SourceProviderId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    targetProviderId,
                    TargetProviderId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Provider fallback authorization does not match the requested switch.");
            }
            if (Interlocked.Exchange(ref consumed, 1) != 0)
            {
                throw new InvalidOperationException(
                    "Provider fallback authorization has already been consumed.");
            }
        }
    }

    public sealed class ReachyFallbackDecision
    {
        internal ReachyFallbackDecision(
            ReachyFallbackDecisionStatus status,
            string diagnosticCode,
            ReachyAuthorizedProviderSwitch? authorization)
        {
            Status = status;
            DiagnosticCode = ReachyFallbackPolicy.RequireIdentifier(
                diagnosticCode,
                nameof(diagnosticCode),
                96);
            Authorization = authorization;
        }

        public ReachyFallbackDecisionStatus Status { get; }

        public string DiagnosticCode { get; }

        public ReachyAuthorizedProviderSwitch? Authorization { get; }
    }

    public sealed class ReachyFallbackDiagnostic
    {
        internal ReachyFallbackDiagnostic(
            DateTimeOffset timestamp,
            ReachyFallbackActionKind action,
            ReachyProviderWorkloadKind workload,
            string policyName,
            string sourceProviderId,
            string targetProviderId,
            string reasonCode,
            string decisionCode)
        {
            Timestamp = timestamp;
            Action = action;
            Workload = workload;
            PolicyName = policyName;
            SourceProviderId = sourceProviderId;
            TargetProviderId = targetProviderId;
            ReasonCode = reasonCode;
            DecisionCode = decisionCode;
        }

        public DateTimeOffset Timestamp { get; }

        public ReachyFallbackActionKind Action { get; }

        public ReachyProviderWorkloadKind Workload { get; }

        public string PolicyName { get; }

        public string SourceProviderId { get; }

        public string TargetProviderId { get; }

        public string ReasonCode { get; }

        public string DecisionCode { get; }
    }
}
