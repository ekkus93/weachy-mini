#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Validation
{
    internal sealed class Rma135PhysicsStartupStabilization
    {
        internal Rma135PhysicsStartupStabilization(
            LocalLlmPhysicsBudgetState state,
            int observations,
            int exceededObservations,
            ulong minimumObservedStepCount)
        {
            State = state;
            Observations = observations;
            ExceededObservations = exceededObservations;
            MinimumObservedStepCount = minimumObservedStepCount;
        }

        internal LocalLlmPhysicsBudgetState State { get; }
        internal int Observations { get; }
        internal int ExceededObservations { get; }
        internal ulong MinimumObservedStepCount { get; }
    }

    internal sealed class Rma135FaultInjectingPhysicsBudgetSource : ILocalLlmPhysicsBudgetSource
    {
        private static readonly TimeSpan PostLoadSettleSampleInterval =
            TimeSpan.FromMilliseconds(800.0);

        // The settle interval above is a WAIT, and must not double as the measurement
        // window. ReachyLocalLlmPhysicsBudgetTracker.Observe reports Exceeded whenever the
        // deadline-miss counter advanced since the previous observation, so a sample taken
        // straight after an 800 ms pause covers ~400 physics steps at the 2 ms timestep and
        // reports Exceeded if any single one of them slipped -- including slips that
        // happened early in the settle window that the device has already recovered from.
        // That conflation makes a longer settle wait report *worse* physics, which is the
        // opposite of its intent.
        //
        // So after settling, one real sample is taken and discarded purely to re-baseline
        // the tracker, and the sample that is actually used covers only the short window
        // below. Nothing is fabricated: the reported state is still a real capture of real
        // authoritative timing, and physics that is genuinely still missing deadlines now
        // reports Exceeded through this path exactly as before. The window matches the
        // startup stabilization loop so both answer the same question -- is physics healthy
        // *now* -- rather than "did anything slip while we were waiting".
        private static readonly TimeSpan PostLoadMeasurementWindow =
            TimeSpan.FromMilliseconds(20.0);

        private readonly object gate = new object();
        private readonly ILocalLlmPhysicsBudgetSource inner;
        private bool postLoadSettleSpacingEnabled = true;
        private bool hasRealCapture;
        private bool replayVerifiedPassThrough;
        private bool injectNextLiveCapture;

        internal Rma135FaultInjectingPhysicsBudgetSource(ILocalLlmPhysicsBudgetSource inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        internal int InjectedCount { get; private set; }
        internal LocalLlmPhysicsBudgetState LastObservedRealState { get; private set; } =
            LocalLlmPhysicsBudgetState.Unavailable;
        internal LocalLlmPhysicsBudgetState UnderlyingStateAtInjection { get; private set; } =
            LocalLlmPhysicsBudgetState.Unavailable;

        internal void ArmOneShotExceededAfterPassThrough()
        {
            lock (gate)
            {
                if (replayVerifiedPassThrough || injectNextLiveCapture)
                {
                    throw new InvalidOperationException(
                        "RMA-135 physics fault injection is already armed.");
                }
                if (LastObservedRealState != LocalLlmPhysicsBudgetState.Healthy &&
                    LastObservedRealState != LocalLlmPhysicsBudgetState.AtRisk)
                {
                    throw new InvalidOperationException(
                        "RMA-135 physics fault injection requires a freshly verified " +
                        "admissible real pass-through sample.");
                }

                postLoadSettleSpacingEnabled = false;
                replayVerifiedPassThrough = true;
                injectNextLiveCapture = true;
            }
        }

        public LocalLlmPhysicsBudgetState Capture()
        {
            bool spacePostLoadSample;
            lock (gate)
            {
                if (replayVerifiedPassThrough)
                {
                    replayVerifiedPassThrough = false;
                    return LastObservedRealState;
                }
                spacePostLoadSample = postLoadSettleSpacingEnabled && hasRealCapture;
            }

            if (spacePostLoadSample)
            {
                Task.Delay(PostLoadSettleSampleInterval).GetAwaiter().GetResult();
                inner.Capture();
                Task.Delay(PostLoadMeasurementWindow).GetAwaiter().GetResult();
            }

            LocalLlmPhysicsBudgetState real = inner.Capture();
            lock (gate)
            {
                hasRealCapture = true;
                LastObservedRealState = real;
                if (!injectNextLiveCapture)
                {
                    return real;
                }

                injectNextLiveCapture = false;
                UnderlyingStateAtInjection = real;
                ++InjectedCount;
                return LocalLlmPhysicsBudgetState.Exceeded;
            }
        }
    }

    internal sealed class Rma135CollectingSink : ILocalLlmStreamSink
    {
        internal int TextEventCount { get; private set; }
        internal bool TerminalValidated { get; private set; }
        internal bool SawTrustedPartialOutput { get; private set; }

        public ValueTask OnEventAsync(
            LocalLlmStreamEvent streamEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (streamEvent.Type == LocalLlmStreamEventType.Text)
            {
                ++TextEventCount;
                SawTrustedPartialOutput |= streamEvent.IsTrustedExecutableOutput;
            }
            else if (streamEvent.Type == LocalLlmStreamEventType.Completed)
            {
                TerminalValidated = true;
            }
            return default;
        }
    }
}
