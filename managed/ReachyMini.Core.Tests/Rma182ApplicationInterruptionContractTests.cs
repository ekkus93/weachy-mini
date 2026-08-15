#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using ReachyMini.AppState;
using ReachyMini.Conversation;

namespace ReachyMini.Core.Tests
{
    internal static class Rma182ApplicationInterruptionContractTests
    {
        public static void RunAll()
        {
            InterruptionGateCancelsOldWorkAndCreatesFreshResumeGeneration();
            CoordinatorPausesDependentsFirstAndResumesDependenciesFirst();
            CoordinatorIsIdempotentAcrossRepeatedCycles();
            CoordinatorFailsClosedButStillQuiescesRemainingParticipants();
            ConversationPauseCancelsWorkAndResumesToIdleOnlyWhenOwned();
            MainScreenPauseHidesPanelsAndRestoresDefinedIdleState();
        }

        private static void InterruptionGateCancelsOldWorkAndCreatesFreshResumeGeneration()
        {
            using var gate = new ReachyApplicationInterruptionGate();
            using CancellationTokenSource before =
                gate.CreateLinkedTokenSource(CancellationToken.None);
            False(before.IsCancellationRequested, "pre-pause token");

            gate.PauseForApplicationInterruption();
            True(gate.IsPaused, "gate paused");
            True(before.IsCancellationRequested, "active work cancelled");
            ulong pausedRevision = gate.Revision;
            gate.PauseForApplicationInterruption();
            Equal(pausedRevision, gate.Revision, "repeat pause is idempotent");

            using CancellationTokenSource whilePaused =
                gate.CreateLinkedTokenSource(CancellationToken.None);
            True(whilePaused.IsCancellationRequested, "new work stays suspended");

            gate.ResumeAfterApplicationInterruption();
            False(gate.IsPaused, "gate resumed");
            using CancellationTokenSource after =
                gate.CreateLinkedTokenSource(CancellationToken.None);
            False(after.IsCancellationRequested, "resume gets fresh cancellation generation");
            gate.ResumeAfterApplicationInterruption();
            False(after.IsCancellationRequested, "repeat resume is idempotent");
        }

        private static void CoordinatorPausesDependentsFirstAndResumesDependenciesFirst()
        {
            var events = new List<string>();
            using ReachyApplicationHost host = BuildHost(events);
            host.Start();
            using var coordinator = new ReachyApplicationInterruptionCoordinator(host);

            ReachyApplicationInterruptionResult paused = coordinator.Pause();
            True(paused.Succeeded, "pause result");
            Equal(8, paused.ParticipantCount, "pause participant count");
            Sequence(
                events,
                "pause:UserInterface",
                "pause:Behavior",
                "pause:Perception",
                "pause:Provider",
                "pause:Persistence",
                "pause:Audio",
                "pause:Camera",
                "pause:Simulation");

            events.Clear();
            ReachyApplicationInterruptionResult resumed = coordinator.Resume();
            True(resumed.Succeeded, "resume result");
            Equal(1UL, resumed.CompletedCycleCount, "completed cycle");
            Sequence(
                events,
                "resume:Simulation",
                "resume:Camera",
                "resume:Audio",
                "resume:Persistence",
                "resume:Provider",
                "resume:Perception",
                "resume:Behavior",
                "resume:UserInterface");
        }

        private static void CoordinatorIsIdempotentAcrossRepeatedCycles()
        {
            var events = new List<string>();
            using ReachyApplicationHost host = BuildHost(events);
            host.Start();
            using var coordinator = new ReachyApplicationInterruptionCoordinator(host);

            for (int cycle = 1; cycle <= 5; ++cycle)
            {
                events.Clear();
                True(coordinator.Pause().Succeeded, "cycle pause");
                int pauseEvents = events.Count;
                True(coordinator.Pause().Succeeded, "repeat cycle pause");
                Equal(pauseEvents, events.Count, "repeat pause did not re-enter participants");

                True(coordinator.Resume().Succeeded, "cycle resume");
                int resumeEvents = events.Count;
                True(coordinator.Resume().Succeeded, "repeat cycle resume");
                Equal(resumeEvents, events.Count, "repeat resume did not re-enter participants");
                Equal((ulong)cycle, coordinator.CompletedCycleCount, "cycle counter");
            }
        }

        private static void CoordinatorFailsClosedButStillQuiescesRemainingParticipants()
        {
            var events = new List<string>();
            using ReachyApplicationHost host = BuildHost(
                events,
                failingKind: ReachyServiceKind.Provider);
            host.Start();
            using var coordinator = new ReachyApplicationInterruptionCoordinator(host);

            ReachyApplicationInterruptionResult result = coordinator.Pause();
            False(result.Succeeded, "failing pause result");
            Equal(1, result.FailureCount, "failing participant count");
            Equal(
                ReachyApplicationInterruptionState.Faulted,
                result.State,
                "coordinator fails closed");
            Contains(events, "pause:Simulation", "simulation still receives pause");
            Contains(events, "pause:UserInterface", "UI received pause before failure");
            False(coordinator.Resume().Succeeded, "faulted coordinator does not resume");
        }

