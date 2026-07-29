#nullable enable

using System;

namespace ReachyMini.Interop
{
    public enum ReachySimResetPose : uint
    {
        SleepRest = 0U,
        NeutralAwake = 1U,
    }

    public sealed class ReachySimSnapshot
    {
        private readonly byte[] bytes;

        internal ReachySimSnapshot(
            byte[] bytes,
            NativeReachySimSnapshotHeader header)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            this.bytes = (byte[])bytes.Clone();
            SnapshotVersion = header.SnapshotVersion;
            ModelHash = header.ModelHash;
            CalibrationProfileId = header.CalibrationProfileId;
            Sequence = header.Sequence;
            SimulationTime = header.SimulationTime;
        }

        public uint SnapshotVersion { get; }

        public ulong ModelHash { get; }

        public ulong CalibrationProfileId { get; }

        public ulong Sequence { get; }

        public double SimulationTime { get; }

        public int ByteCount => bytes.Length;

        public byte[] ToArray()
        {
            return (byte[])bytes.Clone();
        }

        internal byte[] CopyBytes()
        {
            return (byte[])bytes.Clone();
        }
    }

    public sealed class ReachySimSnapshotCaptureResult
    {
        private ReachySimSnapshotCaptureResult(
            bool isSuccess,
            ReachySimSnapshot? snapshot,
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            Snapshot = snapshot;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimSnapshot? Snapshot { get; }

        public ReachySimError Error { get; }

        internal static ReachySimSnapshotCaptureResult Success(
            ReachySimSnapshot snapshot)
        {
            return new ReachySimSnapshotCaptureResult(
                isSuccess: true,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                ReachySimError.NoError);
        }

        internal static ReachySimSnapshotCaptureResult Failure(
            ReachySimError error)
        {
            return new ReachySimSnapshotCaptureResult(
                isSuccess: false,
                snapshot: null,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
