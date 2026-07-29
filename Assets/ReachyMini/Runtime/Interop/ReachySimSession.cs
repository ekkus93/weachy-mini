#nullable enable

using System;
using System.Runtime.InteropServices;
using ReachyMini.Core;

namespace ReachyMini.Interop
{
    public sealed class ReachySimCreateResult
    {
        private ReachySimCreateResult(
            bool isSuccess,
            ReachySimSession? session,
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            Session = session;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimSession? Session { get; }

        public ReachySimError Error { get; }

        internal static ReachySimCreateResult Success(
            ReachySimSession session)
        {
            return new ReachySimCreateResult(
                isSuccess: true,
                session ?? throw new ArgumentNullException(nameof(session)),
                ReachySimError.NoError);
        }

        internal static ReachySimCreateResult Failure(
            ReachySimError error)
        {
            return new ReachySimCreateResult(
                isSuccess: false,
                session: null,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }

    public sealed class ReachySimSession : IDisposable
    {
        private readonly object gate = new object();
        private ReachySimSafeHandle? handle;
        private bool disposed;

        private ReachySimSession(ReachySimSafeHandle handle)
        {
            this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public static ReachySimCreateResult Create(byte[] modelBytes)
        {
            if (modelBytes == null)
            {
                return ReachySimCreateResult.Failure(
                    new ReachySimError(
                        ReachySimErrorCode.InvalidArgument,
                        ReachySimRecoverability.FatalConfiguration,
                        "Model bytes cannot be null."));
            }
            if (modelBytes.Length == 0)
            {
                return ReachySimCreateResult.Failure(
                    new ReachySimError(
                        ReachySimErrorCode.ModelEmpty,
                        ReachySimRecoverability.FatalConfiguration,
                        "Model bytes cannot be empty."));
            }
            if (IntPtr.Size != sizeof(ulong))
            {
                return ReachySimCreateResult.Failure(
                    new ReachySimError(
                        ReachySimErrorCode.ManagedInteropFailure,
                        ReachySimRecoverability.FatalConfiguration,
                        "The Reachy simulation ABI requires a 64-bit process."));
            }

            try
            {
                uint nativeAbiVersion = NativeReachySim.AbiVersion();
                if (nativeAbiVersion != ProjectMetadata.NativeAbiVersion)
                {
                    return ReachySimCreateResult.Failure(
                        new ReachySimError(
                            ReachySimErrorCode.AbiMismatch,
                            ReachySimRecoverability.FatalConfiguration,
                            $"Managed ABI {ProjectMetadata.NativeAbiVersion} does not match native ABI {nativeAbiVersion}."));
                }

                NativeReachySimConfig config = NativeReachySim.DefaultConfig();
                NativeReachySimErrorInfo nativeError =
                    NativeReachySimErrorInfo.Create();
                int status = NativeReachySim.Create(
                    modelBytes,
                    new UIntPtr(checked((uint)modelBytes.Length)),
                    in config,
                    out ulong token,
                    ref nativeError);
                if (status != (int)NativeReachySimStatus.Ok)
                {
                    return ReachySimCreateResult.Failure(
                        ConvertNativeError(status, nativeError));
                }
                if (token == 0UL)
                {
                    return ReachySimCreateResult.Failure(
                        new ReachySimError(
                            ReachySimErrorCode.BackendError,
                            ReachySimRecoverability.RecreateHandle,
                            "Native creation reported success but returned an invalid zero handle."));
                }

                return ReachySimCreateResult.Success(
                    new ReachySimSession(
                        ReachySimSafeHandle.FromToken(token)));
            }
            catch (DllNotFoundException exception)
            {
                return InteropFailure(exception);
            }
            catch (EntryPointNotFoundException exception)
            {
                return InteropFailure(exception);
            }
            catch (BadImageFormatException exception)
            {
                return InteropFailure(exception);
            }
            catch (MarshalDirectiveException exception)
            {
                return InteropFailure(exception);
            }
        }

        public ReachySimOperationResult Reset(ReachySimResetPose resetPose)
        {
            return Reset((uint)resetPose);
        }

        public ReachySimOperationResult Reset(uint resetId)
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                int status = NativeReachySim.Reset(
                    activeHandle.Token,
                    resetId);
                return ResultFromStatus(activeHandle, status);
            }
        }

        public ReachySimOperationResult Step(uint stepCount)
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                int status = NativeReachySim.Step(
                    activeHandle.Token,
                    stepCount);
                return ResultFromStatus(activeHandle, status);
            }
        }

