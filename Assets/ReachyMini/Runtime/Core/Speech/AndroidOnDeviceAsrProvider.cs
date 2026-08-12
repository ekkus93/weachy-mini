#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidOnDeviceAsrProvider : IAsrProvider
    {
        public const string ProviderId = "android-explicit-on-device-speech-recognizer";
        public const string ProviderVersion = "rma-121-v1";

        private readonly IAndroidOnDeviceAsrPlatform platform;
        private readonly string configuredLanguageTag;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private int operationInFlight;
        private int disposed;

        public AndroidOnDeviceAsrProvider(
            IAndroidOnDeviceAsrPlatform platform,
            string instanceId,
            string configuredLanguageTag,
            TimeSpan maximumUtteranceDuration)
        {
            this.platform = platform ??
                throw new ArgumentNullException(nameof(platform));
            this.configuredLanguageTag = SpeechProviderDescriptor.RequireText(
                configuredLanguageTag,
                nameof(configuredLanguageTag));

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                ProviderId,
                instanceId,
                "Android explicit on-device ASR",
                ProviderVersion,
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            Capabilities = new AsrCapabilities(
                new[] { this.configuredLanguageTag },
                supportsPartialResults: true,
                supportsCancellation: true,
                maximumUtteranceDuration);
        }

        public SpeechProviderDescriptor Descriptor { get; }

        public AsrCapabilities Capabilities { get; }

        public async ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ThrowIfDisposed();
            if (!TryAcquireOperation())
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Busy,
                    "Android on-device ASR is busy with another provider operation; requests are not queued.");
            }

            try
            {
                Readiness readiness = await EvaluateReadinessAsync(
                    options,
                    cancellationToken).ConfigureAwait(false);
                return readiness.Availability;
            }
            finally
            {
                ReleaseOperation();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            lifetimeCancellation.Cancel();
            try
            {
                await platform.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                lifetimeCancellation.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private bool TryAcquireOperation()
        {
            return Interlocked.CompareExchange(
                ref operationInFlight,
                1,
                0) == 0;
        }

        private void ReleaseOperation()
        {
            Interlocked.Exchange(ref operationInFlight, 0);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(AndroidOnDeviceAsrProvider));
            }
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "Android on-device ASR returned no diagnostic detail."
                : value;
            return result.Length <= maximumCharacters
                ? result
                : result.Substring(0, maximumCharacters);
        }
    }
}
