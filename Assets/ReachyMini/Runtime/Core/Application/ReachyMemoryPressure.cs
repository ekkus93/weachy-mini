#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public enum ReachyMemoryPressureReleaseStatus
    {
        Released = 0,
        RetainedActiveState = 1,
        NothingToRelease = 2,
        Failed = 3,
    }

    public readonly struct ReachyMemoryPressureReleaseResult
    {
        public ReachyMemoryPressureReleaseResult(
            ReachyMemoryPressureReleaseStatus status,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(ReachyMemoryPressureReleaseStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            Status = status;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public ReachyMemoryPressureReleaseStatus Status { get; }

        public string Diagnostic { get; }
    }

    public interface IReachyMemoryPressureParticipant
    {
        ReachyMemoryPressureReleaseResult ReleaseForMemoryPressure();
    }

    public readonly struct ReachyMemoryPressureSweepResult
    {
        internal ReachyMemoryPressureSweepResult(
            int participantCount,
            int releasedCount,
            int retainedActiveCount,
            int failureCount)
        {
            ParticipantCount = participantCount;
            ReleasedCount = releasedCount;
            RetainedActiveCount = retainedActiveCount;
            FailureCount = failureCount;
        }

        public int ParticipantCount { get; }

        public int ReleasedCount { get; }

        public int RetainedActiveCount { get; }

        public int FailureCount { get; }
    }

    public static class ReachyMemoryPressureRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<IReachyMemoryPressureParticipant> Participants =
            new List<IReachyMemoryPressureParticipant>();

        public static IDisposable Register(IReachyMemoryPressureParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            lock (Sync)
            {
                if (!Participants.Contains(participant))
                {
                    Participants.Add(participant);
                }
            }
            return new Registration(participant);
        }

        public static ReachyMemoryPressureSweepResult ReleaseRegisteredResources()
        {
            IReachyMemoryPressureParticipant[] snapshot;
            lock (Sync)
            {
                snapshot = Participants.ToArray();
            }

            int released = 0;
            int retainedActive = 0;
            int failures = 0;
            for (int index = 0; index < snapshot.Length; ++index)
            {
                try
                {
                    ReachyMemoryPressureReleaseResult result =
                        snapshot[index].ReleaseForMemoryPressure();
                    switch (result.Status)
                    {
                        case ReachyMemoryPressureReleaseStatus.Released:
                            released = checked(released + 1);
                            break;
                        case ReachyMemoryPressureReleaseStatus.RetainedActiveState:
                            retainedActive = checked(retainedActive + 1);
                            break;
                        case ReachyMemoryPressureReleaseStatus.Failed:
                            failures = checked(failures + 1);
                            break;
                    }
                }
                catch (Exception)
                {
                    failures = checked(failures + 1);
                }
            }

            return new ReachyMemoryPressureSweepResult(
                snapshot.Length,
                released,
                retainedActive,
                failures);
        }

        private sealed class Registration : IDisposable
        {
            private IReachyMemoryPressureParticipant? participant;

            public Registration(IReachyMemoryPressureParticipant participant)
            {
                this.participant = participant;
            }

            public void Dispose()
            {
                IReachyMemoryPressureParticipant? registered = participant;
                if (registered == null)
                {
                    return;
                }

                lock (Sync)
                {
                    Participants.Remove(registered);
                }
                participant = null;
                GC.SuppressFinalize(this);
            }
        }
    }
}
