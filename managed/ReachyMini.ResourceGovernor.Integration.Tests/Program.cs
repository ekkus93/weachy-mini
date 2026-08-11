#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using ReachyMini.Simulation;

namespace ReachyMini.ResourceGovernor.Integration.Tests
{
    internal static class Program
    {
        private const long GiB = 1024L * 1024L * 1024L;
        private static int failures;

        public static int Main()
        {
            Run("stalled physics is unavailable", StalledPhysicsIsUnavailable);
            Run("admission returns reduced profile", AdmissionReturnsReducedProfile);
            Run("admission fails closed on signal exception", AdmissionSignalFailure);
            Run("preflight blocks oversized loaded profile", () =>
                PreflightBlocksOversizedLoadedProfile().GetAwaiter().GetResult());
            Run("monitor cancels on stronger pressure", () =>
                MonitorCancelsOnStrongerPressure().GetAwaiter().GetResult());
            Run("monitor signal failure cancels", () =>
                MonitorSignalFailureCancels().GetAwaiter().GetResult());
            Run("coordinator rejects hidden queue", () =>
                CoordinatorRejectsHiddenQueue().GetAwaiter().GetResult());
            Run("provider OOM latches governor", () =>
                ProviderOomLatchesGovernor().GetAwaiter().GetResult());

            Console.WriteLine(failures == 0
                ? "RMA-135 governed-generation integration contracts passed."
                : $"RMA-135 governed-generation integration contracts failed: {failures}.");
            return failures == 0 ? 0 : 1;
        }

        private static void StalledPhysicsIsUnavailable()
        {
            var tracker = new ReachyLocalLlmPhysicsBudgetTracker(0.005);
            Equal(LocalLlmPhysicsBudgetState.Unavailable, tracker.Observe(Timing(100, 0, 0.0, 0.001)));
            Equal(LocalLlmPhysicsBudgetState.Unavailable, tracker.Observe(Timing(100, 0, 0.0, 0.001)));
            Equal(LocalLlmPhysicsBudgetState.Healthy, tracker.Observe(Timing(101, 0, 0.0, 0.001)));
        }

        private static void AdmissionReturnsReducedProfile()
        {
            var governor = new LocalLlmResourceGovernor();
            LocalLlmProviderAdmissionResult result =
                LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                    Baseline(),
                    governor,
                    new QueueResourceSignals(new[] { Snapshot(LocalLlmThermalStatus.Light) }),
                    new QueuePhysicsBudgetSource(new[] { LocalLlmPhysicsBudgetState.Healthy }));
            Equal(LocalLlmProviderAdmissionStatus.Ready, result.Status);
            LocalLlmExecutionProfile profile = result.EffectiveProfile ??
                throw new InvalidOperationException("Expected reduced admission profile.");
            Equal(1536, profile.ContextTokens);
            Equal(128, profile.BatchTokens);
            Equal(3, profile.Threads);
        }