        public ReachySimSnapshotCaptureResult CaptureSnapshot()
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                int status = NativeReachySim.CopySnapshot(
                    activeHandle.Token,
                    IntPtr.Zero,
                    UIntPtr.Zero,
                    out UIntPtr nativeRequiredSize);
                if (status != (int)NativeReachySimStatus.BufferTooSmall &&
                    status != (int)NativeReachySimStatus.Ok)
                {
                    return ReachySimSnapshotCaptureResult.Failure(
                        ErrorFromStatus(activeHandle, status));
                }

                ulong requiredSizeValue = nativeRequiredSize.ToUInt64();
                int headerSize = Marshal.SizeOf<NativeReachySimSnapshotHeader>();
                if (requiredSizeValue < (ulong)headerSize ||
                    requiredSizeValue > int.MaxValue)
                {
                    return ReachySimSnapshotCaptureResult.Failure(
                        InvalidSnapshotError(
                            $"Native snapshot size {requiredSizeValue} is outside the supported managed range."));
                }

                int requiredSize = checked((int)requiredSizeValue);
                IntPtr buffer;
                try
                {
                    buffer = Marshal.AllocHGlobal(requiredSize);
                }
                catch (OutOfMemoryException exception)
                {
                    return ReachySimSnapshotCaptureResult.Failure(
                        new ReachySimError(
                            ReachySimErrorCode.AllocationFailed,
                            ReachySimRecoverability.Retry,
                            exception.Message));
                }

                try
                {
                    status = NativeReachySim.CopySnapshot(
                        activeHandle.Token,
                        buffer,
                        new UIntPtr(checked((uint)requiredSize)),
                        out UIntPtr actualNativeSize);
                    if (status != (int)NativeReachySimStatus.Ok)
                    {
                        return ReachySimSnapshotCaptureResult.Failure(
                            ErrorFromStatus(activeHandle, status));
                    }

                    ulong actualSizeValue = actualNativeSize.ToUInt64();
                    if (actualSizeValue != requiredSizeValue)
                    {
                        return ReachySimSnapshotCaptureResult.Failure(
                            InvalidSnapshotError(
                                $"Native snapshot size changed during capture: expected {requiredSizeValue}, received {actualSizeValue}."));
                    }

                    NativeReachySimSnapshotHeader header =
                        Marshal.PtrToStructure<NativeReachySimSnapshotHeader>(buffer);
                    ReachySimError? validationError =
                        ValidateSnapshotEnvelope(header, requiredSize);
                    if (validationError != null)
                    {
                        return ReachySimSnapshotCaptureResult.Failure(
                            validationError);
                    }

                    byte[] bytes = new byte[requiredSize];
                    Marshal.Copy(buffer, bytes, 0, requiredSize);
                    return ReachySimSnapshotCaptureResult.Success(
                        new ReachySimSnapshot(bytes, header));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public ReachySimOperationResult RestoreSnapshot(
            ReachySimSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return ReachySimOperationResult.Failure(
                    new ReachySimError(
                        ReachySimErrorCode.InvalidArgument,
                        ReachySimRecoverability.FatalConfiguration,
                        "Snapshot cannot be null."));
            }

            byte[] bytes = snapshot.CopyBytes();
            int headerSize = Marshal.SizeOf<NativeReachySimSnapshotHeader>();
            if (bytes.Length < headerSize)
            {
                return ReachySimOperationResult.Failure(
                    InvalidSnapshotError(
                        $"Snapshot contains {bytes.Length} bytes, smaller than the {headerSize}-byte header."));
            }

            IntPtr buffer;
            try
            {
                buffer = Marshal.AllocHGlobal(bytes.Length);
            }
            catch (OutOfMemoryException exception)
            {
                return ReachySimOperationResult.Failure(
                    new ReachySimError(
                        ReachySimErrorCode.AllocationFailed,
                        ReachySimRecoverability.Retry,
                        exception.Message));
            }

            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                NativeReachySimSnapshotHeader header =
                    Marshal.PtrToStructure<NativeReachySimSnapshotHeader>(buffer);
                ReachySimError? validationError =
                    ValidateSnapshotEnvelope(header, bytes.Length);
                if (validationError != null)
                {
                    return ReachySimOperationResult.Failure(validationError);
                }

                lock (gate)
                {
                    ReachySimSafeHandle activeHandle = RequireActiveHandle();
                    int status = NativeReachySim.RestoreSnapshot(
                        activeHandle.Token,
                        buffer,
                        new UIntPtr(checked((uint)bytes.Length)));
                    return ResultFromStatus(activeHandle, status);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        internal int ResetRaw(uint resetId)
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                return NativeReachySim.Reset(activeHandle.Token, resetId);
            }
        }

