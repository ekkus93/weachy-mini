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

                ReachySimSafeHandle safeHandle;
                try
                {
                    safeHandle = ReachySimSafeHandle.FromToken(token);
                }
                catch
                {
                    _ = NativeReachySim.Destroy(token);
                    throw;
                }

                return ReachySimCreateResult.Success(
                    new ReachySimSession(safeHandle));
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

        public ReachySimOperationResult Close()
        {
            lock (gate)
            {
                if (handle == null)
                {
                    disposed = true;
                    return ReachySimOperationResult.Success();
                }

                int status = handle.CloseExplicitly();
                if (status != (int)NativeReachySimStatus.Ok)
                {
                    return ResultFromStatus(handle, status);
                }

                handle.Dispose();
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

        private ReachySimSafeHandle RequireActiveHandle()
        {
            if (disposed || handle == null || handle.IsClosed || handle.IsInvalid)
            {
                throw new ObjectDisposedException(nameof(ReachySimSession));
            }
            return handle;
        }

        private static ReachySimOperationResult ResultFromStatus(
            ReachySimSafeHandle activeHandle,
            int status)
        {
            if (status == (int)NativeReachySimStatus.Ok)
            {
                return ReachySimOperationResult.Success();
            }

            NativeReachySimErrorInfo nativeError =
                NativeReachySimErrorInfo.Create();
            int errorStatus = NativeReachySim.GetLastError(
                activeHandle.Token,
                ref nativeError);
            if (errorStatus == (int)NativeReachySimStatus.Ok)
            {
                return ReachySimOperationResult.Failure(
                    ConvertNativeError(status, nativeError));
            }

            return ReachySimOperationResult.Failure(
                new ReachySimError(
                    (ReachySimErrorCode)status,
                    (ReachySimRecoverability)NativeReachySim.StatusRecoverability(status),
                    $"{NativeStatusMessage(status)} Last-error query failed with {NativeStatusMessage(errorStatus)}"));
        }
    }
}
