using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Core.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            TestProjectMetadata();
            TestNativeLayouts();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("REACHY_MANAGED_NATIVE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                TestNativeSessionLifecycle();
            }

            return 0;
        }

        private static void TestProjectMetadata()
        {
            AssertEqual(
                SimulationFidelity.Unavailable,
                ProjectMetadata.InitialFidelity,
                "initial fidelity");
            AssertEqual(
                true,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.002),
                "500 Hz timestep");
            AssertEqual(
                false,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.0),
                "zero timestep");
            AssertEqual(
                false,
                ProjectMetadata.IsSupportedPhysicsTimestep(0.02),
                "oversized timestep");
        }

        private static void TestNativeLayouts()
        {
            AssertEqual(8, IntPtr.Size, "64-bit managed process");
            AssertEqual(24, Marshal.SizeOf<NativeReachySimConfig>(), "config size");
            AssertEqual(
                40,
                Marshal.SizeOf<NativeReachySimCapabilities>(),
                "capabilities size");
            AssertEqual(
                48,
                Marshal.SizeOf<NativeReachySimStateHeader>(),
                "state header size");
            AssertEqual(
                24,
                Marshal.SizeOf<NativeReachySimCommandBatchHeader>(),
                "command header size");
            AssertEqual(
                96,
                Marshal.SizeOf<NativeReachySimWrenchCommand>(),
                "wrench command size");
            AssertEqual(
                40,
                Marshal.SizeOf<NativeReachySimSnapshotHeader>(),
                "snapshot header size");
            AssertEqual(
                272,
                Marshal.SizeOf<NativeReachySimErrorInfo>(),
                "error info size");

            AssertEqual(
                new IntPtr(8),
                Marshal.OffsetOf<NativeReachySimConfig>(
                    nameof(NativeReachySimConfig.TimestepSeconds)),
                "config timestep offset");
            AssertEqual(
                new IntPtr(16),
                Marshal.OffsetOf<NativeReachySimStateHeader>(
                    nameof(NativeReachySimStateHeader.SimulationTime)),
                "state time offset");
            AssertEqual(
                new IntPtr(16),
                Marshal.OffsetOf<NativeReachySimErrorInfo>(
                    nameof(NativeReachySimErrorInfo.Message)),
                "error message offset");
        }

        private static void TestNativeSessionLifecycle()
        {
            byte[] modelBytes = Encoding.UTF8.GetBytes("managed-contract-model");

            ReachySimCreateResult createResult =
                ReachySimSession.Create(modelBytes);
            AssertEqual(true, createResult.IsSuccess, "native create result");
            ReachySimSession session = createResult.Session ??
                throw new InvalidOperationException(
                    $"Native create failed: {createResult.Error.Code}: {createResult.Error.Message}");

            ReachySimOperationResult stepResult = session.Step(10U);
            AssertEqual(true, stepResult.IsSuccess, "native step result");

            ReachySimOperationResult zeroStepResult = session.Step(0U);
            AssertEqual(false, zeroStepResult.IsSuccess, "zero-step failure");
            AssertEqual(
                ReachySimErrorCode.InvalidArgument,
                zeroStepResult.Error.Code,
                "zero-step error code");

            ReachySimOperationResult resetResult = session.Reset(0U);
            AssertEqual(true, resetResult.IsSuccess, "native reset result");

            ReachySimOperationResult closeResult = session.Close();
            AssertEqual(true, closeResult.IsSuccess, "native close result");
            AssertThrows<ObjectDisposedException>(
                () => session.Step(1U),
                "operation after close");
            session.Dispose();

            for (int iteration = 0; iteration < 1000; ++iteration)
            {
                ReachySimCreateResult stressCreate =
                    ReachySimSession.Create(modelBytes);
                if (!stressCreate.IsSuccess || stressCreate.Session == null)
                {
                    throw new InvalidOperationException(
                        $"Lifecycle create {iteration} failed: {stressCreate.Error.Code}: {stressCreate.Error.Message}");
                }

                using (ReachySimSession stressSession = stressCreate.Session)
                {
                    ReachySimOperationResult stressStep =
                        stressSession.Step(1U);
                    AssertEqual(
                        true,
                        stressStep.IsSuccess,
                        $"lifecycle step {iteration}");
                }
            }
        }

        private static void AssertEqual<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }

        private static void AssertThrows<TException>(
            Action action,
            string description)
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
                $"Managed test failed for {description}: expected {typeof(TException).Name}.");
        }
    }
}
