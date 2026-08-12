#nullable enable

using System;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private const int ExpectedCaseCount = 60;
        private static int caseCount;

        private static async Task<int> Main()
        {
            Run(PolicyRejectsUpscaling);
            Run(PolicyRejectsInvalidDimensions);
            Run(PolicyRejectsInvalidEncodedLimit);
            Run(PolicyRejectsInvalidQuality);
            Run(PolicyComputesBoundedLandscapeDimensions);
            Run(PolicyComputesBoundedPortraitDimensions);
            Run(PolicyDoesNotUpscaleSmallImages);
            Run(ConfigurationRequiresNetworkLocation);
            Run(ConfigurationRequiresSemanticCapability);
            Run(ConfigurationKeepsModelIdConfigurable);
            Run(ConfigurationPublishesExactCapabilities);
            Run(ResponsesProviderRequiresResponsesConfiguration);
            Run(ResponsesProviderRequiresResponsesTransport);
            Run(ChatProviderRequiresChatConfiguration);
            Run(ChatProviderRequiresChatTransport);
            await RunAsync(EncodingRequestRequiresEligibleTransformedFrame).ConfigureAwait(false);
            Run(EncodedImageCopiesInputBytes);
            Run(EncodedImageRequiresTransformedOrigin);
            Run(EncodedImageRequiresValidityApplication);
            Run(EncodedImageRejectsUpscaling);
            Run(EncodedImageDisposalZeroesPayload);
            await RunAsync(ProviderRejectsRawFrameBeforeEncoder).ConfigureAwait(false);
            await RunAsync(ProviderRejectsUnusableCoverageBeforeEncoder).ConfigureAwait(false);
            await RunAsync(ProviderRequiresNetworkDisclosure).ConfigureAwait(false);
            await RunAsync(ProviderHonorsPreCancellation).ConfigureAwait(false);
            await RunAsync(ProviderRejectsOverlongPrompt).ConfigureAwait(false);
            await RunAsync(ResponsesProviderSendsResponsesStyle).ConfigureAwait(false);
            await RunAsync(ChatProviderSendsChatStyle).ConfigureAwait(false);
            await RunAsync(RequestDisablesStorageAndStreaming).ConfigureAwait(false);
            await RunAsync(RequestUsesConfiguredModelAndOutputLimit).ConfigureAwait(false);
            await RunAsync(RequestContainsOnlyEncodedTransformedImage).ConfigureAwait(false);
            await RunAsync(DegradedCoverageContextStatesValidFraction).ConfigureAwait(false);
            await RunAsync(NormalCoverageContextDoesNotClaimDegradation).ConfigureAwait(false);
            await RunAsync(ContextExcludesWorldModelHistory).ConfigureAwait(false);
            await RunAsync(EncoderInvalidFrameMapsInvalidFrame).ConfigureAwait(false);
            await RunAsync(EncoderUnsupportedMapsUnavailable).ConfigureAwait(false);
            await RunAsync(EncoderCancellationMapsCancelled).ConfigureAwait(false);
            await RunAsync(EncoderFailurePreservesSafeCode).ConfigureAwait(false);
            await RunAsync(EncodedImageIdentityMismatchRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(EncodedImagePolicyOverflowRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(EncodedImagePolicyMismatchRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(TransportCancellationMapsCancelled).ConfigureAwait(false);
            await RunAsync(TransportTimeoutMapsTimedOut).ConfigureAwait(false);
            await RunAsync(TransportUnavailableMapsUnavailable).ConfigureAwait(false);
            await RunAsync(TransportFailurePreservesSafeDetail).ConfigureAwait(false);
            Run(TransportSecretDetailIsRedacted);
            Run(TransportLongOpaqueDetailIsRedacted);
            await RunAsync(TransportSuccessReturnsValidatedText).ConfigureAwait(false);
            await RunAsync(OversizedTransportTextRejected).ConfigureAwait(false);
            Run(StructuredResultRejectsSuccessWithoutText);
            Run(StructuredFailureRequiresError);
            await RunAsync(ProviderDoesNotRetryOrFallback).ConfigureAwait(false);
            await RunAsync(ProviderConcurrencyLimitIsVisible).ConfigureAwait(false);
            await RunAsync(ProviderDisposalIsIdempotent).ConfigureAwait(false);
            await RunAsync(DisposedProviderRejectsInvocation).ConfigureAwait(false);
            await RunAsync(ExecutorCancellationRemainsCancellable).ConfigureAwait(false);
            await RunAsync(ProviderDoesNotDisposeInputFrame).ConfigureAwait(false);
            await RunAsync(ProviderDisposesEncodedPayloadAfterSuccess).ConfigureAwait(false);
            await RunAsync(ProviderExceptionDoesNotExposeMessage).ConfigureAwait(false);
            Run(SourceAndDocumentationDeclareFailClosedBoundary);

            Equal(ExpectedCaseCount, caseCount, "contract case count");
            Console.WriteLine(
                "RMA-115 OpenAI-compatible VLM adapter contracts passed: " +
                caseCount + ".");
            return 0;
        }
    }
}