        private static void AdmissionSignalFailure()
        {
            LocalLlmProviderAdmissionResult result =
                LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                    Baseline(),
                    new LocalLlmResourceGovernor(),
                    new ThrowingResourceSignals(),
                    new QueuePhysicsBudgetSource(new[] { LocalLlmPhysicsBudgetState.Healthy }));
            Equal(LocalLlmProviderAdmissionStatus.SignalFailure, result.Status);
            if (result.Succeeded || result.EffectiveProfile != null)
            {
                throw new InvalidOperationException("Signal failure exposed an executable profile.");
            }
        }

        private static async Task PreflightBlocksOversizedLoadedProfile()
        {
            var executor = new FakeExecutor(Baseline(), CompleteImmediately);
            var coordinator = Coordinator(
                executor,
                new QueueResourceSignals(new[] { Snapshot(LocalLlmThermalStatus.Light) }),
                new QueuePhysicsBudgetSource(new[] { LocalLlmPhysicsBudgetState.Healthy }));
            LocalLlmGovernedGenerationResult result = await coordinator.GenerateAsync(
                Request("preflight-block"), new NullSink(), CancellationToken.None);
            Equal(LocalLlmGovernedGenerationStatus.ResourceSuspendedBeforeStart, result.Status);
            Equal(0, executor.InvocationCount);
        }

        private static async Task MonitorCancelsOnStrongerPressure()
        {
            var executor = new FakeExecutor(Baseline(), WaitForCancellation);
            var coordinator = Coordinator(
                executor,
                new QueueResourceSignals(new[]
                {
                    Snapshot(LocalLlmThermalStatus.None),
                    Snapshot(LocalLlmThermalStatus.Severe),
                }),
                new QueuePhysicsBudgetSource(new[]
                {
                    LocalLlmPhysicsBudgetState.Healthy,
                    LocalLlmPhysicsBudgetState.Healthy,
                }));
            LocalLlmGovernedGenerationResult result = await coordinator.GenerateAsync(
                Request("pressure-cancel"), new NullSink(), CancellationToken.None);
            Equal(LocalLlmGovernedGenerationStatus.ResourceCancelledDuringGeneration, result.Status);
            Equal(LocalLlmGenerationStatus.Cancelled, result.ProviderResult?.Status);
            Equal(1, executor.InvocationCount);
        }

        private static async Task MonitorSignalFailureCancels()
        {
            var executor = new FakeExecutor(Baseline(), WaitForCancellation);
            var coordinator = Coordinator(
                executor,
                new FailAfterOneResourceSignal(Snapshot(LocalLlmThermalStatus.None)),
                new QueuePhysicsBudgetSource(new[]
                {
                    LocalLlmPhysicsBudgetState.Healthy,
                    LocalLlmPhysicsBudgetState.Healthy,
                }));
            LocalLlmGovernedGenerationResult result = await coordinator.GenerateAsync(
                Request("signal-cancel"), new NullSink(), CancellationToken.None);
            Equal(LocalLlmGovernedGenerationStatus.SignalFailure, result.Status);
            Equal(LocalLlmGenerationStatus.Cancelled, result.ProviderResult?.Status);
        }

        private static async Task ProviderOomLatchesGovernor()
        {
            var governor = new LocalLlmResourceGovernor();
            var executor = new FakeExecutor(Baseline(), (request, sink, token) =>
                Task.FromResult(Generation(LocalLlmGenerationStatus.ResourceExhausted, request.RequestId)));
            var coordinator = new LocalLlmGovernedGenerationCoordinator(
                executor,
                Baseline(),
                governor,
                new RepeatingResourceSignals(Snapshot(LocalLlmThermalStatus.None)),
                new RepeatingPhysicsBudgetSource(LocalLlmPhysicsBudgetState.Healthy),
                TimeSpan.FromMilliseconds(25));
            LocalLlmGovernedGenerationResult result = await coordinator.GenerateAsync(
                Request("oom-latch"), new NullSink(), CancellationToken.None);
            Equal(LocalLlmGovernedGenerationStatus.ResourceExhausted, result.Status);
            Equal(1, executor.InvocationCount);
            if (!governor.OutOfMemoryLatched)
            {
                throw new InvalidOperationException("Coordinator did not latch provider OOM.");
            }
            Equal(LocalLlmGovernorMode.Suspended, coordinator.EvaluateCurrentBudget().Mode);
            Equal(LocalLlmGovernorMode.Suspended, coordinator.EvaluateCurrentBudget().Mode);
            Equal(LocalLlmGovernorMode.Nominal, coordinator.EvaluateCurrentBudget().Mode);
            Equal(1, executor.InvocationCount);
        }

        private static async Task CoordinatorRejectsHiddenQueue()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new FakeExecutor(Baseline(), async (request, sink, token) =>
            {
                try
                {
                    await release.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                return Generation(LocalLlmGenerationStatus.Cancelled, request.RequestId);
            });
            var coordinator = Coordinator(
                executor,
                new RepeatingResourceSignals(Snapshot(LocalLlmThermalStatus.None)),
                new RepeatingPhysicsBudgetSource(LocalLlmPhysicsBudgetState.Healthy));
            using var cancel = new CancellationTokenSource();
            Task<LocalLlmGovernedGenerationResult> first = coordinator.GenerateAsync(
                Request("busy-first"), new NullSink(), cancel.Token);
            await Task.Delay(10);
            LocalLlmGovernedGenerationResult second = await coordinator.GenerateAsync(
                Request("busy-second"), new NullSink(), CancellationToken.None);
            Equal(LocalLlmGovernedGenerationStatus.Busy, second.Status);
            Equal(1, executor.InvocationCount);
            cancel.Cancel();
            release.TrySetResult(true);
            await first;
        }

        private static LocalLlmGovernedGenerationCoordinator Coordinator(
            ILocalLlmGenerationExecutor executor,
            ILocalLlmResourceSignalSource resources,
            ILocalLlmPhysicsBudgetSource physics)
        {
            return new LocalLlmGovernedGenerationCoordinator(
                executor,
                Baseline(),
                new LocalLlmResourceGovernor(),
                resources,
                physics,
                TimeSpan.FromMilliseconds(25));
        }

        private static LocalLlmGenerationRequest Request(string id) =>
            new LocalLlmGenerationRequest(
                id,
                new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, "hello") });

        private static Task<LocalLlmGenerationResult> CompleteImmediately(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken) =>
            Task.FromResult(Generation(LocalLlmGenerationStatus.Succeeded, request.RequestId));

        private static async Task<LocalLlmGenerationResult> WaitForCancellation(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            return Generation(LocalLlmGenerationStatus.Cancelled, request.RequestId);
        }

        private static LocalLlmGenerationResult Generation(LocalLlmGenerationStatus status, string requestId) =>
            new LocalLlmGenerationResult(status, requestId, 1UL, status.ToString(), 0, null, null);

        private static LocalLlmExecutionProfile Baseline() =>
            LocalLlmExecutionProfile.CreateRma133V6Baseline();

        private static LocalLlmResourceSnapshot Snapshot(LocalLlmThermalStatus thermal) =>
            new LocalLlmResourceSnapshot(
                12L * GiB,
                8L * GiB,
                GiB / 2L,
                false,
                8,
                thermal,
                LocalLlmPhysicsBudgetState.Healthy);

        private static ReachySimulationTimingSnapshot Timing(
            ulong steps,
            ulong misses,
            double lag,
            double lastStep) =>
            new ReachySimulationTimingSnapshot(steps, misses, 0UL, 0UL, 0UL, lag, lastStep, lastStep);

        private sealed class NullSink : ILocalLlmStreamSink
        {
            public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken) =>
                ValueTask.CompletedTask;
        }

        private sealed class FakeExecutor : ILocalLlmGenerationExecutor
        {
            private readonly Func<LocalLlmGenerationRequest, ILocalLlmStreamSink, CancellationToken, Task<LocalLlmGenerationResult>> generate;

            internal FakeExecutor(
                LocalLlmExecutionProfile executionProfile,
                Func<LocalLlmGenerationRequest, ILocalLlmStreamSink, CancellationToken, Task<LocalLlmGenerationResult>> generate)
            {
                ExecutionProfile = executionProfile;
                this.generate = generate;
            }

            public LocalLlmExecutionProfile ExecutionProfile { get; }
            public int InvocationCount { get; private set; }

            public Task<LocalLlmGenerationResult> GenerateAsync(
                LocalLlmGenerationRequest request,
                ILocalLlmStreamSink sink,
                CancellationToken cancellationToken)
            {
                ++InvocationCount;
                return generate(request, sink, cancellationToken);
            }
        }

        private sealed class QueuePhysicsBudgetSource : ILocalLlmPhysicsBudgetSource
        {
            private readonly Queue<LocalLlmPhysicsBudgetState> values;
            internal QueuePhysicsBudgetSource(IEnumerable<LocalLlmPhysicsBudgetState> values) =>
                this.values = new Queue<LocalLlmPhysicsBudgetState>(values);
            public LocalLlmPhysicsBudgetState Capture() => values.Count > 0
                ? values.Dequeue()
                : throw new InvalidOperationException("Physics test signal queue exhausted.");
        }

        private sealed class RepeatingPhysicsBudgetSource : ILocalLlmPhysicsBudgetSource
        {
            private readonly LocalLlmPhysicsBudgetState value;
            internal RepeatingPhysicsBudgetSource(LocalLlmPhysicsBudgetState value) => this.value = value;
            public LocalLlmPhysicsBudgetState Capture() => value;
        }

        private sealed class QueueResourceSignals : ILocalLlmResourceSignalSource
        {
            private readonly Queue<LocalLlmResourceSnapshot> values;
            internal QueueResourceSignals(IEnumerable<LocalLlmResourceSnapshot> values) =>
                this.values = new Queue<LocalLlmResourceSnapshot>(values);
            public LocalLlmResourceSnapshot Capture(LocalLlmPhysicsBudgetState physicsBudgetState) =>
                WithPhysics(values.Count > 0
                    ? values.Dequeue()
                    : throw new InvalidOperationException("Resource test signal queue exhausted."),
                    physicsBudgetState);
        }

        private sealed class RepeatingResourceSignals : ILocalLlmResourceSignalSource
        {
            private readonly LocalLlmResourceSnapshot value;
            internal RepeatingResourceSignals(LocalLlmResourceSnapshot value) => this.value = value;
            public LocalLlmResourceSnapshot Capture(LocalLlmPhysicsBudgetState physicsBudgetState) =>
                WithPhysics(value, physicsBudgetState);
        }

        private sealed class ThrowingResourceSignals : ILocalLlmResourceSignalSource
        {
            public LocalLlmResourceSnapshot Capture(LocalLlmPhysicsBudgetState physicsBudgetState) =>
                throw new InvalidOperationException("synthetic resource signal failure");
        }

        private sealed class FailAfterOneResourceSignal : ILocalLlmResourceSignalSource
        {
            private readonly LocalLlmResourceSnapshot first;
            private int calls;
            internal FailAfterOneResourceSignal(LocalLlmResourceSnapshot first) => this.first = first;
            public LocalLlmResourceSnapshot Capture(LocalLlmPhysicsBudgetState physicsBudgetState)
            {
                if (calls++ != 0)
                {
                    throw new InvalidOperationException("synthetic monitor signal failure");
                }
                return WithPhysics(first, physicsBudgetState);
            }
        }

        private static LocalLlmResourceSnapshot WithPhysics(
            LocalLlmResourceSnapshot value,
            LocalLlmPhysicsBudgetState physics) =>
            new LocalLlmResourceSnapshot(
                value.TotalMemoryBytes,
                value.AvailableMemoryBytes,
                value.LowMemoryThresholdBytes,
                value.SystemReportsLowMemory,
                value.LogicalProcessorCount,
                value.ThermalStatus,
                physics,
                value.RecentOutOfMemory);

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception exception)
            {
                ++failures;
                Console.Error.WriteLine("FAIL: " + name + ": " + exception.Message);
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
            }
        }
    }
}
