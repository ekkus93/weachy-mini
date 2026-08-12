#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Behavior
{
    public sealed class ReachyBehaviorTrajectoryFrame
    {
        private readonly ReadOnlyCollection<double> targets;

        internal ReachyBehaviorTrajectoryFrame(
            int offsetMilliseconds,
            IReadOnlyList<double> targetPositionsRadians)
        {
            if (offsetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offsetMilliseconds));
            }
            OffsetMilliseconds = offsetMilliseconds;
            targets = CopyTargets(targetPositionsRadians);
        }

        public int OffsetMilliseconds { get; }

        public IReadOnlyList<double> TargetPositionsRadians => targets;

        public double[] CopyTargetPositionsRadians()
        {
            var copy = new double[targets.Count];
            for (int index = 0; index < targets.Count; ++index)
            {
                copy[index] = targets[index];
            }
            return copy;
        }

        private static ReadOnlyCollection<double> CopyTargets(
            IReadOnlyList<double> source)
        {
            if (source == null ||
                source.Count != ReachyBehaviorPlannerActuators.Count)
            {
                throw new ArgumentException(
                    "Trajectory frames require exactly nine actuator targets.",
                    nameof(source));
            }

            var copy = new List<double>(source.Count);
            for (int index = 0; index < source.Count; ++index)
            {
                double value = source[index];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(source));
                }
                copy.Add(value);
            }
            return copy.AsReadOnly();
        }
    }

    public sealed class ReachyBehaviorTrajectoryPlan
    {
        private readonly ReadOnlyCollection<ReachyBehaviorTrajectoryFrame> frames;

        internal ReachyBehaviorTrajectoryPlan(
            ReachyBehaviorTrajectoryPurpose purpose,
            string? speech,
            int speechStartOffsetMilliseconds,
            string? resolvedGazeEntityId,
            IReadOnlyList<ReachyBehaviorTrajectoryFrame> frames)
        {
            if (speechStartOffsetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(speechStartOffsetMilliseconds));
            }
            if (frames == null)
            {
                throw new ArgumentNullException(nameof(frames));
            }

            var copy = new List<ReachyBehaviorTrajectoryFrame>(frames.Count);
            int previousOffset = -1;
            for (int index = 0; index < frames.Count; ++index)
            {
                ReachyBehaviorTrajectoryFrame frame = frames[index] ??
                    throw new ArgumentException(
                        "Trajectory plans cannot contain null frames.",
                        nameof(frames));
                if (frame.OffsetMilliseconds <= previousOffset)
                {
                    throw new ArgumentException(
                        "Trajectory frame offsets must increase strictly.",
                        nameof(frames));
                }
                previousOffset = frame.OffsetMilliseconds;
                copy.Add(frame);
            }

            Purpose = purpose;
            Speech = speech;
            SpeechStartOffsetMilliseconds = speechStartOffsetMilliseconds;
            ResolvedGazeEntityId = resolvedGazeEntityId;
            this.frames = copy.AsReadOnly();
        }

        public ReachyBehaviorTrajectoryPurpose Purpose { get; }

        public string? Speech { get; }

        public int SpeechStartOffsetMilliseconds { get; }

        public string? ResolvedGazeEntityId { get; }

        public IReadOnlyList<ReachyBehaviorTrajectoryFrame> Frames => frames;

        public int TotalDurationMilliseconds =>
            frames.Count == 0
                ? SpeechStartOffsetMilliseconds
                : Math.Max(
                    SpeechStartOffsetMilliseconds,
                    frames[frames.Count - 1].OffsetMilliseconds);
    }

    public enum ReachyBehaviorTargetSubmissionStatus
    {
        Accepted = 0,
        QueueFull = 1,
        ControllerUnavailable = 2,
        ContractRejected = 3,
    }

    public interface IReachyBehaviorControllerTargetSink
    {
        ReachyBehaviorTargetSubmissionStatus Submit(
            ReachyBehaviorTrajectoryFrame frame);
    }

    public sealed class ReachyBehaviorPlanResult
    {
        internal ReachyBehaviorPlanResult(
            ReachyBehaviorPlannerStatus status,
            string diagnosticCode,
            ReachyBehaviorTrajectoryPlan? plan)
        {
            Status = status;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Plan = plan;
        }

        public ReachyBehaviorPlannerStatus Status { get; }

        public string DiagnosticCode { get; }

        public ReachyBehaviorTrajectoryPlan? Plan { get; }

        public bool Succeeded =>
            Status == ReachyBehaviorPlannerStatus.Planned && Plan != null;
    }
}
