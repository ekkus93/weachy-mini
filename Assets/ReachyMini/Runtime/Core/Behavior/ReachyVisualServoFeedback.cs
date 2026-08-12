#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed class ReachyVisualServoFeedbackSample
    {
        public ReachyVisualServoFeedbackSample(
            long timestampNanoseconds,
            ulong authoritativeStateSequence,
            WorldModelSnapshot worldSnapshot,
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBehaviorSafetySnapshot safetySnapshot)
        {
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampNanoseconds));
            }
            if (authoritativeStateSequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativeStateSequence));
            }

            WorldModelSnapshot world = worldSnapshot ??
                throw new ArgumentNullException(nameof(worldSnapshot));
            if (world.TimestampNanoseconds <= 0L ||
                world.TimestampNanoseconds > timestampNanoseconds)
            {
                throw new ArgumentException(
                    "Visual-servo world snapshots must be current at or before the feedback timestamp.",
                    nameof(worldSnapshot));
            }
            for (int index = 0; index < world.Entities.Count; ++index)
            {
                ReachyVisionFrameIdentity identity =
                    world.Entities[index].LatestFrameIdentity;
                if (identity.SourceTimestampNanoseconds > timestampNanoseconds ||
                    identity.AuthoritativeSequence > authoritativeStateSequence)
                {
                    throw new ArgumentException(
                        "Visual-servo tracking observations cannot be newer than the paired feedback sample.",
                        nameof(worldSnapshot));
                }
            }

            TimestampNanoseconds = timestampNanoseconds;
            AuthoritativeStateSequence = authoritativeStateSequence;
            WorldSnapshot = world;
            MotionSnapshot = motionSnapshot ??
                throw new ArgumentNullException(nameof(motionSnapshot));
            SafetySnapshot = safetySnapshot ??
                throw new ArgumentNullException(nameof(safetySnapshot));
        }

        public long TimestampNanoseconds { get; }

        public ulong AuthoritativeStateSequence { get; }

        public WorldModelSnapshot WorldSnapshot { get; }

        public ReachyBehaviorMotionSnapshot MotionSnapshot { get; }

        public ReachyBehaviorSafetySnapshot SafetySnapshot { get; }
    }


    public sealed class ReachyVisualServoFeedbackUnavailableException :
        InvalidOperationException
    {
        public ReachyVisualServoFeedbackUnavailableException(
            string diagnosticCode,
            string message)
            : base(message)
        {
            if (string.IsNullOrWhiteSpace(diagnosticCode))
            {
                throw new ArgumentException(
                    "Feedback-unavailable diagnostics cannot be empty.",
                    nameof(diagnosticCode));
            }
            DiagnosticCode = diagnosticCode;
        }

        public string DiagnosticCode { get; }
    }

    public interface IReachyVisualServoFeedbackSource
    {
        ValueTask<ReachyVisualServoFeedbackSample> CaptureAsync(
            CancellationToken cancellationToken);
    }

    public interface IReachyVisualServoPollDelay
    {
        Task DelayAsync(
            int milliseconds,
            CancellationToken cancellationToken);
    }

    public sealed class ReachyVisualServoSystemPollDelay :
        IReachyVisualServoPollDelay
    {
        public Task DelayAsync(
            int milliseconds,
            CancellationToken cancellationToken)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            }
            return Task.Delay(milliseconds, cancellationToken);
        }
    }
}
