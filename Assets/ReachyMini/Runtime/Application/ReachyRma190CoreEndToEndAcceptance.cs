#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Validation
{
    // RMA-190 tranche 1 physical end-to-end acceptance. This intentionally
    // exercises only scenarios that do not require a human utterance, cloud
    // credentials, a local model artifact, or camera calibration:
    //
    //   1. shipping application launch while the runner has disabled network;
    //   2. full production MuJoCo model load + deterministic neutral reset;
    //   3. deterministic baseline-gesture replay through the real production
    //      controller target sink and authoritative MuJoCo state.
    //
    // Later RMA-190 tranches own camera/VLM, speech/local-LLM, cloud-failure,
    // model-failure, and solver-fault scenarios. Keeping the tranches explicit
    // prevents a missing hardware prerequisite from being mistaken for a
    // passing end-to-end scenario.
    internal sealed partial class ReachyRma190CoreEndToEndAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName =
            "reachy_rma190_core_e2e_acceptance";
        internal const string ResultFileName =
            "rma190-core-end-to-end-acceptance.json";

        private const int RuntimeWaitMilliseconds = 60_000;
        private const int OverallTimeoutMilliseconds = 90_000;
        private const int StateAdvanceDelayMilliseconds = 5;
        private const int ResetSettleSampleDelayMilliseconds = 20;
        private const int ResetSettleConsecutiveSamples = 8;
        private const double ResetSettleMaximumMotionRadians = 5.0e-4;
        private const double ResetSettleMaximumVelocityRadiansPerSecond = 2.0e-2;
        private const double MinimumGestureMotionRadians = 1.0e-5;
        private const double GestureReplayStateTolerance = 2.5e-3;
        private const long DeterministicPlanningTimestampNanoseconds =
            1_900_000_000L;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                !ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                return;
            }

            var host = new GameObject("ReachyRma190CoreEndToEndAcceptance");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma190CoreEndToEndAcceptance>();
        }

        private async void Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ResultFileName);
            DeleteIfPresent(resultPath);

            try
            {
                using var timeout = new CancellationTokenSource(
                    OverallTimeoutMilliseconds);
                AcceptanceReport report = await RunAcceptanceAsync(
                        timeout.Token)
                    .ConfigureAwait(true);
                report.status = "passed";
                WriteAtomically(resultPath, JsonUtility.ToJson(report, true));
                Debug.Log("RMA-190 core end-to-end acceptance passed.");
            }
            catch (Exception exception)
            {
                var failure = new AcceptanceReport
                {
                    status = "failed",
                    error = Bound(exception.ToString(), 4096),
                    device_model = SystemInfo.deviceModel,
                    android_api_level = TryReadApiLevel(),
                };
                try
                {
                    WriteAtomically(
                        resultPath,
                        JsonUtility.ToJson(failure, true));
                }
                catch (Exception reportException)
                {
                    Debug.LogError(
                        "RMA-190 could not write its failure report: " +
                        reportException.GetType().Name);
                }
                Debug.LogError(
                    "RMA-190 core end-to-end acceptance failed: " +
                    exception);
            }
        }

        private static async Task<AcceptanceReport> RunAcceptanceAsync(
            CancellationToken cancellationToken)
        {
            ReachyApplicationHostBehaviour hostBehaviour =
                await WaitForShippingHostAsync(cancellationToken)
                    .ConfigureAwait(true);
            ReachyApplicationHost host = hostBehaviour.Host ??
                throw new InvalidOperationException(
                    "The shipping application host is unavailable.");
            ReachyApplicationHealthSnapshot health = host.Health;
            if (health.State != ReachyApplicationState.Ready &&
                health.State != ReachyApplicationState.Degraded)
            {
                throw new InvalidOperationException(
                    "The shipping application host is not operational: " +
                    health.State + " " + health.Message);
            }

            IReachySimulationService simulation =
                host.GetRequiredService<IReachySimulationService>(
                    ReachyServiceKind.Simulation);
            IReachyBehaviorService behavior =
                host.GetRequiredService<IReachyBehaviorService>(
                    ReachyServiceKind.Behavior);
            if (simulation is not ReachyProductionSimulationApplicationService)
            {
                throw new InvalidOperationException(
                    "RMA-190 requires the shipping production simulation service.");
            }
            if (behavior is not ReachyBaselineBehaviorApplicationService)
            {
                throw new InvalidOperationException(
                    "RMA-190 requires the shipping baseline behavior service.");
            }

            ReachyProductionAuthoritativeRuntime runtime =
                await WaitForRuntimeAsync(cancellationToken)
                    .ConfigureAwait(true);
            NetworkSnapshot network = CaptureNetworkSnapshot();
            if (!network.Offline)
            {
                throw new InvalidOperationException(
                    "RMA-190 offline-launch scenario detected a validated Internet connection. " +
                    "The runner must disable network transports before launch.");
            }

            if (runtime.ModelHash == 0UL || runtime.BodyCount != 18)
            {
                throw new InvalidOperationException(
                    "The production runtime did not load the expected full Reachy model: " +
                    "model_hash=0x" + runtime.ModelHash.ToString("x16", CultureInfo.InvariantCulture) +
                    " body_count=" + runtime.BodyCount.ToString(CultureInfo.InvariantCulture) + ".");
            }

            IReachyApplicationInterruptionParticipant? behaviorInterruption =
                behavior as IReachyApplicationInterruptionParticipant;
            if (behaviorInterruption == null)
            {
                throw new InvalidOperationException(
                    "The shipping behavior service cannot be suspended for deterministic replay isolation.");
            }

            behaviorInterruption.PauseForApplicationInterruption();
            bool behaviorSuspended = true;
            try
            {
                ReachySimAuthoritativeStateFrame beforeReset =
                    await WaitForAuthoritativeStateAsync(
                            runtime,
                            minimumSequenceExclusive: 0UL,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                ValidateAuthoritativeLayout(beforeReset);

                uint initialContinuity = beforeReset.ContinuityId;
                ReachySimulationControlResult initialReset = runtime.ResetNeutral();
                if (!initialReset.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "RMA-190 neutral reset failed: " +
                        initialReset.Error.Code + ": " + initialReset.Error.Message);
                }
                ReachySimAuthoritativeStateFrame neutral =
                    await WaitForResetStateAsync(
                            runtime,
                            initialContinuity,
                            cancellationToken)
                        .ConfigureAwait(false);
                ValidateAuthoritativeLayout(neutral);

                ReachyBehaviorTrajectoryPlan plan = BuildAcknowledgmentPlan(neutral);
                string planSha256 = ComputePlanSha256(plan);

                GesturePass first = await ExecuteGesturePassAsync(
                        runtime,
                        plan,
                        neutral.ContinuityId,
                        cancellationToken)
                    .ConfigureAwait(false);

                ReachySimulationControlResult replayReset = runtime.ResetNeutral();
                if (!replayReset.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "RMA-190 replay neutral reset failed: " +
                        replayReset.Error.Code + ": " + replayReset.Error.Message);
                }
                ReachySimAuthoritativeStateFrame replayNeutral =
                    await WaitForResetStateAsync(
                            runtime,
                            first.FinalState.ContinuityId,
                            cancellationToken)
                        .ConfigureAwait(false);

                GesturePass second = await ExecuteGesturePassAsync(
                        runtime,
                        plan,
                        replayNeutral.ContinuityId,
                        cancellationToken)
                    .ConfigureAwait(false);

                double qposError = MaximumQposDifference(
                    first.FinalState,
                    second.FinalState);
                double qvelError = MaximumQvelDifference(
                    first.FinalState,
                    second.FinalState);
                bool actualMotion =
                    first.MaximumMotionRadians >= MinimumGestureMotionRadians &&
                    second.MaximumMotionRadians >= MinimumGestureMotionRadians;
                bool replayWithinTolerance =
                    qposError <= GestureReplayStateTolerance &&
                    qvelError <= GestureReplayStateTolerance;
                if (!actualMotion)
                {
                    throw new InvalidOperationException(
                        "RMA-190 deterministic replay submitted a gesture but did not observe authoritative actuator motion.");
                }
                if (!replayWithinTolerance)
                {
                    throw new InvalidOperationException(
                        "RMA-190 deterministic gesture replay diverged: qpos=" +
                        qposError.ToString("R", CultureInfo.InvariantCulture) +
                        " qvel=" +
                        qvelError.ToString("R", CultureInfo.InvariantCulture) +
                        " tolerance=" +
                        GestureReplayStateTolerance.ToString("R", CultureInfo.InvariantCulture) + ".");
                }

                return new AcceptanceReport
                {
                    status = "running",
                    device_model = SystemInfo.deviceModel,
                    android_api_level = ReadApiLevel(),
                    application_state = health.State.ToString(),
                    network_offline = network.Offline,
                    active_network_present = network.ActiveNetworkPresent,
                    active_network_internet_capability = network.InternetCapability,
                    active_network_validated = network.Validated,
                    simulation_service = simulation.GetType().Name,
                    behavior_service = behavior.GetType().Name,
                    runtime_status = runtime.Status.ToString(),
                    model_hash = "0x" + runtime.ModelHash.ToString(
                        "x16",
                        CultureInfo.InvariantCulture),
                    body_count = runtime.BodyCount,
                    qpos_count = neutral.QposCount,
                    qvel_count = neutral.QvelCount,
                    neutral_reset_succeeded = true,
                    neutral_reset_continuity_advanced =
                        neutral.ContinuityId != initialContinuity,
                    gesture_kind = ReachyBaselineBehaviorKind.Acknowledgment.ToString(),
                    gesture_plan_sha256 = planSha256,
                    gesture_plan_frame_count = plan.Frames.Count,
                    gesture_first_submitted_frame_count = first.SubmittedFrameCount,
                    gesture_second_submitted_frame_count = second.SubmittedFrameCount,
                    gesture_actual_motion_observed = actualMotion,
                    gesture_first_maximum_motion_radians = first.MaximumMotionRadians,
                    gesture_second_maximum_motion_radians = second.MaximumMotionRadians,
                    gesture_replay_maximum_qpos_error = qposError,
                    gesture_replay_maximum_qvel_error = qvelError,
                    gesture_replay_tolerance = GestureReplayStateTolerance,
                    gesture_replay_within_tolerance = replayWithinTolerance,
                    behavior_suspended_for_direct_replay = behaviorSuspended,
                    human_asr_transcript_injected = false,
                    cloud_credentials_injected = false,
                    local_model_injected = false,
                };
            }
            finally
            {
                behaviorInterruption.ResumeAfterApplicationInterruption();
            }
        }

        private static ReachyBehaviorTrajectoryPlan BuildAcknowledgmentPlan(
            ReachySimAuthoritativeStateFrame neutral)
        {
            ReachyBehaviorMotionSnapshot motion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(neutral);
            ReachyBehaviorSafetySnapshot safety =
                ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot(
                    neutral,
                    normalControllerAvailable: true,
                    workspaceClear: true);
            if (!safety.AllowsMotion)
            {
                throw new InvalidOperationException(
                    "The authoritative neutral state does not allow baseline motion.");
            }

            var planner = new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            ReachyBaselineBehaviorLibrary library =
                ReachyBaselineBehaviorLibrary.CreateMobileDefault(planner);
            ReachyBaselineBehaviorPlanResult result = library.Plan(
                ReachyBaselineBehaviorRequest.Acknowledgment(),
                worldSnapshot: null,
                motion,
                safety,
                DeterministicPlanningTimestampNanoseconds);
            ReachyBehaviorTrajectoryPlan? plan = result.PlannerResult.Plan;
            if (!result.PlannerResult.Succeeded || plan == null ||
                plan.Frames.Count == 0)
            {
                throw new InvalidOperationException(
                    "RMA-190 could not create the deterministic acknowledgment gesture: " +
                    result.PlannerResult.DiagnosticCode + ".");
            }
            return plan;
        }

        private static async Task<GesturePass> ExecuteGesturePassAsync(
            ReachyProductionAuthoritativeRuntime runtime,
            ReachyBehaviorTrajectoryPlan plan,
            uint neutralContinuityId,
            CancellationToken cancellationToken)
        {
            ReachySimAuthoritativeStateFrame baseline =
                await WaitForAuthoritativeStateAsync(
                        runtime,
                        minimumSequenceExclusive: 0UL,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            if (baseline.ContinuityId != neutralContinuityId)
            {
                throw new InvalidOperationException(
                    "RMA-190 gesture pass did not begin from the expected neutral continuity.");
            }
            ReachyBehaviorMotionSnapshot baselineMotion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(baseline);

            var executor = new ReachyBehaviorTrajectoryExecutor(
                new ReachyProductionBehaviorControllerTargetSink(runtime),
                new ReachyBehaviorSystemTrajectoryDelay());
            Task<ReachyBehaviorTrajectoryExecutionResult> executionTask =
                executor.ExecuteAsync(plan, cancellationToken);

            double maximumMotion = 0.0;
            ulong lastSequence = baseline.Sequence;
            if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame sample))
            {
                throw new InvalidOperationException(
                    "RMA-190 could not allocate a gesture replay sample frame.");
            }

            while (!executionTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runtime.TryCaptureLatestAuthoritativeState(sample) &&
                    sample.Sequence > lastSequence &&
                    sample.ContinuityId == neutralContinuityId)
                {
                    ReachyBehaviorMotionSnapshot current =
                        ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(sample);
                    maximumMotion = Math.Max(
                        maximumMotion,
                        MaximumActuatorMotion(baselineMotion, current));
                    lastSequence = sample.Sequence;
                }
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }

            ReachyBehaviorTrajectoryExecutionResult execution =
                await executionTask.ConfigureAwait(false);
            if (!execution.Completed ||
                execution.SubmittedFrameCount != plan.Frames.Count)
            {
                throw new InvalidOperationException(
                    "RMA-190 gesture execution did not submit the complete deterministic plan: " +
                    execution.Status + " / " + execution.DiagnosticCode + ".");
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            ReachySimAuthoritativeStateFrame finalState =
                await WaitForAuthoritativeStateAsync(
                        runtime,
                        lastSequence,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (finalState.ContinuityId != neutralContinuityId)
            {
                throw new InvalidOperationException(
                    "RMA-190 gesture continuity changed unexpectedly during replay.");
            }

            ReachyBehaviorMotionSnapshot finalMotion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(finalState);
            maximumMotion = Math.Max(
                maximumMotion,
                MaximumActuatorMotion(baselineMotion, finalMotion));
            return new GesturePass(
                finalState,
                execution.SubmittedFrameCount,
                maximumMotion);
        }
    }
}
