#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Rendering;

namespace ReachyMini.AppState
{
    // RMA-195 phase A: replaces the permanently-Unavailable behavior stub with
    // a real continuous planner -> executor -> target-sink loop against
    // ReachyProductionAuthoritativeRuntime alone. RMA-154's acceptance harness
    // already proved this pipeline end to end, but only as a fixed-timeout
    // single run; this service is the "continuous supervised execution"
    // wrapper the TODO calls out as the actual new work -- it never returns to
    // a "done" state, re-planning its current target indefinitely, aborting
    // in-flight motion whenever ReachyBehaviorAuthoritativeSafety reports the
    // authoritative runtime no longer allows it, and integrating with
    // app-pause/resume via IReachyApplicationInterruptionParticipant.
    //
    // Deliberately zero perception/provider dependency (phase A scope):
    // worldSnapshot is always null and workspaceClear is hardcoded true below
    // -- there is no obstacle-avoidance/perception signal yet. Phase C is
    // expected to replace that hardcoded true with a real
    // IReachyPerceptionService-sourced value once camera-fed tracking lands.
    public sealed class ReachyBaselineBehaviorApplicationService :
        ReachyApplicationServiceBase,
        IReachyBehaviorService,
        IReachyApplicationInterruptionParticipant
    {
        // Kinds that represent an ongoing state rather than a one-shot
        // gesture: replanned and re-executed indefinitely rather than
        // reverting to NeutralIdle once their trajectory completes.
        private const int SustainedReplanIntervalMilliseconds = 250;
        private const int NotReadyRetryMilliseconds = 200;

        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly ReachyBaselineBehaviorLibrary library;
        private readonly ReachyBehaviorTrajectoryExecutor executor;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly object snapshotSync = new object();

        private ReachySimAuthoritativeStateFrame? capturedStateFrame;
        private CancellationTokenSource? loopCancellation;
        private ReachyBaselineBehaviorRequest currentTarget =
            ReachyBaselineBehaviorRequest.NeutralIdle();

        // Written by TryTriggerGesture (any caller thread) and consumed by
        // RunLoopAsync (the background loop thread) -- both sides go through
        // Interlocked.Exchange so the field is not also declared `volatile`
        // (mixing volatile with `ref` in Interlocked calls is a compiler
        // warning, which this project's warnings-as-errors policy forbids).
        private ReachyBaselineBehaviorRequest? pendingTarget;
        private ReachyBehaviorServiceSnapshot snapshot;

        public ReachyBaselineBehaviorApplicationService(
            ReachyProductionAuthoritativeRuntime runtime)
            : base(
                "baseline-behavior",
                ReachyServiceKind.Behavior,
                ReachyServiceCriticality.Optional)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            ReachyDeterministicBehaviorPlanner planner =
                new ReachyDeterministicBehaviorPlanner(
                    ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            library = ReachyBaselineBehaviorLibrary.CreateMobileDefault(planner);
            executor = new ReachyBehaviorTrajectoryExecutor(
                new ReachyProductionBehaviorControllerTargetSink(runtime),
                new ReachyBehaviorSystemTrajectoryDelay());
            snapshot = new ReachyBehaviorServiceSnapshot(
                ReachyBehaviorServiceExecutionState.Idle,
                ReachyBaselineBehaviorKind.NeutralIdle,
                "Baseline behavior has not started.",
                0UL);
        }

        public ReachyBehaviorServiceSnapshot Snapshot
        {
            get
            {
                lock (snapshotSync)
                {
                    return snapshot;
                }
            }
        }

        public event EventHandler<ReachyBehaviorServiceSnapshotChangedEventArgs>?
            SnapshotChanged;

        public bool TryTriggerGesture(
            ReachyBaselineBehaviorKind gesture,
            out string diagnosticCode)
        {
            ReachyBaselineBehaviorRequest? request = CreateZeroArgumentRequest(gesture);
            if (request == null)
            {
                diagnosticCode =
                    "baseline-behavior-gesture-requires-perception-or-provider-input";
                return false;
            }

            ReachyServiceState state = Health.State;
            if (state != ReachyServiceState.Ready &&
                state != ReachyServiceState.Degraded)
            {
                diagnosticCode = "baseline-behavior-service-not-ready";
                return false;
            }

            Interlocked.Exchange(ref pendingTarget, request);
            diagnosticCode = string.Empty;
            return true;
        }

        public void PauseForApplicationInterruption()
        {
            StopLoop();
            PublishSnapshot(
                ReachyBehaviorServiceExecutionState.Paused,
                currentTarget.Kind,
                "Baseline behavior paused for an application interruption.");
        }

        public void ResumeAfterApplicationInterruption()
        {
            if (loopCancellation != null)
            {
                return;
            }
            currentTarget = ReachyBaselineBehaviorRequest.NeutralIdle();
            pendingTarget = null;
            StartLoop();
        }

        protected override void OnInitialize()
        {
            StartLoop();
            SetReady("Baseline behavior continuous execution loop started.");
        }

        protected override void OnDispose()
        {
            StopLoop();
        }

        private void StartLoop()
        {
            var cancellation = new CancellationTokenSource();
            loopCancellation = cancellation;
            _ = Task.Run(() => RunLoopAsync(cancellation.Token));
        }

        private void StopLoop()
        {
            CancellationTokenSource? cancellation =
                Interlocked.Exchange(ref loopCancellation, null);
            if (cancellation == null)
            {
                return;
            }
            cancellation.Cancel();
            cancellation.Dispose();
            // Not awaited: the loop observes cancellation at its next await
            // point (executor delay or the idle/retry delay) and returns
            // promptly. Pause/resume/dispose are invoked sequentially by
            // ReachyApplicationInterruptionCoordinator, never concurrently
            // with each other for the same service, so a brief shutdown
            // window here does not race a fresh StartLoop().
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ReachyBaselineBehaviorRequest? requested =
                        Interlocked.Exchange(ref pendingTarget, null);
                    if (requested != null)
                    {
                        currentTarget = requested;
                    }

                    if (!TryCaptureMotionAndSafety(
                        out ReachyBehaviorMotionSnapshot motion,
                        out ReachyBehaviorSafetySnapshot safety))
                    {
                        PublishSnapshot(
                            ReachyBehaviorServiceExecutionState.Idle,
                            currentTarget.Kind,
                            "Waiting for the authoritative simulation to start.");
                        await Task.Delay(NotReadyRetryMilliseconds, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (!safety.AllowsMotion)
                    {
                        PublishSnapshot(
                            ReachyBehaviorServiceExecutionState.SafetyBlocked,
                            currentTarget.Kind,
                            "Motion is blocked by an active safety interlock.");
                        await Task.Delay(NotReadyRetryMilliseconds, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    ReachyBaselineBehaviorPlanResult planResult = library.Plan(
                        currentTarget,
                        worldSnapshot: null,
                        motion,
                        safety,
                        NowNanoseconds(),
                        cancellationToken);
                    if (!planResult.Succeeded || planResult.PlannerResult.Plan == null)
                    {
                        PublishSnapshot(
                            ReachyBehaviorServiceExecutionState.Idle,
                            currentTarget.Kind,
                            "Baseline plan '" + planResult.PlannerResult.DiagnosticCode +
                                "' was not produced; retrying neutral idle.");
                        currentTarget = ReachyBaselineBehaviorRequest.NeutralIdle();
                        await Task.Delay(NotReadyRetryMilliseconds, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    PublishSnapshot(
                        ReachyBehaviorServiceExecutionState.ExecutingGesture,
                        currentTarget.Kind,
                        "Executing " + currentTarget.Kind + ".");
                    ReachyBehaviorTrajectoryExecutionResult executionResult =
                        await executor.ExecuteAsync(
                            planResult.PlannerResult.Plan,
                            cancellationToken).ConfigureAwait(false);

                    if (executionResult.Status ==
                        ReachyBehaviorTrajectoryExecutionStatus.Cancelled)
                    {
                        break;
                    }

                    if (!executionResult.Completed)
                    {
                        PublishSnapshot(
                            ReachyBehaviorServiceExecutionState.Idle,
                            currentTarget.Kind,
                            "Trajectory submission rejected (" +
                                executionResult.DiagnosticCode +
                                "); reverting to neutral idle.");
                        currentTarget = ReachyBaselineBehaviorRequest.NeutralIdle();
                        await Task.Delay(NotReadyRetryMilliseconds, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (!IsSustained(currentTarget.Kind))
                    {
                        currentTarget = ReachyBaselineBehaviorRequest.NeutralIdle();
                    }
                    PublishSnapshot(
                        ReachyBehaviorServiceExecutionState.Idle,
                        currentTarget.Kind,
                        "Baseline behavior settled on " + currentTarget.Kind + ".");
                    await Task.Delay(SustainedReplanIntervalMilliseconds, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected shutdown path (Pause/Dispose); no fault to report.
            }
            catch (Exception exception)
            {
                SetFaulted(
                    "Baseline behavior execution loop terminated unexpectedly: " +
                        exception.Message);
            }
        }

        private bool TryCaptureMotionAndSafety(
            out ReachyBehaviorMotionSnapshot motion,
            out ReachyBehaviorSafetySnapshot safety)
        {
            motion = null!;
            safety = null!;

            if (capturedStateFrame == null)
            {
                if (!runtime.TryCreateAuthoritativeStateFrame(
                    out ReachySimAuthoritativeStateFrame created))
                {
                    return false;
                }
                capturedStateFrame = created;
            }

            if (!runtime.TryCaptureLatestAuthoritativeState(capturedStateFrame))
            {
                return false;
            }

            motion = ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(
                capturedStateFrame);
            safety = ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot(
                capturedStateFrame,
                normalControllerAvailable: true,
                workspaceClear: true);
            return true;
        }

        private long NowNanoseconds()
        {
            double nanoseconds =
                clock.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency);
            return checked((long)nanoseconds);
        }

        private void PublishSnapshot(
            ReachyBehaviorServiceExecutionState state,
            ReachyBaselineBehaviorKind kind,
            string message)
        {
            ReachyBehaviorServiceSnapshot next;
            lock (snapshotSync)
            {
                next = new ReachyBehaviorServiceSnapshot(
                    state,
                    kind,
                    message,
                    checked(snapshot.Revision + 1UL));
                snapshot = next;
            }
            SnapshotChanged?.Invoke(
                this,
                new ReachyBehaviorServiceSnapshotChangedEventArgs(next));
        }

        private static bool IsSustained(ReachyBaselineBehaviorKind kind)
        {
            return kind == ReachyBaselineBehaviorKind.NeutralIdle ||
                kind == ReachyBaselineBehaviorKind.Listening ||
                kind == ReachyBaselineBehaviorKind.SleepRest;
        }

        private static ReachyBaselineBehaviorRequest? CreateZeroArgumentRequest(
            ReachyBaselineBehaviorKind gesture)
        {
            switch (gesture)
            {
                case ReachyBaselineBehaviorKind.NeutralIdle:
                    return ReachyBaselineBehaviorRequest.NeutralIdle();
                case ReachyBaselineBehaviorKind.Listening:
                    return ReachyBaselineBehaviorRequest.Listening();
                case ReachyBaselineBehaviorKind.Speaking:
                    return ReachyBaselineBehaviorRequest.SpeakingFromTiming();
                case ReachyBaselineBehaviorKind.Acknowledgment:
                    return ReachyBaselineBehaviorRequest.Acknowledgment();
                case ReachyBaselineBehaviorKind.Curiosity:
                    return ReachyBaselineBehaviorRequest.Curiosity();
                case ReachyBaselineBehaviorKind.Surprise:
                    return ReachyBaselineBehaviorRequest.Surprise();
                case ReachyBaselineBehaviorKind.UnavailableError:
                    return ReachyBaselineBehaviorRequest.UnavailableError();
                case ReachyBaselineBehaviorKind.SleepRest:
                    return ReachyBaselineBehaviorRequest.SleepRest();
                case ReachyBaselineBehaviorKind.Wake:
                    return ReachyBaselineBehaviorRequest.Wake();
                // GazeAcquisition/GazeSearch need a perception-sourced entity
                // ID (phase C); no zero-argument factory exists for them.
                default:
                    return null;
            }
        }
    }
}
