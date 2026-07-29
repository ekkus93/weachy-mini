using System;

namespace ReachyMini.Interop
{
    public enum ReachySimErrorCode
    {
        Ok = 0,
        InvalidArgument = 1,
        AbiMismatch = 2,
        StructSizeMismatch = 3,
        ModelEmpty = 4,
        ModelTooLarge = 5,
        AllocationFailed = 6,
        ResourceExhausted = 7,
        BackendUnavailable = 8,
        BackendError = 9,
        InvalidHandle = 10,
        StaleHandle = 11,
        HandleBusy = 12,
        BufferTooSmall = 13,
        CommandFormatError = 14,
        SnapshotIncompatible = 15,
        Unsupported = 16,
        NumericFailure = 17,
        ManagedInteropFailure = 1000,
    }

    public enum ReachySimRecoverability
    {
        None = 0,
        Retry = 1,
        RecreateHandle = 2,
        ReloadModel = 3,
        FatalConfiguration = 4,
    }

    public sealed class ReachySimError
    {
        public static ReachySimError NoError { get; } = new ReachySimError(
            ReachySimErrorCode.Ok,
            ReachySimRecoverability.None,
            string.Empty);

        public ReachySimError(
            ReachySimErrorCode code,
            ReachySimRecoverability recoverability,
            string message)
        {
            Code = code;
            Recoverability = recoverability;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public ReachySimErrorCode Code { get; }

        public ReachySimRecoverability Recoverability { get; }

        public string Message { get; }

        internal static ReachySimError FromNative(
            int status,
            uint recoverability,
            string message)
        {
            return new ReachySimError(
                (ReachySimErrorCode)status,
                (ReachySimRecoverability)recoverability,
                message ?? string.Empty);
        }
    }

    public sealed class ReachySimOperationResult
    {
        private ReachySimOperationResult(
            bool isSuccess,
            ReachySimError error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public bool IsSuccess { get; }

        public ReachySimError Error { get; }

        internal static ReachySimOperationResult Success()
        {
            return new ReachySimOperationResult(
                isSuccess: true,
                ReachySimError.NoError);
        }

        internal static ReachySimOperationResult Failure(
            ReachySimError error)
        {
            return new ReachySimOperationResult(
                isSuccess: false,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
