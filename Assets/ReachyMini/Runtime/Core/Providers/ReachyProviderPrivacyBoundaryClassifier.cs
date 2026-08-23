#nullable enable

using System;
using ReachyMini.Security;

namespace ReachyMini.Providers
{
    // RMA-195 (2026-08-22): decides which ReachyProviderPrivacyBoundary a
    // configured remote provider endpoint actually crosses, instead of every
    // call site assuming Cloud.
    //
    // Before this existed, ReachyLocalLlmProviderApplicationService and
    // ReachyAndroidPerceptionApplicationService.CloudVlm.cs both hard-coded
    // ReachyProviderPrivacyBoundary.Cloud for their switch target, so a
    // provider pointed at a server on the user's own LAN (an Ollama instance
    // at http://192.168.1.50:11434, say) was reported -- to the fallback
    // policy engine, to its recorded diagnostics, and to any disclosure text
    // derived from them -- as sending data to the cloud. That is simply not
    // true, and the spec's own requirement to "indicate which data type is
    // about to leave the device" is not met by mislabelling where it goes.
    // ReachyProviderPrivacyBoundary.LocalNetwork already existed for exactly
    // this case but was never assigned by any caller.
    //
    // This deliberately does NOT relax any gate. ReachyProviderSwitchRequest.
    // TargetsNetworkBoundary treats LocalNetwork and Cloud identically, so a
    // LocalNetwork target still requires ReachyFallbackPolicy.
    // AllowNetworkProviderSwitch and still trips the ChangesPrivacyBoundary
    // confirmation when moving off OnDevice. Classifying honestly changes the
    // label, never the authorization.
    public static class ReachyProviderPrivacyBoundaryClassifier
    {
        // Fails safe toward the more sensitive classification: only a literal
        // loopback/private address or an explicitly local-only DNS suffix is
        // treated as LocalNetwork. A public hostname that happens to resolve
        // to a private address (DNS rebinding) is not parsed as an IP here, so
        // it classifies as Cloud rather than being quietly downgraded.
        //
        // Scheme is irrelevant to this question: an https:// endpoint on the
        // user's own LAN is still LocalNetwork, and cleartext to a public host
        // is still Cloud (and is rejected outright elsewhere -- see
        // ReachyProviderTlsMode).
        public static ReachyProviderPrivacyBoundary Classify(Uri baseUri)
        {
            if (baseUri == null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }
            return ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost(baseUri)
                ? ReachyProviderPrivacyBoundary.LocalNetwork
                : ReachyProviderPrivacyBoundary.Cloud;
        }

        // Short, user-facing description of where data actually goes. Used by
        // the cloud LLM/VLM settings sections, which are the one place the
        // real configured endpoint is known (ReachyProviderSelection describes
        // a generic execution *preference* and genuinely cannot answer this).
        public static string DescribeBoundary(ReachyProviderPrivacyBoundary boundary)
        {
            switch (boundary)
            {
                case ReachyProviderPrivacyBoundary.OnDevice:
                    return "On device — data does not leave this phone.";
                case ReachyProviderPrivacyBoundary.DeviceService:
                    return "Android system service — data may leave this phone.";
                case ReachyProviderPrivacyBoundary.LocalNetwork:
                    return "Local network — data leaves this phone but stays on your network.";
                case ReachyProviderPrivacyBoundary.Cloud:
                    return "Cloud — data leaves your network to a remote provider.";
                default:
                    throw new ArgumentOutOfRangeException(nameof(boundary));
            }
        }
    }
}
