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
        private readonly object gate = new object();
        private readonly ILocalLlmPhysicsBudgetSource inner;
        private int passThroughCapturesBeforeInjection = -1;

        internal Rma135FaultInjectingPhysicsBudgetSource(ILocalLlmPhysicsBudgetSource inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        internal int InjectedCount { get; private set; }
        internal LocalLlmPhysicsBudgetState UnderlyingStateAtInjection { get; private set; } =
            LocalLlmPhysicsBudgetState.Unavailable;

        internal void ArmOneShotExceededAfterPassThrough()
        {
            lock (gate)
            {
                if (passThroughCapturesBeforeInjection >= 0)
                {
                    throw new InvalidOperationException(
                        "RMA-135 physics fault injection is already armed.");
                }
                passThroughCapturesBeforeInjection = 1;
            }
        }

        public LocalLlmPhysicsBudgetState Capture()
        {
            LocalLlmPhysicsBudgetState real = inner.Capture();
            lock (gate)
            {
                if (passThroughCapturesBeforeInjection < 0)
                {
                    return real;
                }
                if (passThroughCapturesBeforeInjection > 0)
                {
                    --passThroughCapturesBeforeInjection;
                    return real;
                }
                passThroughCapturesBeforeInjection = -1;
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