        private static void ConversationPauseCancelsWorkAndResumesToIdleOnlyWhenOwned()
        {
            using var conversation = new ReachyConversationStateMachine();
            ReachyConversationOperation listening = conversation.BeginListening();
            False(listening.CancellationToken.IsCancellationRequested, "listening starts active");

            ReachyConversationSnapshot paused =
                conversation.SuspendForApplicationInterruption();
            Equal(ReachyConversationState.Unavailable, paused.State, "conversation paused state");
            True(listening.CancellationToken.IsCancellationRequested, "conversation operation cancelled");
            ReachyConversationSnapshot resumed =
                conversation.ResumeAfterApplicationInterruption();
            Equal(ReachyConversationState.Idle, resumed.State, "conversation resumes idle");

            _ = conversation.MarkUnavailable("provider-offline");
            _ = conversation.SuspendForApplicationInterruption();
            ReachyConversationSnapshot preserved =
                conversation.ResumeAfterApplicationInterruption();
            Equal(ReachyConversationState.Unavailable, preserved.State, "preexisting unavailable preserved");
            Equal("unavailable-provider-offline", preserved.StatusCode, "preexisting reason preserved");
        }

        private static void MainScreenPauseHidesPanelsAndRestoresDefinedIdleState()
        {
            var state = new ReachyMainScreenStateStore();
            state.ShowSettings("Settings open.");
            ReachyMainScreenSnapshot paused = state.PauseForApplicationInterruption();
            Equal(ReachyInteractionState.Interrupted, paused.InteractionState, "UI pause state");
            False(paused.SettingsVisible, "settings hidden while paused");
            False(paused.DiagnosticsVisible, "diagnostics hidden while paused");

            ReachyMainScreenSnapshot resumed = state.ResumeAfterApplicationInterruption();
            Equal(ReachyInteractionState.Idle, resumed.InteractionState, "UI resume state");
            Contains(resumed.Detail, "start a new interaction", "UI resume detail");

            state.SetInteraction(ReachyInteractionState.Error, "existing error");
            _ = state.PauseForApplicationInterruption();
            ReachyMainScreenSnapshot preserved = state.ResumeAfterApplicationInterruption();
            Equal(ReachyInteractionState.Error, preserved.InteractionState, "UI error preserved");
            Equal("existing error", preserved.Detail, "UI error detail preserved");
        }

        private static ReachyApplicationHost BuildHost(
            List<string> events,
            ReachyServiceKind? failingKind = null)
        {
            var registrations = new List<ReachyServiceRegistration>();
            foreach (ReachyServiceKind kind in Enum.GetValues<ReachyServiceKind>())
            {
                ReachyServiceKind captured = kind;
                registrations.Add(new ReachyServiceRegistration(
                    captured.ToString(),
                    captured,
                    ReachyServiceCriticality.Required,
                    Array.Empty<ReachyServiceKind>(),
                    resolver => new LifecycleService(
                        captured,
                        events,
                        failingKind == captured)));
            }
            return new ReachyApplicationHost(
                ReachyApplicationComposition.CreateComplete(registrations));
        }

        private sealed class LifecycleService :
            ReachyApplicationServiceBase,
            IReachySimulationService,
            IReachyCameraService,
            IReachyAudioService,
            IReachyProviderService,
            IReachyPerceptionService,
            IReachyBehaviorService,
            IReachyPersistenceService,
            IReachyUserInterfaceService,
            IReachyApplicationInterruptionParticipant
        {
            private readonly List<string> events;
            private readonly bool failPause;

            internal LifecycleService(
                ReachyServiceKind kind,
                List<string> events,
                bool failPause)
                : base(
                    kind.ToString(),
                    kind,
                    ReachyServiceCriticality.Required)
            {
                this.events = events;
                this.failPause = failPause;
            }

            public void PauseForApplicationInterruption()
            {
                events.Add("pause:" + Kind);
                if (failPause)
                {
                    throw new InvalidOperationException("injected pause failure");
                }
            }

            public void ResumeAfterApplicationInterruption()
            {
                events.Add("resume:" + Kind);
            }

            protected override void OnInitialize()
            {
                SetReady("ready");
            }
        }

        private static void Sequence(
            List<string> actual,
            params string[] expected)
        {
            Equal(expected.Length, actual.Count, "event count");
            for (int index = 0; index < expected.Length; ++index)
            {
                Equal(expected[index], actual[index], "event order " + index);
            }
        }

        private static void Contains(
            List<string> values,
            string expected,
            string label)
        {
            for (int index = 0; index < values.Count; ++index)
            {
                if (string.Equals(values[index], expected, StringComparison.Ordinal))
                {
                    return;
                }
            }
            throw new InvalidOperationException(label + ": missing " + expected);
        }

        private static void Contains(string value, string expected, string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(label + ": missing " + expected);
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + ": expected true");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(label + ": expected false");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected " + expected + ", actual " + actual);
            }
        }
    }
}
