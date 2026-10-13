#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ReachyMini.Perception;
using ReachyMini.Performance;
using ReachyMini.LocalModels;

namespace ReachyMini.VlmScheduling.Tests
{
    internal static partial class Program
    {
        private static void SceneChangesCancelEveryObsoleteRequest()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumConcurrent: 2));
            VlmScheduleLease question = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.UserVisualQuestion,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What is there?"),
                1_010L).Lease!;
            VlmScheduleLease scene = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SignificantSceneChange,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe the scene."),
                1_020L).Lease!;

            VlmContextUpdateResult update = scheduler.UpdateContext(2UL, 1UL);
            Equal(2, update.CancelledRequestCount, "scene cancellation count");
            True(question.IsCancellationRequested, "question cancelled by scene");
            True(scene.IsCancellationRequested, "scene request cancelled by scene");
            Equal(2, scheduler.GetSnapshot(1_020L).Providers[0].ActiveRequestCount, "cancelled slots retained");
            True(scheduler.Complete(question.RequestId).WasCancellationRequested, "question completion cancelled");
            True(scheduler.Complete(scene.RequestId).WasCancellationRequested, "scene completion cancelled");
        }

        private static void QuestionChangesCancelOnlyQuestionBoundRequests()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumConcurrent: 2));
            VlmScheduleLease question = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.UserVisualQuestion,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What is there?"),
                1_010L).Lease!;
            VlmScheduleLease scene = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SignificantSceneChange,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe the scene."),
                1_020L).Lease!;

            VlmContextUpdateResult update = scheduler.UpdateContext(1UL, 2UL);
            Equal(1, update.CancelledRequestCount, "question cancellation count");
            True(question.IsCancellationRequested, "question cancelled");
            False(scene.IsCancellationRequested, "scene description retained");
        }

        private static void ContextRevisionRegressionIsNonMutating()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            scheduler.UpdateContext(2UL, 2UL);
            VlmSchedulerSnapshot before = scheduler.GetSnapshot(1_000L);
            VlmContextUpdateResult rejected = scheduler.UpdateContext(1UL, 2UL);
            Equal(VlmContextUpdateStatus.StaleRejected, rejected.Status, "context regression");
            VlmSchedulerSnapshot after = scheduler.GetSnapshot(1_000L);
            Equal(before.SceneRevision, after.SceneRevision, "scene remains unchanged");
            Equal(before.QuestionRevision, after.QuestionRevision, "question remains unchanged");
        }

        private static void StaleSignalsAreRejectedWithoutAdmission()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            scheduler.UpdateContext(2UL, 2UL);
            VlmScheduleDecision rejected = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL, sceneRevision: 1UL, questionRevision: 1UL),
                1_010L);
            Equal(VlmScheduleStatus.StaleContextRejected, rejected.Status, "stale signal");
            Equal(0L, scheduler.GetSnapshot(1_010L).Diagnostics.ScheduledRequestCount, "stale not admitted");
        }

        private static void SchedulingTimestampsCannotRegress()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumConcurrent: 2));
            VlmScheduleDecision first = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_100L);
            scheduler.Complete(first.Lease!.RequestId);
            VlmScheduleDecision stale = scheduler.TrySchedule(
                ManualSignal("vlm-device", 2UL),
                1_099L);
            Equal(VlmScheduleStatus.StaleTimestampRejected, stale.Status, "stale timestamp");
        }

        private static void CancellationCallbackFailuresRemainVisible()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleLease lease = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_010L).Lease!;
            using CancellationTokenRegistration registration = lease.CancellationToken.Register(
                static () => throw new InvalidOperationException("test callback failure"));
            VlmContextUpdateResult update = scheduler.UpdateContext(2UL, 1UL);
            Equal(VlmContextUpdateStatus.Accepted, update.Status, "callback failure context status");
            Equal(1L, scheduler.GetSnapshot(1_010L).Diagnostics.CancellationCallbackFailureCount, "callback failure diagnostic");
        }

        private static void CancellationDispatchDoesNotInvertCompletionLocks()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleLease lease = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_010L).Lease!;
            using var callbackEntered = new ManualResetEventSlim(false);
            using var allowCallback = new ManualResetEventSlim(false);
            Exception? updateFailure = null;
            Exception? completionFailure = null;
            VlmCompletionResult? completion = null;
            using CancellationTokenRegistration registration = lease.CancellationToken.Register(
                () =>
                {
                    callbackEntered.Set();
                    if (!allowCallback.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Cancellation callback release was not signalled.");
                    }
                    _ = scheduler.GetSnapshot(1_010L);
                });

            var updateThread = new Thread(
                () =>
                {
                    try
                    {
                        _ = scheduler.UpdateContext(2UL, 1UL);
                    }
                    catch (Exception exception)
                    {
                        updateFailure = exception;
                    }
                })
            {
                IsBackground = true,
                Name = "rma113-cancellation-dispatch",
            };
            updateThread.Start();
            True(callbackEntered.Wait(TimeSpan.FromSeconds(5)), "cancellation callback entered");

            var completionThread = new Thread(
                () =>
                {
                    try
                    {
                        completion = scheduler.Complete(lease.RequestId);
                    }
                    catch (Exception exception)
                    {
                        completionFailure = exception;
                    }
                })
            {
                IsBackground = true,
                Name = "rma113-concurrent-completion",
            };
            completionThread.Start();

            bool completionIsWaiting = SpinWait.SpinUntil(
                () => (completionThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));
            if (!completionIsWaiting)
            {
                allowCallback.Set();
                _ = updateThread.Join(TimeSpan.FromSeconds(1));
                _ = completionThread.Join(TimeSpan.FromSeconds(1));
                throw new InvalidOperationException(
                    "Concurrent completion did not reach its cancellation wait boundary.");
            }

            allowCallback.Set();
            True(updateThread.Join(TimeSpan.FromSeconds(5)), "cancellation dispatch completed");
            True(completionThread.Join(TimeSpan.FromSeconds(5)), "concurrent completion completed");
            if (updateFailure != null)
            {
                throw new InvalidOperationException("Cancellation dispatch failed.", updateFailure);
            }
            if (completionFailure != null)
            {
                throw new InvalidOperationException("Concurrent completion failed.", completionFailure);
            }
            True(completion != null, "completion result available");
            True(completion!.WasCancellationRequested, "completion retained cancellation state");
            True(lease.CancellationToken.IsCancellationRequested, "cached token remains readable");
            scheduler.Dispose();
        }

        private static void PriorityDegradationSuspendsAndCancelsRequests()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleLease lease = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_010L).Lease!;

            ReachyPriorityDegradationDecision suspended =
                new ReachyPriorityDegradationPolicy().Evaluate(
                    new ReachyPriorityDegradationSignals(
                        totalMemoryBytes: 12L * 1024L * 1024L * 1024L,
                        availableMemoryBytes: 8L * 1024L * 1024L * 1024L,
                        lowMemoryThresholdBytes: 512L * 1024L * 1024L,
                        systemReportsLowMemory: false,
                        LocalLlmThermalStatus.Moderate,
                        LocalLlmPhysicsBudgetState.Healthy));
            scheduler.ApplyPriorityDegradation(suspended);
            True(lease.IsCancellationRequested, "priority suspension cancels active VLM");

            VlmScheduleDecision blocked = scheduler.TrySchedule(
                ManualSignal("vlm-device", 2UL),
                1_020L);
            Equal(
                VlmScheduleStatus.ResourceSuspended,
                blocked.Status,
                "priority suspension blocks VLM admission");
            True(
                scheduler.Complete(lease.RequestId).WasCancellationRequested,
                "priority-cancelled completion remains visible");

            ReachyPriorityDegradationDecision nominal =
                new ReachyPriorityDegradationPolicy().Evaluate(
                    new ReachyPriorityDegradationSignals(
                        totalMemoryBytes: 12L * 1024L * 1024L * 1024L,
                        availableMemoryBytes: 8L * 1024L * 1024L * 1024L,
                        lowMemoryThresholdBytes: 512L * 1024L * 1024L,
                        systemReportsLowMemory: false,
                        LocalLlmThermalStatus.None,
                        LocalLlmPhysicsBudgetState.Healthy));
            scheduler.ApplyPriorityDegradation(nominal);
            Equal(
                VlmScheduleStatus.Scheduled,
                scheduler.TrySchedule(
                    ManualSignal("vlm-device", 2UL),
                    1_020L).Status,
                "priority recovery restores VLM admission");
        }

        private static void ApplicationInterruptionSuspendsAndCancelsRequests()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleLease lease = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_010L).Lease!;

            scheduler.PauseForApplicationInterruption();
            True(lease.IsCancellationRequested, "lifecycle pause cancels active VLM");
            Equal(
                VlmScheduleStatus.ResourceSuspended,
                scheduler.TrySchedule(
                    ManualSignal("vlm-device", 2UL),
                    1_020L).Status,
                "lifecycle pause blocks VLM admission");
            scheduler.PauseForApplicationInterruption();
            True(
                scheduler.Complete(lease.RequestId).WasCancellationRequested,
                "lifecycle cancellation remains visible after repeated pause");

            scheduler.ResumeAfterApplicationInterruption();
            scheduler.ResumeAfterApplicationInterruption();
            Equal(
                VlmScheduleStatus.Scheduled,
                scheduler.TrySchedule(
                    ManualSignal("vlm-device", 2UL),
                    1_020L).Status,
                "lifecycle resume permits only new VLM work");
        }

        private static void ProviderPolicyStateIsBounded()
        {
            var policies = new List<VlmProviderSchedulingPolicy>();
            for (int index = 0; index < ReachyVlmScheduler.MaximumProviderPolicies + 1; ++index)
            {
                policies.Add(OnDevicePolicy(instanceId: "vlm-" + index));
            }
            Throws<ArgumentOutOfRangeException>(
                () => DisposeScheduler(new ReachyVlmScheduler(
                    policies,
                    VlmSchedulerOptions.ExplicitTriggersOnly,
                    1UL,
                    1UL,
                    1_000L)),
                "provider policy bound");

            Throws<ArgumentException>(
                () => DisposeScheduler(new ReachyVlmScheduler(
                    new[] { OnDevicePolicy(), OnDevicePolicy() },
                    VlmSchedulerOptions.ExplicitTriggersOnly,
                    1UL,
                    1UL,
                    1_000L)),
                "duplicate provider policy");
        }

        private static void SnapshotsAreImmutableCopies()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmSchedulerSnapshot snapshot = scheduler.GetSnapshot(1_000L);
            Throws<NotSupportedException>(
                () => ((IList<VlmProviderSchedulerSnapshot>)snapshot.Providers).Clear(),
                "provider snapshot immutable");
            scheduler.TrySchedule(ManualSignal("vlm-device", 1UL), 1_010L);
            Equal(0, snapshot.Providers[0].ActiveRequestCount, "old snapshot unchanged");
        }

        private static void UnknownCompletionIsVisible()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmCompletionResult result = scheduler.Complete("not-active");
            Equal(VlmCompletionStatus.UnknownRequest, result.Status, "unknown completion status");
            Equal(1L, scheduler.GetSnapshot(1_000L).Diagnostics.UnknownCompletionCount, "unknown completion counter");
        }

        private static void SourceContractRemainsExplicit()
        {
            // ReachyVlmSchedulingPolicy.cs was split (docs/LARGE_FILE_REFACTOR_TODO.md,
            // file #10) into several top-level ReachyVlm*.cs files. Concatenate every
            // split-out piece so this contract check keeps covering the same source,
            // regardless of which file each token now lives in.
            const string directory = "Assets/ReachyMini/Runtime/Core/Perception";
            var builder = new System.Text.StringBuilder();
            foreach (string sourceFile in Directory.GetFiles(directory, "ReachyVlm*.cs"))
            {
                builder.Append(File.ReadAllText(sourceFile));
            }
            string source = builder.ToString();
            Contains(source, "UserVisualQuestion", "user question trigger");
            Contains(source, "PlannerRequest", "planner trigger");
            Contains(source, "SignificantSceneChange", "scene trigger");
            Contains(source, "NewEntity", "new entity trigger");
            Contains(source, "ManualRequest", "manual trigger");
            Contains(source, "SlowInterval", "slow trigger");
            Contains(source, "MaximumRequestsPerWindow", "rate bound");
            Contains(source, "MaximumConcurrentOperations", "concurrency bound");
            Contains(source, "DisclosureRequired", "disclosure gate");
            Contains(source, "no fallback provider", "no fallback contract");
            Contains(source, "MarkCancellationRequested", "obsolete cancellation contract");
            Contains(source, "CancellationCallbackFailureCount", "cancellation failure visibility");
            Contains(source, "Monitor.Wait(cancellationSync)", "completion waits without scheduler lock");
            Contains(source, "cancellationToken = cancellation.Token", "cached cancellation token");
            DoesNotContain(source, "CameraFrame", "no frame-rate trigger");
            DoesNotContain(source, "catch (Exception", "no broad exception fallback");
        }

    }
}
