using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static void TestProjectMetadata()
        {
            AssertEqual(
                SimulationFidelity.Unavailable,
                ProjectMetadata.InitialFidelity,
                "initial fidelity");
            AssertEqual(
                1U,
                ProjectMetadata.NativeSnapshotFormatVersion,
                "snapshot format version");
            AssertEqual(
                0UL,
                ProjectMetadata.UncalibratedCalibrationProfileId,
                "uncalibrated profile identifier");
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
                48,
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
                new IntPtr(40),
                Marshal.OffsetOf<NativeReachySimSnapshotHeader>(
                    nameof(NativeReachySimSnapshotHeader.CalibrationProfileId)),
                "snapshot calibration offset");
            AssertEqual(
                new IntPtr(16),
                Marshal.OffsetOf<NativeReachySimErrorInfo>(
                    nameof(NativeReachySimErrorInfo.Message)),
                "error message offset");
        }
    }
}
