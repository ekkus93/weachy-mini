#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma135ResourceGovernorAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName = "reachy_rma135_resource_governor_acceptance";
        internal const string ResultFileName = "rma135-resource-governor-acceptance.json";
        internal const string ModelFileName = "rma135-qwen3-0.6b-q4_k_m.gguf";
        internal const string CheckpointFilePrefix = "rma135-resource-governor-checkpoint-";

        private const string SuccessPrompt =
            "The person says hello and asks you to greet them warmly. No gaze target is requested.";
        private static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(180.0);
        private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(25.0);

        // Recovery from the controlled fault injection requires
        // LocalLlmResourceGovernor.RecoverySamplesRequired (3) *consecutive* non-Suspended
        // real observations. Physical evidence on the LG-H872 boundary device (see
        // docs/RMA_135_RESOURCE_THERMAL_GOVERNOR_SPEC_2026-08-10.md section 8) shows a
        // genuine PhysicsBudgetExceeded reading after a governed cancellation can stay real
        // and sustained for several real seconds on this low-end hardware before the
        // envelope settles -- this is expected latency under concurrent authoritative
        // physics + local-LLM load, not an intermittent blip and not a defect. This budget
        // gives real recovery that much wall-clock time/attempts; it does not change
        // governor hysteresis itself, and a run that still exhausts it reflects a real,
        // sustained device condition rather than a harness timing artifact.
        private const int RecoveryObservationBudget = 40;
        private static readonly TimeSpan RecoveryObservationInterval = TimeSpan.FromMilliseconds(75.0);

        private static string bootstrapError = string.Empty;
        private static bool unhandledFailure;
        private static string unhandledFailureMessage = string.Empty;
        private static int checkpointSequence;
        private static Stopwatch? checkpointStopwatch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (UnityEngine.Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            bool requested;
            try
            {
                requested = ReadBooleanLaunchExtra(LaunchExtraName, false);
            }
            catch (Exception exception)
            {
                requested = true;
                bootstrapError = "Failed to read the RMA-135 launch extra: " + exception.Message;
            }
            if (!requested)
            {
                return;
            }

            try
            {
                InitializeCheckpointRun();
                WriteCheckpoint("bootstrap_started", "RMA-135 launch extra accepted.");
            }
            catch (Exception exception)
            {
                bootstrapError = "Failed to initialize RMA-135 checkpoints: " + exception.Message;
                Debug.LogError(bootstrapError);
            }

            GameObject host = new GameObject("ReachyRma135ResourceGovernorAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma135ResourceGovernorAcceptance>();
            TryWriteCheckpoint("bootstrap_component_installed", "Acceptance MonoBehaviour installed.");
        }

        private async void Start()
        {
            UnityEngine.Application.logMessageReceivedThreaded += HandleLogMessage;
            string resultPath = Path.Combine(UnityEngine.Application.persistentDataPath, ResultFileName);
            try
            {
                WriteCheckpoint("start_entered", "RMA-135 physical acceptance Start entered.");
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                if (!string.IsNullOrEmpty(bootstrapError))
                {
                    throw new InvalidOperationException(bootstrapError);
                }

                Rma135AcceptanceReport report = await RunAcceptanceAsync().ConfigureAwait(true);
                if (unhandledFailure)
                {
                    throw new InvalidOperationException(
                        "An unhandled Unity exception occurred during RMA-135 acceptance: " +
                        unhandledFailureMessage);
                }
                report.status = "passed";
                WriteReport(resultPath, report);
                WriteCheckpoint("passed", "Final RMA-135 acceptance report committed.");
                Debug.Log("RMA-135 resource governor physical acceptance passed.");
            }
            catch (Exception exception)
            {
                TryWriteCheckpoint("failed", Bound(exception.Message, 1024));
                Rma135AcceptanceReport failure = new Rma135AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                    android_api_level = TryReadApiLevel(),
                };
                try
                {
                    WriteReport(resultPath, failure);
                }
                catch (Exception reportException)
                {
                    Debug.LogError("RMA-135 could not write its failure report: " + reportException);
                }
                Debug.LogError("RMA-135 resource governor physical acceptance failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceivedThreaded -= HandleLogMessage;
            }
        }
    }
}
