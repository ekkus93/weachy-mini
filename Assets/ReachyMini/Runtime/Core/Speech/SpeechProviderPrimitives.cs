#nullable enable

using System;
using System.Threading;

namespace ReachyMini.Speech
{
    public enum SpeechProviderKind
    {
        AutomaticSpeechRecognition = 0,
        TextToSpeech = 1,
    }

    public enum SpeechProviderLocation
    {
        OnDevice = 0,
        DeviceService = 1,
        LocalNetwork = 2,
        Cloud = 3,
    }

    public enum SpeechNetworkRequirement
    {
        None = 0,
        ProviderControlled = 1,
        Required = 2,
    }

    public enum SpeechAvailabilityState
    {
        Available = 0,
        Unavailable = 1,
        PermissionRequired = 2,
        SetupRequired = 3,
        Busy = 4,
        Faulted = 5,
    }

    public enum SpeechErrorCategory
    {
        Permission = 0,
        Network = 1,
        Timeout = 2,
        Cancelled = 3,
        Busy = 4,
        UnsupportedLanguage = 5,
        MissingVoiceData = 6,
        ProviderUnavailable = 7,
        ServiceFailure = 8,
        MalformedResponse = 9,
        ContractViolation = 10,
        Unknown = 11,
    }

    public sealed class SpeechProviderDescriptor
    {
        public SpeechProviderDescriptor(
            SpeechProviderKind kind,
            string providerId,
            string instanceId,
            string displayName,
            string version,
            SpeechProviderLocation location,
            SpeechNetworkRequirement networkRequirement)
        {
            if (!Enum.IsDefined(typeof(SpeechProviderKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (!Enum.IsDefined(typeof(SpeechProviderLocation), location))
            {
                throw new ArgumentOutOfRangeException(nameof(location));
            }
            if (!Enum.IsDefined(typeof(SpeechNetworkRequirement), networkRequirement))
            {
                throw new ArgumentOutOfRangeException(nameof(networkRequirement));
            }

            ValidateLocationNetworkContract(location, networkRequirement);
            Kind = kind;
            ProviderId = RequireText(providerId, nameof(providerId));
            InstanceId = RequireText(instanceId, nameof(instanceId));
            DisplayName = RequireText(displayName, nameof(displayName));
            Version = RequireText(version, nameof(version));
            Location = location;
            NetworkRequirement = networkRequirement;
        }

        public SpeechProviderKind Kind { get; }
        public string ProviderId { get; }
        public string InstanceId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public SpeechProviderLocation Location { get; }
        public SpeechNetworkRequirement NetworkRequirement { get; }
        public bool MayUseNetwork => NetworkRequirement != SpeechNetworkRequirement.None;
        public bool RequiresNetworkDisclosure => MayUseNetwork;

        internal static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Speech provider contract text cannot be empty.", name);
            }

            return value;
        }

        private static void ValidateLocationNetworkContract(
            SpeechProviderLocation location,
            SpeechNetworkRequirement networkRequirement)
        {
            if (location == SpeechProviderLocation.OnDevice &&
                networkRequirement != SpeechNetworkRequirement.None)
            {
                throw new ArgumentException(
                    "An on-device speech provider cannot declare network use.",
                    nameof(networkRequirement));
            }

            if ((location == SpeechProviderLocation.LocalNetwork ||
                 location == SpeechProviderLocation.Cloud) &&
                networkRequirement != SpeechNetworkRequirement.Required)
            {
                throw new ArgumentException(
                    "Local-network and cloud speech providers must declare networking as required.",
                    nameof(networkRequirement));
            }
        }
    }

