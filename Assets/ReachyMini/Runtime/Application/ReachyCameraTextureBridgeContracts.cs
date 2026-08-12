#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraTextureBridgeState
    {
        Waiting = 0,
        Ready = 1,
        Faulted = 2,
        Unsupported = 3,
    }

    public sealed class ReachyCameraTextureBridgeSnapshot
    {
        public ReachyCameraTextureBridgeSnapshot(
            ReachyCameraTextureBridgeState state,
            string message,
            ReachyCameraTextureFrameDescriptor? frame,
            ulong uploadedFrameCount,
            ulong staleFrameCount,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Texture bridge state requires diagnostics.",
                    nameof(message));
            }
            if (state == ReachyCameraTextureBridgeState.Ready && frame == null)
            {
                throw new ArgumentNullException(
                    nameof(frame),
                    "Ready texture state requires frame metadata.");
            }
            if (state != ReachyCameraTextureBridgeState.Ready && frame != null)
            {
                throw new ArgumentException(
                    "Non-ready texture state cannot retain a sampleable frame.",
                    nameof(frame));
            }

            State = state;
            Message = message;
            Frame = frame;
            UploadedFrameCount = uploadedFrameCount;
            StaleFrameCount = staleFrameCount;
            Revision = revision;
        }

        public ReachyCameraTextureBridgeState State { get; }

        public string Message { get; }

        public ReachyCameraTextureFrameDescriptor? Frame { get; }

        public ulong UploadedFrameCount { get; }

        public ulong StaleFrameCount { get; }

        public ulong Revision { get; }

        public bool HasTexture =>
            State == ReachyCameraTextureBridgeState.Ready && Frame != null;
    }

    public sealed class ReachyCameraTextureBridgeChangedEventArgs : EventArgs
    {
        public ReachyCameraTextureBridgeChangedEventArgs(
            ReachyCameraTextureBridgeSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyCameraTextureBridgeSnapshot Snapshot { get; }
    }
}
