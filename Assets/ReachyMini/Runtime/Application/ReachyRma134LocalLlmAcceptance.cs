#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma134LocalLlmAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName = "reachy_rma134_acceptance";
        internal const string ResultFileName = "rma134-local-llm-acceptance.json";
        internal const string ModelFileName = "rma134-qwen3-0.6b-q4_k_m.gguf";
        internal const string CheckpointFilePrefix = "rma134-local-llm-checkpoint-";

        private const string SimulationModelResourcePath = "ReachyMiniRuntime/reachy_mini_mjb";
        private const double PhysicsStepSeconds = 0.002;
        private const double PhysicsP95BudgetMicroseconds = 2000.0;
        private const int MinimumConcurrentPhysicsSteps = 250;
        private const int MaximumConcurrentPhysicsSteps = 10000;
        private const string SuccessPrompt =
            "The person says hello and asks you to greet them warmly. No gaze target is requested.";

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
                bootstrapError = "Failed to read the RMA-134 launch extra: " + exception.Message;
            }
            if (!requested)
            {
                return;
            }

            try
            {
                InitializeCheckpointRun();
                WriteCheckpoint("bootstrap_started", "RMA-134 launch extra accepted.");
            }
            catch (Exception exception)
            {
                bootstrapError = "Failed to initialize RMA-134 checkpoints: " + exception.Message;
                Debug.LogError(bootstrapError);
            }

            GameObject host = new GameObject("ReachyRma134LocalLlmAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma134LocalLlmAcceptance>();
            TryWriteCheckpoint("bootstrap_component_installed", "Acceptance MonoBehaviour installed.");
        }

        private async void Start()
        {
            UnityEngine.Application.logMessageReceivedThreaded += HandleLogMessage;
            string resultPath = Path.Combine(UnityEngine.Application.persistentDataPath, ResultFileName);
            try
            {
                WriteCheckpoint("start_entered", "Acceptance Start entered.");
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                if (!string.IsNullOrEmpty(bootstrapError))
                {
                    throw new InvalidOperationException(bootstrapError);
                }
                Rma134AcceptanceReport report = await RunAcceptanceAsync().ConfigureAwait(true);
                if (unhandledFailure)
                {
                    throw new InvalidOperationException(
                        "An unhandled Unity exception occurred during RMA-134 acceptance: " + unhandledFailureMessage);
                }
                report.status = "passed";
                WriteReport(resultPath, report);
                WriteCheckpoint("passed", "Final acceptance report committed.");
                Debug.Log("RMA-134 local LLM physical acceptance passed.");
            }
            catch (Exception exception)
            {
                TryWriteCheckpoint("failed", Bound(exception.Message, 1024));
                Rma134AcceptanceReport failure = new Rma134AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    operating_system = SystemInfo.operatingSystem,
                };
                try
                {
                    WriteReport(resultPath, failure);
                }
                catch (Exception reportException)
                {
                    Debug.LogError("RMA-134 could not write its failure report: " + reportException);
                }
                Debug.LogError("RMA-134 local LLM physical acceptance failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceivedThreaded -= HandleLogMessage;
            }
        }
    }
}
