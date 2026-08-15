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
    internal static class Program
    {
        private static int Main()
        {
            ExplicitOnlyDefaultDisablesSlowInterval();
            UserVisualQuestionsScheduleExplicitly();
            AutonomousTriggersRequireSceneDescriptions();
            DuplicateAndRegressedTriggersAreSuppressed();
            SlowIntervalUsesAnExactConfiguredBoundary();
            ProviderRateLimitUsesAnExactWindowBoundary();
            ConcurrencySlotsRemainOwnedUntilCompletion();
            ProviderLimitsRemainIndependent();
            UnknownProvidersNeverFallback();
            CloudDisclosureIsRequiredAndSurfaced();
            CloudCostAcknowledgementIsIndependent();
            LocalNetworkProvidersRequireOnlyNetworkDisclosure();
            ProviderCapabilitiesAreEnforced();
            ProviderPromptLimitsAreEnforced();
            SceneChangesCancelEveryObsoleteRequest();
            QuestionChangesCancelOnlyQuestionBoundRequests();
            ContextRevisionRegressionIsNonMutating();
            StaleSignalsAreRejectedWithoutAdmission();
            SchedulingTimestampsCannotRegress();
            CancellationCallbackFailuresRemainVisible();
            CancellationDispatchDoesNotInvertCompletionLocks();
            PriorityDegradationSuspendsAndCancelsRequests();
            ProviderPolicyStateIsBounded();
            SnapshotsAreImmutableCopies();
            UnknownCompletionIsVisible();
            SourceContractRemainsExplicit();
            Console.WriteLine("RMA-113 VLM scheduling-policy contracts passed.");
            return 0;
        }

        private static void ExplicitOnlyDefaultDisablesSlowInterval()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleDecision decision = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SlowInterval,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe the scene."),
                1_100L);
            Equal(VlmScheduleStatus.TriggerDisabled, decision.Status, "default slow interval");
            Equal(0, scheduler.GetSnapshot(1_100L).Providers[0].ActiveRequestCount, "no slow request");
        }

        private static void UserVisualQuestionsScheduleExplicitly()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleDecision decision = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.UserVisualQuestion,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What is in front of me?"),
                1_010L);
            Equal(VlmScheduleStatus.Scheduled, decision.Status, "user question status");
            True(decision.Lease != null, "user question lease");
            Equal(1UL, decision.Lease!.QuestionRevision, "question binding");
            False(decision.Lease.Disclosure.NetworkRequired, "on-device network disclosure");
        }

        private static void AutonomousTriggersRequireSceneDescriptions()
        {
            Throws<ArgumentException>(
                () => Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SignificantSceneChange,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What changed?"),
                "scene trigger operation");
            Throws<ArgumentException>(
                () => Signal(
                    "vlm-device",
                    VlmScheduleTrigger.NewEntity,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What appeared?"),
                "new entity operation");
        }

        private static void DuplicateAndRegressedTriggersAreSuppressed()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumConcurrent: 2));
            VlmScheduleDecision first = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.NewEntity,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 4UL,
                    prompt: "Describe the new entity."),
                1_010L);
            Equal(VlmScheduleStatus.Scheduled, first.Status, "first new entity");
            scheduler.Complete(first.Lease!.RequestId);

            VlmScheduleDecision duplicate = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.NewEntity,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 4UL,
                    prompt: "Describe the new entity."),
                1_020L);
            Equal(VlmScheduleStatus.DuplicateSuppressed, duplicate.Status, "duplicate trigger");

            VlmScheduleDecision regressed = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.NewEntity,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 3UL,
                    prompt: "Describe the new entity."),
                1_030L);
            Equal(VlmScheduleStatus.DuplicateSuppressed, regressed.Status, "regressed trigger");
        }

        private static void SlowIntervalUsesAnExactConfiguredBoundary()
        {
            var options = new VlmSchedulerOptions(100L, "Describe the scene slowly.");
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(), options);
            VlmScheduleDecision early = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SlowInterval,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe the scene slowly."),
                1_099L);
            Equal(VlmScheduleStatus.IntervalNotDue, early.Status, "slow early status");
            Equal(1L, early.RetryAfterNanoseconds, "slow retry boundary");

            VlmScheduleDecision due = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.SlowInterval,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe the scene slowly."),
                1_100L);
            Equal(VlmScheduleStatus.Scheduled, due.Status, "slow exact boundary");
        }

        private static void ProviderRateLimitUsesAnExactWindowBoundary()
        {
            using ReachyVlmScheduler scheduler = Scheduler(
                OnDevicePolicy(maximumRequestsPerWindow: 1, rateWindowNanoseconds: 100L));
            VlmScheduleDecision first = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_000L);
            scheduler.Complete(first.Lease!.RequestId);

            VlmScheduleDecision limited = scheduler.TrySchedule(
                ManualSignal("vlm-device", 2UL),
                1_099L);
            Equal(VlmScheduleStatus.RateLimited, limited.Status, "rate limited");
            Equal(1L, limited.RetryAfterNanoseconds, "rate retry boundary");

            VlmScheduleDecision released = scheduler.TrySchedule(
                ManualSignal("vlm-device", 2UL),
                1_100L);
            Equal(VlmScheduleStatus.Scheduled, released.Status, "rate exact boundary");
        }

        private static void ConcurrencySlotsRemainOwnedUntilCompletion()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumConcurrent: 1));
            VlmScheduleDecision first = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL),
                1_010L);
            VlmScheduleDecision blocked = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.PlannerRequest,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe for the planner."),
                1_020L);
            Equal(VlmScheduleStatus.ConcurrencyLimited, blocked.Status, "concurrency blocked");

            scheduler.Complete(first.Lease!.RequestId);
            VlmScheduleDecision admitted = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.PlannerRequest,
                    VlmSemanticOperation.SceneDescription,
                    sequence: 1UL,
                    prompt: "Describe for the planner."),
                1_020L);
            Equal(VlmScheduleStatus.Scheduled, admitted.Status, "concurrency released");
        }

        private static void ProviderLimitsRemainIndependent()
        {
            using var scheduler = new ReachyVlmScheduler(
                new[]
                {
                    OnDevicePolicy(instanceId: "vlm-a", maximumConcurrent: 1),
                    OnDevicePolicy(instanceId: "vlm-b", maximumConcurrent: 1),
                },
                VlmSchedulerOptions.ExplicitTriggersOnly,
                initialSceneRevision: 1UL,
                initialQuestionRevision: 1UL,
                startTimestampNanoseconds: 1_000L);

            Equal(
                VlmScheduleStatus.Scheduled,
                scheduler.TrySchedule(ManualSignal("vlm-a", 1UL), 1_010L).Status,
                "provider a admitted");
            Equal(
                VlmScheduleStatus.Scheduled,
                scheduler.TrySchedule(ManualSignal("vlm-b", 1UL), 1_010L).Status,
                "provider b admitted");
        }

        private static void UnknownProvidersNeverFallback()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy());
            VlmScheduleDecision decision = scheduler.TrySchedule(
                ManualSignal("missing-provider", 1UL),
                1_010L);
            Equal(VlmScheduleStatus.ProviderUnavailable, decision.Status, "unknown provider");
            Contains(decision.Diagnostic, "no fallback", "no fallback diagnostic");
            Equal(0L, scheduler.GetSnapshot(1_010L).Diagnostics.ScheduledRequestCount, "no fallback admission");
        }

        private static void CloudDisclosureIsRequiredAndSurfaced()
        {
            using ReachyVlmScheduler scheduler = Scheduler(CloudPolicy());
            VlmScheduleDecision blocked = scheduler.TrySchedule(
                ManualSignal("vlm-cloud", 1UL),
                1_010L);
            Equal(VlmScheduleStatus.DisclosureRequired, blocked.Status, "cloud disclosure status");
            True(blocked.Disclosure != null, "cloud disclosure snapshot");
            True(blocked.Disclosure!.NetworkRequired, "cloud network required");
            True(blocked.Disclosure.CostRequired, "cloud cost required");
            Contains(blocked.Disclosure.NetworkDisclosure!, "image", "cloud network disclosure text");
            Contains(blocked.Disclosure.CostDisclosure!, "cost", "cloud cost disclosure text");

            VlmScheduleDecision admitted = scheduler.TrySchedule(
                ManualSignal(
                    "vlm-cloud",
                    1UL,
                    networkAcknowledged: true,
                    costAcknowledged: true),
                1_010L);
            Equal(VlmScheduleStatus.Scheduled, admitted.Status, "cloud acknowledged");
        }

        private static void CloudCostAcknowledgementIsIndependent()
        {
            using ReachyVlmScheduler scheduler = Scheduler(CloudPolicy());
            VlmScheduleDecision blocked = scheduler.TrySchedule(
                ManualSignal(
                    "vlm-cloud",
                    1UL,
                    networkAcknowledged: true,
                    costAcknowledged: false),
                1_010L);
            Equal(VlmScheduleStatus.DisclosureRequired, blocked.Status, "cloud cost acknowledgement");
            True(blocked.Disclosure!.NetworkAcknowledged, "network acknowledged");
            False(blocked.Disclosure.CostAcknowledged, "cost not acknowledged");
        }

        private static void LocalNetworkProvidersRequireOnlyNetworkDisclosure()
        {
            using ReachyVlmScheduler scheduler = Scheduler(LocalNetworkPolicy());
            VlmScheduleDecision blocked = scheduler.TrySchedule(
                ManualSignal("vlm-lan", 1UL),
                1_010L);
            Equal(VlmScheduleStatus.DisclosureRequired, blocked.Status, "LAN disclosure");
            False(blocked.Disclosure!.CostRequired, "LAN cost not required");

            VlmScheduleDecision admitted = scheduler.TrySchedule(
                ManualSignal("vlm-lan", 1UL, networkAcknowledged: true),
                1_010L);
            Equal(VlmScheduleStatus.Scheduled, admitted.Status, "LAN acknowledged");
        }

        private static void ProviderCapabilitiesAreEnforced()
        {
            using ReachyVlmScheduler scheduler = Scheduler(
                OnDevicePolicy(supportsVisualQuestions: false, supportsSceneDescription: true));
            VlmScheduleDecision decision = scheduler.TrySchedule(
                Signal(
                    "vlm-device",
                    VlmScheduleTrigger.UserVisualQuestion,
                    VlmSemanticOperation.VisualQuestion,
                    sequence: 1UL,
                    prompt: "What is there?"),
                1_010L);
            Equal(VlmScheduleStatus.CapabilityUnsupported, decision.Status, "visual capability");
        }

        private static void ProviderPromptLimitsAreEnforced()
        {
            using ReachyVlmScheduler scheduler = Scheduler(OnDevicePolicy(maximumPromptCharacters: 8));
            VlmScheduleDecision decision = scheduler.TrySchedule(
                ManualSignal("vlm-device", 1UL, prompt: "This prompt is too long."),
                1_010L);
            Equal(VlmScheduleStatus.CapabilityUnsupported, decision.Status, "prompt limit");
        }

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

        private static ReachyVlmScheduler Scheduler(
            VlmProviderSchedulingPolicy policy,
            VlmSchedulerOptions? options = null)
        {
            return new ReachyVlmScheduler(
                new[] { policy },
                options ?? VlmSchedulerOptions.ExplicitTriggersOnly,
                initialSceneRevision: 1UL,
                initialQuestionRevision: 1UL,
                startTimestampNanoseconds: 1_000L);
        }

        private static VlmProviderSchedulingPolicy OnDevicePolicy(
            string instanceId = "vlm-device",
            int maximumConcurrent = 2,
            int maximumRequestsPerWindow = 8,
            long rateWindowNanoseconds = 1_000L,
            bool supportsVisualQuestions = true,
            bool supportsSceneDescription = true,
            int maximumPromptCharacters = 256)
        {
            return Policy(
                instanceId,
                VisionProviderLocation.OnDevice,
                maximumConcurrent,
                maximumRequestsPerWindow,
                rateWindowNanoseconds,
                supportsVisualQuestions,
                supportsSceneDescription,
                maximumPromptCharacters,
                null,
                null);
        }

        private static VlmProviderSchedulingPolicy CloudPolicy()
        {
            return Policy(
                "vlm-cloud",
                VisionProviderLocation.Cloud,
                maximumConcurrent: 2,
                maximumRequestsPerWindow: 4,
                rateWindowNanoseconds: 1_000L,
                supportsVisualQuestions: true,
                supportsSceneDescription: true,
                maximumPromptCharacters: 256,
                networkDisclosure: "A transformed valid image will leave this device.",
                costDisclosure: "This cloud request may incur provider cost.");
        }

        private static VlmProviderSchedulingPolicy LocalNetworkPolicy()
        {
            return Policy(
                "vlm-lan",
                VisionProviderLocation.LocalNetwork,
                maximumConcurrent: 1,
                maximumRequestsPerWindow: 4,
                rateWindowNanoseconds: 1_000L,
                supportsVisualQuestions: true,
                supportsSceneDescription: true,
                maximumPromptCharacters: 256,
                networkDisclosure: "A transformed valid image will be sent to the selected local-network provider.",
                costDisclosure: null);
        }

        private static VlmProviderSchedulingPolicy Policy(
            string instanceId,
            VisionProviderLocation location,
            int maximumConcurrent,
            int maximumRequestsPerWindow,
            long rateWindowNanoseconds,
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            int maximumPromptCharacters,
            string? networkDisclosure,
            string? costDisclosure)
        {
            var descriptor = new ProviderDescriptor(
                VisionProviderKind.SemanticVisionLanguage,
                "vlm-provider",
                instanceId,
                "Test VLM",
                "1.0",
                location);
            var capabilities = new VisionLanguageCapabilities(
                supportsVisualQuestions,
                supportsSceneDescription,
                supportsCancellation: true,
                maximumConcurrentOperations: Math.Max(2, maximumConcurrent),
                maximumPromptCharacters);
            return new VlmProviderSchedulingPolicy(
                descriptor,
                capabilities,
                maximumConcurrent,
                maximumRequestsPerWindow,
                rateWindowNanoseconds,
                networkDisclosure,
                costDisclosure);
        }

        private static VlmScheduleSignal ManualSignal(
            string providerInstanceId,
            ulong sequence,
            bool networkAcknowledged = false,
            bool costAcknowledged = false,
            string prompt = "Describe the current scene.",
            ulong sceneRevision = 1UL,
            ulong questionRevision = 1UL)
        {
            return Signal(
                providerInstanceId,
                VlmScheduleTrigger.ManualRequest,
                VlmSemanticOperation.SceneDescription,
                sequence,
                prompt,
                sceneRevision,
                questionRevision,
                networkAcknowledged,
                costAcknowledged);
        }

        private static VlmScheduleSignal Signal(
            string providerInstanceId,
            VlmScheduleTrigger trigger,
            VlmSemanticOperation operation,
            ulong sequence,
            string prompt,
            ulong sceneRevision = 1UL,
            ulong questionRevision = 1UL,
            bool networkAcknowledged = false,
            bool costAcknowledged = false)
        {
            return new VlmScheduleSignal(
                providerInstanceId,
                trigger,
                operation,
                sequence,
                sceneRevision,
                questionRevision,
                prompt,
                networkAcknowledged,
                costAcknowledged);
        }

        private static void DisposeScheduler(ReachyVlmScheduler scheduler)
        {
            scheduler.Dispose();
        }

        private static void True(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name + " expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                throw new InvalidOperationException(name + " expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected=" + expected + " actual=" + actual + ".");
            }
        }

        private static void Contains(string value, string expected, string name)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(name + " missing '" + expected + "'.");
            }
        }

        private static void DoesNotContain(string value, string unexpected, string name)
        {
            if (value.Contains(unexpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " unexpectedly contained '" + unexpected + "'.");
            }
        }

        private static void Throws<TException>(Action action, string name)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                name + " expected " + typeof(TException).Name + ".");
        }
    }
}
