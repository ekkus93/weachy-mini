#nullable enable

using System;
using ReachyMini.LocalModels;
using ReachyMini.Performance;

namespace ReachyMini.Core.Tests
{
    internal static class Rma184RepresentativeDeviceMatrixContractTests
    {
        public static void RunAll()
        {
            PublishedProfilesAreDeterministic();
            SupportPolicySeparatesCoreAndFullOfflineSupport();
            LongRunQualificationAcceptsBoundedRepresentativeEvidence();
            LongRunQualificationRejectsAccumulatingPressure();
        }

        private static void PublishedProfilesAreDeterministic()
        {
            ReachyRepresentativeDeviceProfile low =
                ReachyRepresentativeDeviceProfile.ForClass(
                    ReachyAndroidPerformanceClass.Low);
            ReachyRepresentativeDeviceProfile mid =
                ReachyRepresentativeDeviceProfile.ForClass(
                    ReachyAndroidPerformanceClass.Mid);
            ReachyRepresentativeDeviceProfile high =
                ReachyRepresentativeDeviceProfile.ForClass(
                    ReachyAndroidPerformanceClass.High);

            Equal(30, low.TargetFramesPerSecond, "low FPS");
            Equal(LocalLlmDeviceProfileKind.Conservative, low.LocalLlmProfile, "low LLM");
            Equal(30, mid.TargetFramesPerSecond, "mid FPS");
            Equal(LocalLlmDeviceProfileKind.Balanced, mid.LocalLlmProfile, "mid LLM");
            Equal(60, high.TargetFramesPerSecond, "high FPS");
            Equal(LocalLlmDeviceProfileKind.Performance, high.LocalLlmProfile, "high LLM");
            Equal(1800, ReachyRepresentativeDeviceProfile.MinimumLongRunSeconds, "long run");
            Near(2.0, ReachyRepresentativeDeviceProfile.MaximumPhysicsP95Milliseconds, "physics p95");
            Near(1.0, ReachyRepresentativeDeviceProfile.MinimumLocalLlmDecodeTokensPerSecond, "decode floor");
        }

        private static void SupportPolicySeparatesCoreAndFullOfflineSupport()
        {
            ReachyDeviceSupportAssessment legacy =
                ReachyRepresentativeDeviceSupportPolicy.Evaluate(
                    Capabilities(
                        api: 26,
                        asr: ReachyOfflineSpeechCapability.Unavailable,
                        tts: ReachyOfflineSpeechCapability.Unknown));
            Equal(
                ReachyDeviceSupportStatus.SupportedWithLimitations,
                legacy.Status,
                "API-26 core support");
            Equal(true, legacy.CoreRuntimeSupported, "legacy core supported");
            Equal(false, legacy.FullOfflineInteractionSupported, "legacy offline limited");

            ReachyDeviceSupportAssessment modern =
                ReachyRepresentativeDeviceSupportPolicy.Evaluate(
                    Capabilities(
                        api: 35,
                        asr: ReachyOfflineSpeechCapability.Available,
                        tts: ReachyOfflineSpeechCapability.Available));
            Equal(ReachyDeviceSupportStatus.Supported, modern.Status, "modern support");
            Equal(true, modern.FullOfflineInteractionSupported, "modern offline support");

            ReachyDeviceSupportAssessment unsupported =
                ReachyRepresentativeDeviceSupportPolicy.Evaluate(
                    new ReachyRepresentativeDeviceCapabilities(
                        androidApiLevel: 25,
                        totalMemoryBytes: 2L * 1024L * 1024L * 1024L,
                        logicalProcessorCount: 2,
                        graphicsApi: "OpenGLES2",
                        rearCameraAvailable: false,
                        frontCameraAvailable: false,
                        onDeviceAsr: ReachyOfflineSpeechCapability.Unavailable,
                        offlineTts: ReachyOfflineSpeechCapability.Unavailable));
            Equal(ReachyDeviceSupportStatus.Unsupported, unsupported.Status, "unsupported floor");
        }

        private static void LongRunQualificationAcceptsBoundedRepresentativeEvidence()
        {
            ReachyRepresentativeDeviceQualificationResult result =
                ReachyRepresentativeDeviceQualificationPolicy.Evaluate(
                    ReachyAndroidPerformanceClass.High,
                    new ReachyRepresentativeDeviceQualificationObservation(
                        durationSeconds: 1800.0,
                        renderP95Milliseconds: 18.0,
                        physicsP95Milliseconds: 1.5,
                        initialUnityAllocatedMemoryBytes: 700L * 1024L * 1024L,
                        finalUnityAllocatedMemoryBytes: 760L * 1024L * 1024L,
                        initialStateLagSeconds: 0.001,
                        finalStateLagSeconds: 0.0015,
                        localLlmDecodeTokensPerSecond: 2.0,
                        thermalDegradationOrderValid: true));
            Equal(true, result.Passed, "bounded high-device evidence");
        }

        private static void LongRunQualificationRejectsAccumulatingPressure()
        {
            ReachyRepresentativeDeviceQualificationResult result =
                ReachyRepresentativeDeviceQualificationPolicy.Evaluate(
                    ReachyAndroidPerformanceClass.Low,
                    new ReachyRepresentativeDeviceQualificationObservation(
                        durationSeconds: 1200.0,
                        renderP95Milliseconds: 50.0,
                        physicsP95Milliseconds: 3.0,
                        initialUnityAllocatedMemoryBytes: 400L * 1024L * 1024L,
                        finalUnityAllocatedMemoryBytes: 700L * 1024L * 1024L,
                        initialStateLagSeconds: 0.0,
                        finalStateLagSeconds: 0.010,
                        localLlmDecodeTokensPerSecond: 0.5,
                        thermalDegradationOrderValid: false));
            Equal(false, result.Passed, "accumulating low-device evidence");
            Equal(7, result.Failures.Count, "all qualification failures visible");
        }

        private static ReachyRepresentativeDeviceCapabilities Capabilities(
            int api,
            ReachyOfflineSpeechCapability asr,
            ReachyOfflineSpeechCapability tts) =>
            new ReachyRepresentativeDeviceCapabilities(
                api,
                totalMemoryBytes: 8L * 1024L * 1024L * 1024L,
                logicalProcessorCount: 8,
                graphicsApi: "Vulkan",
                rearCameraAvailable: true,
                frontCameraAvailable: true,
                onDeviceAsr: asr,
                offlineTts: tts);

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-184 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void Near(double expected, double actual, string description)
        {
            if (Math.Abs(expected - actual) > 1.0e-9)
            {
                throw new InvalidOperationException(
                    $"RMA-184 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }
    }
}
