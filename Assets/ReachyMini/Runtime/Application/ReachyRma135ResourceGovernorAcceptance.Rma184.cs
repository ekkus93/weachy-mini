#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using ReachyMini.Performance;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma135ResourceGovernorAcceptance
    {
        internal const string Rma184LongRunSecondsExtraName =
            "reachy_rma184_long_run_seconds";
        internal const string Rma184PerformanceClassExtraName =
            "reachy_rma184_performance_class";
        internal const string Rma184LongRunResultFileName =
            "rma184-representative-device-long-run.json";

        private const int Rma184MaximumLongRunSeconds = 3600;
        private static readonly TimeSpan Rma184DecisionSampleInterval =
            TimeSpan.FromSeconds(2.0);
        private static readonly TimeSpan Rma184GenerationInterval =
            TimeSpan.FromMinutes(3.0);
        private static readonly TimeSpan Rma184GenerationTimeout =
            TimeSpan.FromMinutes(3.0);

        private static async Task RunRma184LongRunIfRequestedAsync(
            ReachyProductionAuthoritativeRuntime productionRuntime,
            LocalLlmGovernedGenerationCoordinator coordinator)
        {
            int requestedSeconds = ReadIntegerLaunchExtra(
                Rma184LongRunSecondsExtraName,
                0);
            if (requestedSeconds == 0)
            {
                return;
            }
            if (requestedSeconds <
                    ReachyRepresentativeDeviceProfile.MinimumLongRunSeconds ||
                requestedSeconds > Rma184MaximumLongRunSeconds)
            {
                throw new InvalidOperationException(
                    "RMA-184 long-run duration must be between " +
                    ReachyRepresentativeDeviceProfile.MinimumLongRunSeconds.ToString(
                        CultureInfo.InvariantCulture) +
                    " and " +
                    Rma184MaximumLongRunSeconds.ToString(
                        CultureInfo.InvariantCulture) +
                    " seconds.");
            }

            int classValue = ReadIntegerLaunchExtra(
                Rma184PerformanceClassExtraName,
                (int)ReachyAndroidPerformanceClass.Mid);
            if (!Enum.IsDefined(
                    typeof(ReachyAndroidPerformanceClass),
                    classValue))
            {
                throw new InvalidOperationException(
                    "RMA-184 performance-class launch extra is invalid.");
            }
            ReachyAndroidPerformanceClass performanceClass =
                (ReachyAndroidPerformanceClass)classValue;
            ReachyRepresentativeDeviceProfile profile =
                ReachyRepresentativeDeviceProfile.ForClass(performanceClass);
            string resultPath = Path.Combine(
                UnityEngine.Application.persistentDataPath,
                Rma184LongRunResultFileName);
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            ReachyRuntimeThermalPolicy.FramePacingSnapshot originalPacing =
                ReachyRuntimeThermalPolicy.CaptureCurrentPacing();
            ReachyPerformanceSession? session = null;
            try
            {
                ReachyRuntimeThermalPolicy.Apply(
                    profile.TargetFramesPerSecond);
                ReachySimulationTimingSnapshot initialTiming =
                    await WaitForRma184TimingSnapshotAsync(
                        productionRuntime).ConfigureAwait(true);
                ReachyPriorityDegradationRuntimeDriver degradation =
                    await WaitForRma184PriorityDriverAsync().ConfigureAwait(true);

                session = ReachyPerformanceTelemetry.StartSession(
                    profile.TargetFramesPerSecond,
                    "rma184-" +
                    performanceClass.ToString().ToLowerInvariant() +
                    "-long-run");

                double deadline =
                    Time.realtimeSinceStartupAsDouble +
                    requestedSeconds +
                    1.0;
                double nextGeneration =
                    Time.realtimeSinceStartupAsDouble;
                int decisionSamples = 0;
                int thermalPressureSamples = 0;
                int nonNominalDecisionSamples = 0;
                int successfulGenerations = 0;
                int suspendedGenerations = 0;
                double minimumDecodeTokensPerSecond =
                    double.PositiveInfinity;
                double maximumDecodeTokensPerSecond = 0.0;
                ReachyPriorityDegradationLevel maximumObservedLevel =
                    ReachyPriorityDegradationLevel.Nominal;
                bool thermalOrderValid = true;

                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    ReachyPriorityDegradationDecision? decision =
                        degradation.CurrentDecision;
                    ReachyPriorityDegradationSignals? sampledSignals =
                        degradation.CurrentSignals;
                    if (decision != null && sampledSignals != null)
                    {
                        ++decisionSamples;
                        if (decision.Level > maximumObservedLevel)
                        {
                            maximumObservedLevel = decision.Level;
                        }
                        if (decision.Level !=
                            ReachyPriorityDegradationLevel.Nominal)
                        {
                            ++nonNominalDecisionSamples;
                        }
                        thermalOrderValid &=
                            ValidateRma184Decision(decision);

                        LocalLlmThermalStatus thermal =
                            sampledSignals.ThermalStatus;
                        if (IsRma184ThermalPressure(thermal))
                        {
                            ++thermalPressureSamples;
                            if (!DecisionMatchesThermalPressure(
                                    decision,
                                    thermal))
                            {
                                thermalOrderValid = false;
                            }
                        }
                    }

                    double now =
                        Time.realtimeSinceStartupAsDouble;
                    if (now >= nextGeneration)
                    {
                        nextGeneration =
                            now +
                            Rma184GenerationInterval.TotalSeconds;
                        var sink =
                            new Rma135CollectingSink();
                        using var timeout =
                            new CancellationTokenSource(
                                Rma184GenerationTimeout);
                        LocalLlmGovernedGenerationResult generation =
                            await coordinator.GenerateAsync(
                                CreateRequest(
                                    "rma184-long-run-" +
                                    (successfulGenerations +
                                     suspendedGenerations + 1)
                                        .ToString(
                                            CultureInfo.InvariantCulture),
                                    SuccessPrompt),
                                sink,
                                timeout.Token).ConfigureAwait(true);
                        if (generation.Succeeded)
                        {
                            ValidateSuccessfulGeneration(
                                generation,
                                sink,
                                "RMA-184 governed long-run generation");
                            ++successfulGenerations;
                            double decodeRate =
                                DecodeTokensPerSecond(
                                    generation.ProviderResult?.Metrics);
                            if (decodeRate > 0.0)
                            {
                                minimumDecodeTokensPerSecond =
                                    Math.Min(
                                        minimumDecodeTokensPerSecond,
                                        decodeRate);
                                maximumDecodeTokensPerSecond =
                                    Math.Max(
                                        maximumDecodeTokensPerSecond,
                                        decodeRate);
                            }
                        }
                        else if (
                            generation.Status ==
                                LocalLlmGovernedGenerationStatus
                                    .ResourceSuspendedBeforeStart ||
                            generation.Status ==
                                LocalLlmGovernedGenerationStatus
                                    .ResourceCancelledDuringGeneration)
                        {
                            ++suspendedGenerations;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "RMA-184 governed long-run " +
                                "generation failed: " +
                                generation.Status +
                                "; " +
                                generation.Detail);
                        }
                    }

                    double remaining =
                        deadline -
                        Time.realtimeSinceStartupAsDouble;
                    if (remaining > 0.0)
                    {
                        await Task.Delay(
                            remaining <
                                Rma184DecisionSampleInterval
                                    .TotalSeconds
                                ? TimeSpan.FromSeconds(remaining)
                                : Rma184DecisionSampleInterval)
                            .ConfigureAwait(true);
                    }
                }

                ReachyPerformanceReport performance =
                    session.Complete();
                session = null;
                ReachySimulationTimingSnapshot finalTiming =
                    await WaitForRma184TimingSnapshotAsync(
                        productionRuntime).ConfigureAwait(true);
                ReachyPerformanceTimingSummary rendering =
                    performance.FindTiming(
                        ReachyPerformanceWorkload.UnityRendering);
                ReachyPerformanceTimingSummary physics =
                    performance.FindTiming(
                        ReachyPerformanceWorkload.NativePhysics);

                long initialMemory =
                    performance.Resources
                        .InitialUnityAllocatedMemoryBytes ??
                    0L;
                long finalMemory =
                    performance.Resources
                        .FinalUnityAllocatedMemoryBytes ??
                    0L;
                double decodeFloor =
                    double.IsPositiveInfinity(
                        minimumDecodeTokensPerSecond)
                        ? 0.0
                        : minimumDecodeTokensPerSecond;
                bool observedThermalDegradation =
                    thermalPressureSamples > 0 &&
                    nonNominalDecisionSamples > 0;
                bool physicalThermalOrderValid =
                    thermalOrderValid &&
                    decisionSamples > 0 &&
                    observedThermalDegradation;

                var observation =
                    new ReachyRepresentativeDeviceQualificationObservation(
                        performance.DurationSeconds,
                        rendering.P95Milliseconds,
                        physics.P95Milliseconds,
                        initialMemory,
                        finalMemory,
                        initialTiming.AccumulatedLagSeconds,
                        finalTiming.AccumulatedLagSeconds,
                        decodeFloor,
                        physicalThermalOrderValid);
                ReachyRepresentativeDeviceQualificationResult
                    qualification =
                        ReachyRepresentativeDeviceQualificationPolicy
                            .Evaluate(
                                performanceClass,
                                observation);

                var report = new Rma184LongRunReport
                {
                    schema_version = 1,
                    status =
                        qualification.Passed
                            ? "passed"
                            : "failed",
                    device_model =
                        SystemInfo.deviceModel,
                    operating_system =
                        SystemInfo.operatingSystem,
                    performance_class =
                        performanceClass.ToString(),
                    target_fps =
                        profile.TargetFramesPerSecond,
                    requested_duration_seconds =
                        requestedSeconds,
                    measured_duration_seconds =
                        performance.DurationSeconds,
                    render_p95_ms =
                        rendering.P95Milliseconds,
                    physics_p95_ms =
                        physics.P95Milliseconds,
                    initial_unity_allocated_memory_bytes =
                        initialMemory,
                    final_unity_allocated_memory_bytes =
                        finalMemory,
                    memory_growth_bytes =
                        Math.Max(
                            0L,
                            finalMemory - initialMemory),
                    initial_state_lag_seconds =
                        initialTiming.AccumulatedLagSeconds,
                    final_state_lag_seconds =
                        finalTiming.AccumulatedLagSeconds,
                    state_lag_growth_seconds =
                        Math.Max(
                            0.0,
                            finalTiming.AccumulatedLagSeconds -
                            initialTiming.AccumulatedLagSeconds),
                    successful_local_llm_generations =
                        successfulGenerations,
                    suspended_local_llm_generations =
                        suspendedGenerations,
                    minimum_local_llm_decode_tokens_per_second =
                        decodeFloor,
                    maximum_local_llm_decode_tokens_per_second =
                        maximumDecodeTokensPerSecond,
                    decision_samples =
                        decisionSamples,
                    thermal_pressure_samples =
                        thermalPressureSamples,
                    non_nominal_decision_samples =
                        nonNominalDecisionSamples,
                    maximum_observed_degradation_level =
                        maximumObservedLevel.ToString(),
                    thermal_degradation_order_valid =
                        physicalThermalOrderValid,
                    peak_thermal_state =
                        performance.Resources.PeakThermalState,
                    qualification_passed =
                        qualification.Passed,
                    qualification_failures =
                        string.Join(
                            ",",
                            qualification.Failures),
                    report_contains_prompt_or_response_content =
                        false,
                };
                WriteRma184Report(
                    resultPath,
                    report);

                if (!qualification.Passed)
                {
                    throw new InvalidOperationException(
                        "RMA-184 representative-device " +
                        "long-run qualification failed: " +
                        report.qualification_failures);
                }
                if (successfulGenerations <= 0)
                {
                    throw new InvalidOperationException(
                        "RMA-184 long-run recorded no " +
                        "successful governed local-LLM generation.");
                }

                WriteCheckpoint(
                    "rma184_long_run_passed",
                    "duration_s=" +
                    performance.DurationSeconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture) +
                    " render_p95_ms=" +
                    rendering.P95Milliseconds.ToString(
                        "F3",
                        CultureInfo.InvariantCulture) +
                    " physics_p95_ms=" +
                    physics.P95Milliseconds.ToString(
                        "F3",
                        CultureInfo.InvariantCulture) +
                    " decode_floor_tps=" +
                    decodeFloor.ToString(
                        "F3",
                        CultureInfo.InvariantCulture) +
                    " max_degradation=" +
                    maximumObservedLevel);
            }
            catch (Exception exception)
            {
                TryWriteRma184Failure(
                    resultPath,
                    exception);
                throw;
            }
            finally
            {
                session?.Dispose();
                ReachyRuntimeThermalPolicy.Restore(
                    originalPacing);
            }
        }

        private static async Task<
            ReachySimulationTimingSnapshot>
            WaitForRma184TimingSnapshotAsync(
                ReachyProductionAuthoritativeRuntime runtime)
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                if (runtime.Status ==
                        ReachyProductionRuntimeStatus.Faulted ||
                    runtime.SimulationRunState ==
                        ReachySimulationRunState.Faulted)
                {
                    throw new InvalidOperationException(
                        "Production simulation faulted during " +
                        "RMA-184 long-run qualification.");
                }
                if (runtime.TryGetLatestTimingSnapshot(
                        out ReachySimulationTimingSnapshot timing) &&
                    timing.TotalStepCount > 0UL)
                {
                    return timing;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for production simulation " +
                "timing during RMA-184 qualification.");
        }

        private static async Task<
            ReachyPriorityDegradationRuntimeDriver>
            WaitForRma184PriorityDriverAsync()
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                ReachyPriorityDegradationRuntimeDriver? driver =
                    UnityEngine.Object.FindAnyObjectByType<
                        ReachyPriorityDegradationRuntimeDriver>();
                if (driver?.CurrentDecision != null &&
                    driver.CurrentSignals != null)
                {
                    return driver;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for the shipping RMA-181 " +
                "priority-degradation driver.");
        }

        private static bool IsRma184ThermalPressure(
            LocalLlmThermalStatus thermal)
        {
            switch (thermal)
            {
                case LocalLlmThermalStatus.Light:
                case LocalLlmThermalStatus.Moderate:
                case LocalLlmThermalStatus.Severe:
                case LocalLlmThermalStatus.Critical:
                case LocalLlmThermalStatus.Emergency:
                case LocalLlmThermalStatus.Shutdown:
                    return true;
                case LocalLlmThermalStatus.Unavailable:
                case LocalLlmThermalStatus.None:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(thermal));
            }
        }

        private static bool ValidateRma184Decision(
            ReachyPriorityDegradationDecision decision)
        {
            if (Math.Abs(
                    decision.PhysicsTimestepSeconds -
                    ReachyMini.Core.ProjectMetadata
                        .InitialPhysicsTimestepSeconds) >
                    1.0e-12 ||
                decision.PhysicsStepSkippingAllowed ||
                !decision.AudioInteractionPreserved)
            {
                return false;
            }

            switch (decision.Level)
            {
                case ReachyPriorityDegradationLevel.Nominal:
                    return
                        decision.ExpensiveVisualEffectsAllowed &&
                        decision.VlmAllowed &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Nominal;
                case ReachyPriorityDegradationLevel.RenderReduced:
                    return
                        !decision.ExpensiveVisualEffectsAllowed &&
                        decision.VlmAllowed &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Nominal;
                case ReachyPriorityDegradationLevel.CameraReduced:
                    return
                        !decision.ExpensiveVisualEffectsAllowed &&
                        decision.TrackingMaximumDimension <= 480 &&
                        decision.VlmAllowed &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Nominal;
                case ReachyPriorityDegradationLevel.VlmSuspended:
                    return
                        !decision.VlmAllowed &&
                        decision.TrackingMaximumDimension <= 320 &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Nominal;
                case ReachyPriorityDegradationLevel.LlmReduced:
                    return
                        !decision.VlmAllowed &&
                        decision.TrackingMaximumDimension <= 256 &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Minimal;
                case ReachyPriorityDegradationLevel.Critical:
                    return
                        !decision.VlmAllowed &&
                        decision.TrackingMaximumDimension <= 192 &&
                        decision.MinimumLocalLlmMode ==
                            LocalLlmGovernorMode.Suspended &&
                        decision.TargetRenderFps <= 15;
                default:
                    return false;
            }
        }

        private static bool DecisionMatchesThermalPressure(
            ReachyPriorityDegradationDecision decision,
            LocalLlmThermalStatus thermal)
        {
            switch (thermal)
            {
                case LocalLlmThermalStatus.Light:
                    return
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .RenderReduced ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .CameraReduced ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .VlmSuspended ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .LlmReduced ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .Critical;
                case LocalLlmThermalStatus.Moderate:
                    return
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .VlmSuspended ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .LlmReduced ||
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .Critical;
                case LocalLlmThermalStatus.Severe:
                case LocalLlmThermalStatus.Critical:
                case LocalLlmThermalStatus.Emergency:
                case LocalLlmThermalStatus.Shutdown:
                    return
                        decision.Level ==
                            ReachyPriorityDegradationLevel
                                .Critical;
                case LocalLlmThermalStatus.Unavailable:
                case LocalLlmThermalStatus.None:
                    return true;
                default:
                    return false;
            }
        }

        private static double DecodeTokensPerSecond(
            LocalLlmGenerationMetrics? metrics)
        {
            if (metrics == null ||
                metrics.GeneratedTokens == 0UL ||
                metrics.FinishedMonotonicMicroseconds <=
                    metrics.StartedMonotonicMicroseconds)
            {
                return 0.0;
            }

            double seconds =
                (metrics.FinishedMonotonicMicroseconds -
                 metrics.StartedMonotonicMicroseconds) /
                1000000.0;
            return seconds > 0.0
                ? metrics.GeneratedTokens / seconds
                : 0.0;
        }

        private static int ReadIntegerLaunchExtra(
            string name,
            int defaultValue)
        {
            using var unityPlayer =
                new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");
            using AndroidJavaObject intent =
                activity.Call<AndroidJavaObject>(
                    "getIntent");
            return intent.Call<int>(
                "getIntExtra",
                name,
                defaultValue);
        }

        private static void WriteRma184Report(
            string path,
            Rma184LongRunReport report)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                JsonUtility.ToJson(report, true) + "\n",
                new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static void TryWriteRma184Failure(
            string path,
            Exception exception)
        {
            try
            {
                WriteRma184Report(
                    path,
                    new Rma184LongRunReport
                    {
                        schema_version = 1,
                        status = "failed",
                        device_model =
                            SystemInfo.deviceModel,
                        operating_system =
                            SystemInfo.operatingSystem,
                        qualification_passed =
                            false,
                        qualification_failures =
                            Bound(
                                exception.GetType().Name +
                                ": " +
                                exception.Message,
                                1024),
                        report_contains_prompt_or_response_content =
                            false,
                    });
            }
            catch (Exception writeException)
            {
                Debug.LogError(
                    "RMA-184 long-run failure evidence " +
                    "could not be written: " +
                    Bound(
                        writeException.Message,
                        1024));
            }
        }
    }

    [Serializable]
    internal sealed class Rma184LongRunReport
    {
        public int schema_version;
        public string status = string.Empty;
        public string device_model = string.Empty;
        public string operating_system = string.Empty;
        public string performance_class = string.Empty;
        public int target_fps;
        public int requested_duration_seconds;
        public double measured_duration_seconds;
        public double render_p95_ms;
        public double physics_p95_ms;
        public long initial_unity_allocated_memory_bytes;
        public long final_unity_allocated_memory_bytes;
        public long memory_growth_bytes;
        public double initial_state_lag_seconds;
        public double final_state_lag_seconds;
        public double state_lag_growth_seconds;
        public int successful_local_llm_generations;
        public int suspended_local_llm_generations;
        public double
            minimum_local_llm_decode_tokens_per_second;
        public double
            maximum_local_llm_decode_tokens_per_second;
        public int decision_samples;
        public int thermal_pressure_samples;
        public int non_nominal_decision_samples;
        public string
            maximum_observed_degradation_level =
                string.Empty;
        public bool thermal_degradation_order_valid;
        public string peak_thermal_state = string.Empty;
        public bool qualification_passed;
        public string qualification_failures =
            string.Empty;
        public bool
            report_contains_prompt_or_response_content;
    }
}
