#nullable enable

using System;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        private static void ProviderKindsAndCapabilitiesRemainExplicit()
        {
            ProviderDescriptor source = Descriptor(
                VisionProviderKind.FrameSource,
                "source-1",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor tracker = Descriptor(
                VisionProviderKind.LightweightTracker,
                "tracker-1",
                VisionProviderLocation.OnDevice);
            ProviderDescriptor vlm = Descriptor(
                VisionProviderKind.SemanticVisionLanguage,
                "vlm-1",
                VisionProviderLocation.Cloud);

            Equal(VisionProviderKind.FrameSource, source.Kind, "source kind");
            Equal(
                VisionProviderKind.LightweightTracker,
                tracker.Kind,
                "tracker kind");
            Equal(
                VisionProviderKind.SemanticVisionLanguage,
                vlm.Kind,
                "VLM kind");
            True(vlm.RequiresNetworkDisclosure, "cloud disclosure");
            False(source.RequiresNetworkDisclosure, "on-device disclosure");

            _ = new FrameSourceCapabilities(
                supportsGpuColor: true,
                supportsGpuValidityMask: true,
                supportsCancellation: true,
                maximumWidth: 4096,
                maximumHeight: 4096,
                maximumOutstandingFrames: 2);
            _ = TrackerCapability();
            _ = VisionLanguageCapability();

            Throws<ArgumentException>(
                () =>
                {
                    _ = new TrackerCapabilities(
                        supportsFaces: false,
                        supportsPeople: false,
                        supportsObjects: false,
                        supportsMotion: false,
                        consumesGpuFrames: true,
                        supportsCancellation: true,
                        maximumConcurrentOperations: 1);
                },
                "empty tracker capability set");
            Throws<ArgumentException>(
                () =>
                {
                    _ = new FrameSourceCapabilities(
                        supportsGpuColor: true,
                        supportsGpuValidityMask: false,
                        supportsCancellation: true,
                        maximumWidth: 640,
                        maximumHeight: 480,
                        maximumOutstandingFrames: 1);
                },
                "missing validity-mask capability");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new VisionRequestContext(
                        "bad-timeout",
                        new VisionProviderSelection(tracker).Current,
                        TimeSpan.Zero);
                },
                "zero timeout");
        }

        private static async Task
            TransformedFramesRequireOwnedColorValidityAndCoverageAsync()
        {
            await using var resources = new FakeResources(
                width: 10,
                height: 10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 1UL);
            True(frame.IsObservationEligible, "transformed frame eligibility");
            Equal(0.8, frame.Coverage.Fraction, "coverage fraction");
            True(
                frame.Resources.HasResource(VisionResourceKind.Color),
                "color resource");
            True(
                frame.Resources.HasResource(VisionResourceKind.ValidityMask),
                "validity resource");

            await using var invalidResources = new FakeResources(
                10,
                10,
                hasValidity: false);
            Throws<ArgumentException>(
                () =>
                {
                    _ = Frame(
                        invalidResources,
                        VisionFrameOrigin.TransformedReachyEye,
                        VisionCoverageState.Normal,
                        sourceSequence: 2UL);
                },
                "transformed frame without validity resource");
            Throws<ArgumentException>(
                () =>
                {
                    _ = new ReachyVisionCoverage(
                        VisionCoverageState.Unusable,
                        10L,
                        100L,
                        hasValidityMask: true,
                        shouldStopVisionDrivenTurning: false,
                        diagnostic: "unsafe unusable coverage");
                },
                "unusable coverage without turning stop");
        }

        private static async Task FrameResourcesDisposeExactlyOnceAsync()
        {
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 16UL);
            await frame.DisposeAsync().ConfigureAwait(false);
            await frame.DisposeAsync().ConfigureAwait(false);
            Equal(1, resources.DisposeCount, "resource dispose count");
            True(frame.IsDisposed, "frame disposed state");
            False(frame.IsObservationEligible, "disposed frame eligibility");
        }
    }
}
