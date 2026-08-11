#nullable enable

using System;
using ReachyMini.AppState;
using ReachyMini.LocalModels;
using ReachyMini.Simulation;

namespace ReachyMini.ResourceGovernor.Tests
{
    internal static class Program
    {
        private const long GiB = 1024L * 1024L * 1024L;
        private static int failures;

        public static int Main()
        {
            Run("nominal performance", () =>
            {
                LocalLlmGovernorDecision d = Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy);
                Equal(LocalLlmGovernorMode.Nominal, d.Mode);
                Equal(LocalLlmDeviceProfileKind.Performance, d.DeviceProfile.Kind);
                Profile(d, 2048, 256, 4);
            });
            Run("conservative device cap", () =>
            {
                LocalLlmGovernorDecision d = Eval(4, 3, 0.25, 4, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy);
                Equal(LocalLlmDeviceProfileKind.Conservative, d.DeviceProfile.Kind);
                Reason(d, LocalLlmGovernorReason.DeviceProfileLimit);
                Profile(d, 1024, 128, 2);
            });
            Run("light thermal reduced", () =>
            {
                LocalLlmGovernorDecision d = Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.Light, LocalLlmPhysicsBudgetState.Healthy);
                Equal(LocalLlmGovernorMode.Reduced, d.Mode);
                Reason(d, LocalLlmGovernorReason.ThermalLight);
                Profile(d, 1536, 128, 3);
            });
            Run("moderate thermal minimal", () =>
            {
                LocalLlmGovernorDecision d = Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.Moderate, LocalLlmPhysicsBudgetState.Healthy);
                Equal(LocalLlmGovernorMode.Minimal, d.Mode);
                Reason(d, LocalLlmGovernorReason.ThermalModerate);
                Profile(d, 1024, 64, 1);
            });
            Run("severe thermal suspended", () =>
            {
                LocalLlmGovernorDecision d = Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.Severe, LocalLlmPhysicsBudgetState.Healthy);
                Suspended(d, LocalLlmGovernorReason.ThermalSevereOrWorse);
            });
            Run("android low memory suspended", () =>
            {
                var governor = new LocalLlmResourceGovernor();
                LocalLlmGovernorDecision d = governor.Evaluate(Baseline(), new LocalLlmResourceSnapshot(
                    8L * GiB, 2L * GiB, GiB / 2L, true, 8,
                    LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy));
                Suspended(d, LocalLlmGovernorReason.AndroidLowMemory);
                Reason(d, LocalLlmGovernorReason.MemoryCritical);
            });
            Run("memory pressure tiers", () =>
            {
                Equal(LocalLlmGovernorMode.Reduced, Eval(12, 2.5, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy).Mode);
                Equal(LocalLlmGovernorMode.Minimal, Eval(12, 1.5, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy).Mode);
                Suspended(Eval(12, 0.8, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy), LocalLlmGovernorReason.MemoryCritical);
            });
            Run("physics priority", () =>
            {
                LocalLlmGovernorDecision risk = Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.AtRisk);
                Equal(LocalLlmGovernorMode.Minimal, risk.Mode);
                Reason(risk, LocalLlmGovernorReason.PhysicsAtRisk);
                Suspended(Eval(12, 8, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Exceeded), LocalLlmGovernorReason.PhysicsBudgetExceeded);
            });
            Run("unknown signals fail closed for physics", () =>
            {
                var governor = new LocalLlmResourceGovernor();
                LocalLlmGovernorDecision d = governor.Evaluate(Baseline(), new LocalLlmResourceSnapshot(
                    0L, 0L, 0L, false, 0,
                    LocalLlmThermalStatus.Unavailable, LocalLlmPhysicsBudgetState.Unavailable));
                Suspended(d, LocalLlmGovernorReason.PhysicsSignalUnavailable);
                Reason(d, LocalLlmGovernorReason.MemorySignalUnavailable);
                Reason(d, LocalLlmGovernorReason.ThermalSignalUnavailable);
                Equal(LocalLlmDeviceProfileKind.Conservative, d.DeviceProfile.Kind);
            });
            Run("recovery hysteresis", RecoveryHysteresis);
            Run("recent OOM suspended", () =>
            {
                var governor = new LocalLlmResourceGovernor();
                LocalLlmGovernorDecision d = governor.Evaluate(Baseline(), new LocalLlmResourceSnapshot(
                    12L * GiB, 8L * GiB, GiB / 2L, false, 8,
                    LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy, true));
                Suspended(d, LocalLlmGovernorReason.RecentOutOfMemory);
            });
            Run("behavior controls preserved", BehaviorControlsPreserved);
            Run("invalid snapshot rejected", () => Throws<ArgumentOutOfRangeException>(() =>
                _ = new LocalLlmResourceSnapshot(4L * GiB, 5L * GiB, 0L, false, 4,
                    LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy)));
            Run("physics tracker budget", PhysicsTrackerBudget);
            Run("incompatible profile suspended", IncompatibleProfileSuspended);

            Console.WriteLine(failures == 0
                ? "RMA-135 managed resource-governor contracts passed."
                : $"RMA-135 managed resource-governor contracts failed: {failures}.");
            return failures == 0 ? 0 : 1;
        }

        private static void RecoveryHysteresis()
        {
            var governor = new LocalLlmResourceGovernor();
            LocalLlmResourceSnapshot severe = Snapshot(12, 8, 0.5, 8, LocalLlmThermalStatus.Severe, LocalLlmPhysicsBudgetState.Healthy);
            LocalLlmResourceSnapshot healthy = Snapshot(12, 8, 0.5, 8, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy);
            Equal(LocalLlmGovernorMode.Suspended, governor.Evaluate(Baseline(), severe).Mode);
            LocalLlmGovernorDecision first = governor.Evaluate(Baseline(), healthy);
            Equal(LocalLlmGovernorMode.Suspended, first.Mode);
            Reason(first, LocalLlmGovernorReason.RecoveryHold);
            Equal(LocalLlmGovernorMode.Suspended, governor.Evaluate(Baseline(), healthy).Mode);
            Equal(LocalLlmGovernorMode.Nominal, governor.Evaluate(Baseline(), healthy).Mode);
        }

        private static void BehaviorControlsPreserved()
        {
            var baseline = new LocalLlmExecutionProfile(2048, 256, 64, 128, 4, 4, 0.25F, 0.2F, 987U, 37, 20, 2048, 8192);
            LocalLlmGovernorDecision d = new LocalLlmResourceGovernor().Evaluate(
                baseline, Snapshot(12, 8, 0.5, 8, LocalLlmThermalStatus.Moderate, LocalLlmPhysicsBudgetState.Healthy));
            LocalLlmExecutionProfile p = d.EffectiveProfile ?? throw new InvalidOperationException("Expected effective profile.");
            Equal(128, p.MaximumGeneratedTokens);
            Equal(0.25F, p.Temperature);
            Equal(0.2F, p.MinP);
            Equal(987U, p.Seed);
            Equal(37, p.StreamQueueCapacity);
            Equal(20, p.MaximumConversationMessages);
            Equal(2048, p.MaximumMessageCharacters);
            Equal(8192, p.MaximumResponseUtf8Bytes);
        }

        private static void PhysicsTrackerBudget()
        {
            var tracker = new ReachyLocalLlmPhysicsBudgetTracker(0.005);
            Equal(LocalLlmPhysicsBudgetState.Unavailable, tracker.Observe(Timing(100, 0, 0.0, 0.001)));
            Equal(LocalLlmPhysicsBudgetState.Healthy, tracker.Observe(Timing(101, 0, 0.001, 0.001)));
            Equal(LocalLlmPhysicsBudgetState.AtRisk, tracker.Observe(Timing(102, 0, 0.007, 0.006)));
            Equal(LocalLlmPhysicsBudgetState.Exceeded, tracker.Observe(Timing(103, 1, 0.008, 0.001)));
            Throws<InvalidOperationException>(() => tracker.Observe(Timing(102, 1, 0.008, 0.001)));
        }

        private static void IncompatibleProfileSuspended()
        {
            var baseline = new LocalLlmExecutionProfile(2048, 256, 64, 1500, 4, 4, 0F, 0F, 133U, 64);
            LocalLlmGovernorDecision d = new LocalLlmResourceGovernor().Evaluate(
                baseline, Snapshot(4, 3, 0.25, 4, LocalLlmThermalStatus.None, LocalLlmPhysicsBudgetState.Healthy));
            Suspended(d, LocalLlmGovernorReason.ProfileIncompatible);
        }

        private static LocalLlmGovernorDecision Eval(
            double totalGiB, double availableGiB, double thresholdGiB, int processors,
            LocalLlmThermalStatus thermal, LocalLlmPhysicsBudgetState physics)
        {
            return new LocalLlmResourceGovernor().Evaluate(
                Baseline(), Snapshot(totalGiB, availableGiB, thresholdGiB, processors, thermal, physics));
        }

        private static LocalLlmResourceSnapshot Snapshot(
            double totalGiB, double availableGiB, double thresholdGiB, int processors,
            LocalLlmThermalStatus thermal, LocalLlmPhysicsBudgetState physics)
        {
            return new LocalLlmResourceSnapshot(
                checked((long)(totalGiB * GiB)), checked((long)(availableGiB * GiB)),
                checked((long)(thresholdGiB * GiB)), false, processors, thermal, physics);
        }

        private static ReachySimulationTimingSnapshot Timing(ulong steps, ulong misses, double lag, double lastStep)
        {
            return new ReachySimulationTimingSnapshot(
                steps, misses, 0UL, 0UL, 0UL, lag, lastStep, lastStep);
        }

        private static LocalLlmExecutionProfile Baseline() => LocalLlmExecutionProfile.CreateRma133V6Baseline();

        private static void Profile(LocalLlmGovernorDecision d, int context, int batch, int threads)
        {
            LocalLlmExecutionProfile p = d.EffectiveProfile ?? throw new InvalidOperationException("Expected effective profile.");
            Equal(context, p.ContextTokens);
            Equal(batch, p.BatchTokens);
            Equal(threads, p.Threads);
        }

        private static void Suspended(LocalLlmGovernorDecision d, LocalLlmGovernorReason reason)
        {
            Equal(LocalLlmGovernorMode.Suspended, d.Mode);
            if (d.InferenceAllowed || d.EffectiveProfile != null)
            {
                throw new InvalidOperationException("Suspended inference exposed an executable profile.");
            }
            Reason(d, reason);
        }

        private static void Reason(LocalLlmGovernorDecision d, LocalLlmGovernorReason reason)
        {
            if ((d.Reasons & reason) == 0)
            {
                throw new InvalidOperationException($"Expected reason {reason}; actual={d.Reasons}.");
            }
        }

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

        private static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Expected exception " + typeof(TException).Name + ".");
        }
    }
}
