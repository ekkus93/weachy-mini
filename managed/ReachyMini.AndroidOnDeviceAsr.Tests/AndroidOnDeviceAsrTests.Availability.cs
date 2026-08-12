#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static async Task DescriptorIsExplicitOnDevice()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        AssertEqual(
            SpeechProviderLocation.OnDevice,
            provider.Descriptor.Location,
            "provider location");
        AssertEqual(
            SpeechNetworkRequirement.None,
            provider.Descriptor.NetworkRequirement,
            "network requirement");
        Assert(
            !provider.Descriptor.MayUseNetwork,
            "explicit on-device provider must not advertise network use");
        AssertEqual(
            AndroidOnDeviceAsrProvider.ProviderId,
            provider.Descriptor.ProviderId,
            "provider id");
    }

    private static async Task Api30Unavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(30, true, false, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "API30 availability");
        AssertEqual(0, platform.SupportCheckCount, "support checks");
        AssertEqual(0, platform.RecognitionStartCount, "recognizer starts");
    }

    private static async Task Api31RecognizerUnavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(31, true, false, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "API31 unavailable recognizer");
        AssertEqual(0, platform.SupportCheckCount, "support checks");
    }

    private static async Task PermissionRequired()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(33, false, true, true),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.PermissionRequired,
            availability.State,
            "permission state");
        AssertEqual(0, platform.SupportCheckCount, "support checks before permission");

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            events,
            SpeechErrorCategory.Permission,
            "android_on_device_asr_microphone_permission_required");
        AssertEqual(0, platform.RecognitionStartCount, "recognizer starts before permission");
    }

    private static async Task Api31PreflightUnavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(31, true, true, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Available,
            availability.State,
            "API31 explicit recognizer");
        Assert(
            availability.Diagnostic.Contains(
                "cannot preflight",
                StringComparison.OrdinalIgnoreCase),
            "API31 availability must disclose the missing language preflight");
        AssertEqual(0, platform.SupportCheckCount, "support preflight calls");
    }

    private static async Task InstalledLanguageAvailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(SpeechAvailabilityState.Available, availability.State, "availability");
        AssertEqual(1, platform.SupportCheckCount, "support checks");
    }

    private static async Task ModelDownloadRequired()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.ModelDownloadRequired,
                "model must be installed"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.SetupRequired,
            availability.State,
            "setup state");
    }

    private static async Task ModelDownloadPending()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.ModelDownloadPending,
                "model download pending"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.SetupRequired,
            availability.State,
            "pending model state");
    }

    private static async Task UnsupportedLanguage()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.UnsupportedLanguage,
                "unsupported language"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "unsupported language state");
    }

    private static async Task PreflightUnavailableStillAvailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.PreflightUnavailable,
                "cannot check support"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Available,
            availability.State,
            "preflight unavailable state");
        Assert(
            availability.Diagnostic.Contains(
                "no fallback",
                StringComparison.OrdinalIgnoreCase),
            "availability must retain the no-fallback boundary");
    }

    private static async Task SupportFaulted()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.Faulted,
                "support service faulted"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Faulted,
            availability.State,
            "support fault");
    }

    private static async Task ConfiguredLanguageEnforced()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                new AsrOptions("fr-FR", true),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "configured language");
        AssertEqual(0, platform.ProbeCount, "probe count for mismatched language");
        AssertEqual(0, platform.RecognitionStartCount, "start count");
    }
}
