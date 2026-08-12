#nullable enable

using System;
using System.Threading.Tasks;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        internal static async Task RunAsync()
        {
            ProviderKindsAndCapabilitiesRemainExplicit();
            await TransformedFramesRequireOwnedColorValidityAndCoverageAsync()
                .ConfigureAwait(false);
            await FrameSourceRejectsRawFallbackAndStaleSequenceAsync()
                .ConfigureAwait(false);
            await CallerCancellationReturnsTypedFailureAsync()
                .ConfigureAwait(false);
            await TimeoutQuarantinesProviderAsync().ConfigureAwait(false);
            await ProviderFaultRemainsVisibleAsync().ConfigureAwait(false);
            await ProviderSwitchSupersedesLateResultsAsync()
                .ConfigureAwait(false);
            await ResultIdentityMismatchFailsClosedAsync()
                .ConfigureAwait(false);
            await CloudDisclosureIsRequiredBeforeInvocationAsync()
                .ConfigureAwait(false);
            await FrameResourcesDisposeExactlyOnceAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 vision provider contracts passed.");
        }
    }
}
