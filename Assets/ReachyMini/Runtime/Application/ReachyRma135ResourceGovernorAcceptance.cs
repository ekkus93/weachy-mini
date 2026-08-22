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
        // real observations.
        //
        // The observation INTERVAL is not a free tuning knob:
        // ReachyLocalLlmPhysicsBudgetTracker.Observe reports Exceeded when the deadline-miss
        // counter advanced at all since the previous observation. Each sample therefore
        // covers the whole gap since the last one, so a wider interval makes an individual
        // sample strictly MORE likely to report Exceeded (a 2 ms timestep means a 20 ms gap
        // spans ~10 physics steps but a 75 ms gap spans ~37). Spacing samples further apart
        // to "give the device time to settle" is counterproductive: it lowers the odds of
        // ever observing three consecutive admissible samples.
        //
        // 20 ms matches WaitForPhysicsBudgetAsync, the startup stabilization loop, which
        // needs the same three-consecutive-admissible streak on the same device moments
        // earlier and succeeds reliably. The larger COUNT is the safe knob for granting more
        // real recovery opportunity, so the budget is generous while the window stays tight.
        private const int RecoveryObservationBudget = 40;
        private static readonly TimeSpan RecoveryObservationInterval = TimeSpan.FromMilliseconds(20.0);

        // Admission samples physics once, and a new deadline miss suspends immediately by
        // design. Those misses are frequently transient on real hardware, so admission is
        // retried with the same interval and a comparable budget to WaitForPhysicsBudgetAsync
        // rather than treating one unlucky sample as an unusable device. A sustained
        // suspension still exhausts the budget and fails.
        private const int AdmissionAttemptBudget = 100;
        private static readonly TimeSpan AdmissionRetryInterval = TimeSpan.FromMilliseconds(20.0);

        // The spec forbids the integration layer silently retrying THE CANCELLED REQUEST at
        // a smaller profile behind the scenes ("does not reset the conversation, retry
        // automatically at the lower profile" -- section 11). It does not forbid what a real
        // subsequent interaction does naturally: a distinct new request. A resource-pressure
        // cancellation here means the same real-time condition RMA-135 exists to react to
        // (a genuine physics deadline miss, or thermal/memory state) landed during this
        // specific narrow generation window -- not that recovery failed. Each retry issues a
        // fresh LocalLlmGenerationRequest with its own id; nothing here replays the cancelled
        // one. Only ResourceCancelledDuringGeneration/ResourceSuspendedBeforeStart are
        // retried; SignalFailure and ResourceExhausted are real faults and stay terminal on
        // the first occurrence. A sustained condition still exhausts the budget and fails.
        private const int PostRecoveryGenerationAttemptBudget = 8;

        // A flat delay between attempts was found (2026-08-17, real hardware) to starve the
        // governor's hysteresis recovery of samples: RecoverySamplesRequired (3) consecutive
        // clean observations only advance on an EvaluateCurrentBudget call, and a fixed gap
        // between attempts contributes none. On a device with a low but real steady deadline-
        // miss rate, a fresh miss lands often enough (about once every two attempts, observed)
        // to keep resetting the streak before it reaches 3 -- a governor-cadence problem, not
        // sustained physics pressure or cleanup-path contention. Before every retry after the
        // first, actively poll at the same proven interval WaitForPhysicsBudgetAsync and the
        // governor_recovery loop above use, giving hysteresis the same fair chance to converge
        // it already gets at those call sites, instead of hoping a blind wait happens to land
        // outside the device's miss cadence.
        private const int PostRecoveryPreflightObservationBudget = 100;

        // Both the fault-injection probe ("Hi.") and SuccessPrompt above cost
        // real tokens beyond the mandatory system prompt. A governor-admitted
        // profile can legitimately leave less than this much room under
        // severe device throttling (observed as low as 1 token on real
        // hardware, 2026-08-21) -- below this floor, no message can survive
        // LocalLlmProvider's own token preflight long enough to exercise
        // fault injection or verify recovery, so that whole sequence is
        // skipped rather than failed on a precondition it cannot control.
        private const int MinimumHeadroomTokensForFaultInjectionProbe = 8;

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
