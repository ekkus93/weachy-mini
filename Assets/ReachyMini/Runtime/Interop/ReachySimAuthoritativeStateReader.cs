#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    internal sealed class ReachySimStateFormatException : InvalidOperationException
    {
        public ReachySimStateFormatException(string message)
            : base(message)
        {
        }
    }

    internal sealed class ReachySimAuthoritativeStateException : InvalidOperationException
    {
        internal ReachySimAuthoritativeStateException(ReachySimError error)
            : base(
                error == null
                    ? "The native authoritative state operation failed."
                    : $"Native authoritative state failed: {error.Code}: {error.Message}")
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public ReachySimError Error { get; }
    }

    public interface IReachySimAuthoritativeStateReader : IDisposable
    {
        ReachySimAuthoritativeStateLayout Layout { get; }

        ReachySimAuthoritativeStateFrame CreateFrame();

        void Capture(ReachySimAuthoritativeStateFrame frame);
    }

    public sealed class ReachySimAuthoritativeStateReader :
        IReachySimAuthoritativeStateReader
    {
        private const ulong RequestMagic = 0x5253494d53544154UL;
        private readonly object gate = new object();
        private readonly ReachySimSession session;
        private IntPtr buffer;
        private bool disposed;

        public ReachySimAuthoritativeStateReader(ReachySimSession session)
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            int requestSize =
                Marshal.SizeOf<NativeReachySimStateRequest>();
            buffer = Marshal.AllocHGlobal(requestSize);
            try
            {
                WriteRequest(buffer);
                int status = session.CopyStateRaw(
                    buffer,
                    requestSize,
                    out int requiredSize);
                if (status != (int)NativeReachySimStatus.BufferTooSmall)
                {
                    throw status == (int)NativeReachySimStatus.Ok
                        ? new ReachySimStateFormatException(
                            "The native backend did not negotiate the authoritative state payload.")
                        : new ReachySimAuthoritativeStateException(
                            session.GetErrorForStatus(status));
                }

                int minimumSize = checked(
                    Marshal.SizeOf<NativeReachySimStateHeader>() +
                    Marshal.SizeOf<NativeReachySimStatePayloadHeader>());
                if (requiredSize <= minimumSize)
                {
                    throw new ReachySimStateFormatException(
                        $"The native backend returned only {requiredSize} state bytes; " +
                        "the authoritative state payload is unavailable.");
                }

                buffer = Marshal.ReAllocHGlobal(
                    buffer,
                    new IntPtr(requiredSize));
                WriteRequest(buffer);
                status = session.CopyStateRaw(
                    buffer,
                    requiredSize,
                    out int actualSize);
                if (status != (int)NativeReachySimStatus.Ok)
                {
                    throw new ReachySimAuthoritativeStateException(
                        session.GetErrorForStatus(status));
                }
                if (actualSize != requiredSize)
                {
                    throw new ReachySimStateFormatException(
                        $"Native state size changed during negotiation: " +
                        $"expected {requiredSize}, received {actualSize}.");
                }

                Layout = ReachySimAuthoritativeStateParser.Inspect(
                    buffer,
                    actualSize);
            }
            catch
            {
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                throw;
            }
        }

        public ReachySimAuthoritativeStateLayout Layout { get; }

        public ReachySimAuthoritativeStateFrame CreateFrame()
        {
            ThrowIfDisposed();
            return new ReachySimAuthoritativeStateFrame(Layout);
        }

        public void Capture(ReachySimAuthoritativeStateFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }
            if (!Layout.Matches(frame.Layout))
            {
                throw new ArgumentException(
                    "The destination frame was created for a different authoritative state layout.",
                    nameof(frame));
            }

            lock (gate)
            {
                ThrowIfDisposed();
                WriteRequest(buffer);
                int status = session.CopyStateRaw(
                    buffer,
                    Layout.ByteCount,
                    out int actualSize);
                if (status != (int)NativeReachySimStatus.Ok)
                {
                    throw new ReachySimAuthoritativeStateException(
                        session.GetErrorForStatus(status));
                }
                if (actualSize != Layout.ByteCount)
                {
                    throw new ReachySimStateFormatException(
                        $"Native state layout changed from {Layout.ByteCount} " +
                        $"to {actualSize} bytes while the session was active.");
                }

                ReachySimAuthoritativeStateParser.Decode(
                    buffer,
                    actualSize,
                    frame,
                    Layout.ModelHash);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        private static void WriteRequest(IntPtr destination)
        {
            Marshal.WriteInt64(destination, 0, unchecked((long)RequestMagic));
            Marshal.WriteInt32(
                destination,
                8,
                unchecked((int)ProjectMetadata.NativeAbiVersion));
            Marshal.WriteInt32(
                destination,
                12,
                Marshal.SizeOf<NativeReachySimStateRequest>());
            Marshal.WriteInt32(
                destination,
                16,
                unchecked((int)ProjectMetadata.NativeStateFormatVersion));
            Marshal.WriteInt32(destination, 20, 0);
        }

        private void ThrowIfDisposed()
        {
            if (disposed || buffer == IntPtr.Zero)
            {
                throw new ObjectDisposedException(
                    nameof(ReachySimAuthoritativeStateReader));
            }
        }
    }

}
