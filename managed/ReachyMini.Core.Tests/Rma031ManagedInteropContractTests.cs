#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.Core;
using ReachyMini.Interop;

namespace ReachyMini.Core.Tests
{
    internal static class Rma031ManagedInteropContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ReachySimError? layoutError =
                ReachySimManagedAbiContract.ValidateCurrentProcessLayout();
            if (layoutError != null)
            {
                throw new InvalidOperationException(
                    $"RMA-031 managed layout contract failed: {layoutError.Code}: {layoutError.Message}");
            }

            ReachySimError? matchingAbiError =
                ReachySimManagedAbiContract.ValidateNativeAbi(
                    ProjectMetadata.NativeAbiVersion);
            if (matchingAbiError != null)
            {
                throw new InvalidOperationException(
                    $"RMA-031 matching ABI was rejected: {matchingAbiError.Message}");
            }

            uint incompatibleAbi = checked(ProjectMetadata.NativeAbiVersion + 1U);
            ReachySimError? mismatchError =
                ReachySimManagedAbiContract.ValidateNativeAbi(incompatibleAbi);
            if (mismatchError == null)
            {
                throw new InvalidOperationException(
                    "RMA-031 incompatible ABI did not prevent managed startup.");
            }
            if (mismatchError.Code != ReachySimErrorCode.AbiMismatch ||
                mismatchError.Recoverability !=
                    ReachySimRecoverability.FatalConfiguration)
            {
                throw new InvalidOperationException(
                    $"RMA-031 incompatible ABI returned {mismatchError.Code}/{mismatchError.Recoverability}.");
            }
            if (!mismatchError.Message.Contains(
                    ProjectMetadata.NativeAbiVersion.ToString(),
                    StringComparison.Ordinal) ||
                !mismatchError.Message.Contains(
                    incompatibleAbi.ToString(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RMA-031 ABI mismatch diagnostic omitted version detail: {mismatchError.Message}");
            }
        }
    }
}