        internal int StepRaw(uint stepCount)
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                return NativeReachySim.Step(activeHandle.Token, stepCount);
            }
        }

        internal int SubmitCommandsRaw(
            IntPtr bytes,
            int byteCount)
        {
            if (bytes == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "The command buffer pointer cannot be zero.",
                    nameof(bytes));
            }
            if (byteCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(byteCount),
                    "The command buffer must contain at least one byte.");
            }

            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                return NativeReachySim.SubmitCommands(
                    activeHandle.Token,
                    bytes,
                    new UIntPtr(checked((uint)byteCount)));
            }
        }

        internal int CopyStateRaw(
            IntPtr bytes,
            int byteCapacity,
            out int requiredSize)
        {
            if (bytes == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "The state buffer pointer cannot be zero.",
                    nameof(bytes));
            }
            if (byteCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(byteCapacity),
                    "The state buffer capacity must be positive.");
            }

            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                int status = NativeReachySim.CopyState(
                    activeHandle.Token,
                    bytes,
                    new UIntPtr(checked((uint)byteCapacity)),
                    out UIntPtr nativeRequiredSize);
                ulong requiredSizeValue = nativeRequiredSize.ToUInt64();
                if (requiredSizeValue > int.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Native state requires {requiredSizeValue} bytes, which exceeds the managed buffer limit.");
                }

                requiredSize = checked((int)requiredSizeValue);
                return status;
            }
        }

        internal ReachySimError GetErrorForStatus(int status)
        {
            lock (gate)
            {
                ReachySimSafeHandle activeHandle = RequireActiveHandle();
                return ErrorFromStatus(activeHandle, status);
            }
        }

        public ReachySimOperationResult Close()
        {
            lock (gate)
            {
                ReachySimSafeHandle? activeHandle = handle;
                if (activeHandle == null)
                {
                    disposed = true;
                    return ReachySimOperationResult.Success();
                }

                int status = activeHandle.CloseExplicitly();
                if (status != (int)NativeReachySimStatus.Ok)
                {
                    return ResultFromStatus(activeHandle, status);
                }

                activeHandle.Dispose();
                handle = null;
                disposed = true;
                return ReachySimOperationResult.Success();
            }
        }

        public void Dispose()
        {
            ReachySimOperationResult result = Close();
            GC.SuppressFinalize(this);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Native simulator destruction failed: {result.Error.Code}: {result.Error.Message}");
            }
        }

        private static ReachySimCreateResult InteropFailure(
            Exception exception)
        {
            return ReachySimCreateResult.Failure(
                new ReachySimError(
                    ReachySimErrorCode.ManagedInteropFailure,
                    ReachySimRecoverability.FatalConfiguration,
                    exception.Message));
        }

        private static ReachySimError ConvertNativeError(
            int fallbackStatus,
            NativeReachySimErrorInfo nativeError)
        {
            int status = nativeError.Status ==
                (int)NativeReachySimStatus.Ok
                    ? fallbackStatus
                    : nativeError.Status;
            uint recoverability = nativeError.Status ==
                (int)NativeReachySimStatus.Ok
                    ? NativeReachySim.StatusRecoverability(status)
                    : nativeError.Recoverability;
            string message = string.IsNullOrWhiteSpace(nativeError.Message)
                ? NativeStatusMessage(status)
                : nativeError.Message;
            return ReachySimError.FromNative(
                status,
                recoverability,
                message);
        }

        private static string NativeStatusMessage(int status)
        {
            IntPtr pointer = NativeReachySim.StatusString(status);
            string? value = Marshal.PtrToStringAnsi(pointer);
            return string.IsNullOrWhiteSpace(value)
                ? $"Native simulator returned status {status}."
                : value;
        }

        private static ReachySimError? ValidateSnapshotEnvelope(
            NativeReachySimSnapshotHeader header,
            int byteCount)
        {
            int expectedHeaderSize =
                Marshal.SizeOf<NativeReachySimSnapshotHeader>();
            if (header.AbiVersion != ProjectMetadata.NativeAbiVersion)
            {
                return InvalidSnapshotError(
                    $"Snapshot ABI {header.AbiVersion} does not match managed ABI {ProjectMetadata.NativeAbiVersion}.");
            }
            if (header.StructSize != checked((uint)expectedHeaderSize))
            {
                return InvalidSnapshotError(
                    $"Snapshot header size {header.StructSize} does not match {expectedHeaderSize}.");
            }
            if (header.SnapshotVersion !=
                ProjectMetadata.NativeSnapshotFormatVersion)
            {
                return InvalidSnapshotError(
                    $"Snapshot format {header.SnapshotVersion} does not match supported format {ProjectMetadata.NativeSnapshotFormatVersion}.");
            }
            if (double.IsNaN(header.SimulationTime) ||
                double.IsInfinity(header.SimulationTime) ||
                header.SimulationTime < 0.0)
            {
                return InvalidSnapshotError(
                    "Snapshot simulation time is invalid.");
            }

            ulong expectedByteCount =
                (ulong)expectedHeaderSize + header.PayloadSize;
            if (expectedByteCount != (ulong)byteCount)
            {
                return InvalidSnapshotError(
                    $"Snapshot envelope declares {expectedByteCount} bytes but contains {byteCount}.");
            }

            return null;
        }

        private static ReachySimError InvalidSnapshotError(string message)
        {
            return new ReachySimError(
                ReachySimErrorCode.SnapshotIncompatible,
                ReachySimRecoverability.ReloadModel,
                message);
        }

        private ReachySimSafeHandle RequireActiveHandle()
        {
            ReachySimSafeHandle? activeHandle = handle;
            if (disposed ||
                activeHandle == null ||
                activeHandle.IsClosed ||
                activeHandle.IsInvalid)
            {
                throw new ObjectDisposedException(nameof(ReachySimSession));
            }
            return activeHandle;
        }

        private static ReachySimOperationResult ResultFromStatus(
            ReachySimSafeHandle activeHandle,
            int status)
        {
            if (status == (int)NativeReachySimStatus.Ok)
            {
                return ReachySimOperationResult.Success();
            }

            return ReachySimOperationResult.Failure(
                ErrorFromStatus(activeHandle, status));
        }

        private static ReachySimError ErrorFromStatus(
            ReachySimSafeHandle activeHandle,
            int status)
        {
            NativeReachySimErrorInfo nativeError =
                NativeReachySimErrorInfo.Create();
            int errorStatus = NativeReachySim.GetLastError(
                activeHandle.Token,
                ref nativeError);
            if (errorStatus == (int)NativeReachySimStatus.Ok)
            {
                return ConvertNativeError(status, nativeError);
            }

            return new ReachySimError(
                (ReachySimErrorCode)status,
                (ReachySimRecoverability)NativeReachySim.StatusRecoverability(status),
                $"{NativeStatusMessage(status)} Last-error query failed with {NativeStatusMessage(errorStatus)}");
        }
    }
}