    public sealed class SpeechProviderAvailability
    {
        public SpeechProviderAvailability(SpeechAvailabilityState state, string diagnostic)
        {
            if (!Enum.IsDefined(typeof(SpeechAvailabilityState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            State = state;
            Diagnostic = SpeechProviderDescriptor.RequireText(diagnostic, nameof(diagnostic));
        }

        public SpeechAvailabilityState State { get; }
        public string Diagnostic { get; }
        public bool IsAvailable => State == SpeechAvailabilityState.Available;
    }

    public sealed class SpeechProviderError
    {
        public const int MaximumCodeCharacters = 128;
        public const int MaximumDiagnosticCharacters = 512;

        public SpeechProviderError(
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool isRetryable)
        {
            if (!Enum.IsDefined(typeof(SpeechErrorCategory), category))
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }

            Category = category;
            Code = RequireBoundedText(code, nameof(code), MaximumCodeCharacters);
            Diagnostic = RequireBoundedText(
                diagnostic,
                nameof(diagnostic),
                MaximumDiagnosticCharacters);
            IsRetryable = isRetryable;
        }

        public SpeechErrorCategory Category { get; }
        public string Code { get; }
        public string Diagnostic { get; }
        public bool IsRetryable { get; }

        private static string RequireBoundedText(
            string value,
            string name,
            int maximumCharacters)
        {
            string text = SpeechProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    text.Length,
                    "Speech error text exceeds its contract limit.");
            }

            return text;
        }
    }

    public sealed class SpeechProviderSelectionSnapshot
    {
        internal SpeechProviderSelectionSnapshot(
            SpeechProviderKind kind,
            string providerInstanceId,
            ulong epoch)
        {
            Kind = kind;
            ProviderInstanceId = SpeechProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            if (epoch == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(epoch));
            }

            Epoch = epoch;
        }

        public SpeechProviderKind Kind { get; }
        public string ProviderInstanceId { get; }
        public ulong Epoch { get; }
    }

    public sealed class SpeechProviderSelection
    {
        private readonly object sync = new object();
        private SpeechProviderSelectionSnapshot current;

        public SpeechProviderSelection(SpeechProviderDescriptor initialProvider)
        {
            if (initialProvider == null)
            {
                throw new ArgumentNullException(nameof(initialProvider));
            }

            current = new SpeechProviderSelectionSnapshot(
                initialProvider.Kind,
                initialProvider.InstanceId,
                1UL);
        }

        public SpeechProviderSelectionSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public SpeechProviderSelectionSnapshot Select(SpeechProviderDescriptor provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (sync)
            {
                if (provider.Kind != current.Kind)
                {
                    throw new ArgumentException(
                        "A speech provider selection cannot change provider kind.",
                        nameof(provider));
                }

                current = new SpeechProviderSelectionSnapshot(
                    provider.Kind,
                    provider.InstanceId,
                    checked(current.Epoch + 1UL));
                return current;
            }
        }

        public bool IsCurrent(SpeechOperationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (sync)
            {
                return context.ProviderKind == current.Kind &&
                    context.ProviderInstanceId == current.ProviderInstanceId &&
                    context.SelectionEpoch == current.Epoch;
            }
        }
    }

    public sealed class SpeechOperationContext
    {
        public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(5.0);

        public SpeechOperationContext(
            string requestId,
            SpeechProviderSelectionSnapshot selection,
            TimeSpan timeout)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    timeout,
                    "Speech operation timeouts must be in (0, 5 minutes].");
            }

            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            ProviderKind = selection.Kind;
            ProviderInstanceId = selection.ProviderInstanceId;
            SelectionEpoch = selection.Epoch;
            Timeout = timeout;
        }

        public string RequestId { get; }
        public SpeechProviderKind ProviderKind { get; }
        public string ProviderInstanceId { get; }
        public ulong SelectionEpoch { get; }
        public TimeSpan Timeout { get; }
    }

    public sealed class SpeechProviderPolicy
    {
        public bool AutomaticProviderFallbackEnabled { get; }
        public bool CrossPrivacyBoundaryFallbackEnabled { get; }
        public bool AutomaticRetryEnabled { get; }
    }
}
