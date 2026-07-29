using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ReachyMini.Interop;

namespace ReachyMini.Tests
{
    public sealed class ReachySimInteropLayoutTests
    {
        [Test]
        public void ManagedProcessUsesSupportedPointerWidth()
        {
            Assert.That(IntPtr.Size, Is.EqualTo(8));
        }

        [TestCase(typeof(NativeReachySimConfig), 24)]
        [TestCase(typeof(NativeReachySimCapabilities), 40)]
        [TestCase(typeof(NativeReachySimStateHeader), 48)]
        [TestCase(typeof(NativeReachySimCommandBatchHeader), 24)]
        [TestCase(typeof(NativeReachySimWrenchCommand), 96)]
        [TestCase(typeof(NativeReachySimSnapshotHeader), 40)]
        [TestCase(typeof(NativeReachySimErrorInfo), 272)]
        public void ManagedStructuresMatchNativeSizes(
            Type structureType,
            int expectedSize)
        {
            Assert.That(
                Marshal.SizeOf(structureType),
                Is.EqualTo(expectedSize));
        }

        [Test]
        public void CriticalOffsetsMatchNativeLayout()
        {
            Assert.That(
                Marshal.OffsetOf<NativeReachySimConfig>(
                    nameof(NativeReachySimConfig.TimestepSeconds)),
                Is.EqualTo(new IntPtr(8)));
            Assert.That(
                Marshal.OffsetOf<NativeReachySimStateHeader>(
                    nameof(NativeReachySimStateHeader.SimulationTime)),
                Is.EqualTo(new IntPtr(16)));
            Assert.That(
                Marshal.OffsetOf<NativeReachySimErrorInfo>(
                    nameof(NativeReachySimErrorInfo.Message)),
                Is.EqualTo(new IntPtr(16)));
        }
    }
}
