#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    // RMA-195 (2026-08-22): locks in that a configured provider endpoint is
    // classified by where it actually is, not assumed to be Cloud, and that
    // the classification fails safe toward the more sensitive boundary.
    internal static class Rma195PrivacyBoundaryClassifierTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            LoopbackIsLocalNetwork();
            PrivateAddressesAreLocalNetwork();
            LocalOnlyDnsSuffixesAreLocalNetwork();
            PublicHostsAreCloud();
            HttpsOnAPrivateAddressIsStillLocalNetwork();
            PublicNameIsCloudEvenThoughItCouldResolvePrivately();
            NullUriIsRejected();
            EveryBoundaryHasADescription();
        }

        private static void LoopbackIsLocalNetwork()
        {
            Boundary(
                "http://127.0.0.1:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "loopback ipv4");
            Boundary(
                "http://localhost:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "localhost");
            Boundary(
                "http://[::1]:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "loopback ipv6");
        }

        private static void PrivateAddressesAreLocalNetwork()
        {
            Boundary(
                "http://192.168.1.50:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "rfc1918 192.168/16");
            Boundary(
                "http://10.0.0.7:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "rfc1918 10/8");
            Boundary(
                "http://172.16.4.9:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "rfc1918 172.16/12");
        }

        private static void LocalOnlyDnsSuffixesAreLocalNetwork()
        {
            Boundary(
                "http://kawa.local:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                ".local suffix");
            Boundary(
                "http://server.localhost:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                ".localhost suffix");
        }

        private static void PublicHostsAreCloud()
        {
            Boundary(
                "https://api.openai.com",
                ReachyProviderPrivacyBoundary.Cloud,
                "public cloud host");
            Boundary(
                "https://8.8.8.8",
                ReachyProviderPrivacyBoundary.Cloud,
                "public ip literal");
        }

        private static void HttpsOnAPrivateAddressIsStillLocalNetwork()
        {
            // Scheme is irrelevant to *where* the data goes; a TLS-secured
            // LAN endpoint is still on the user's own network.
            Boundary(
                "https://192.168.1.50:11434",
                ReachyProviderPrivacyBoundary.LocalNetwork,
                "https on a private address");
        }

        private static void PublicNameIsCloudEvenThoughItCouldResolvePrivately()
        {
            // Deliberately fails safe: only literal private IPs are trusted,
            // so a public name that happens to resolve to a private address
            // is never quietly downgraded to LocalNetwork.
            Boundary(
                "https://internal.example.com",
                ReachyProviderPrivacyBoundary.Cloud,
                "public dns name");
        }

        private static void NullUriIsRejected()
        {
            bool threw = false;
            try
            {
                ReachyProviderPrivacyBoundaryClassifier.Classify(null!);
            }
            catch (ArgumentNullException)
            {
                threw = true;
            }
            Equal(true, threw, "null uri rejected");
        }

        private static void EveryBoundaryHasADescription()
        {
            foreach (object value in Enum.GetValues(typeof(ReachyProviderPrivacyBoundary)))
            {
                string description = ReachyProviderPrivacyBoundaryClassifier.DescribeBoundary(
                    (ReachyProviderPrivacyBoundary)value);
                if (string.IsNullOrWhiteSpace(description))
                {
                    throw new InvalidOperationException(
                        $"Managed test failed: boundary {value} has no description.");
                }
            }

            bool threw = false;
            try
            {
                ReachyProviderPrivacyBoundaryClassifier.DescribeBoundary(
                    (ReachyProviderPrivacyBoundary)999);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Equal(true, threw, "undefined boundary description fails closed");
        }

        private static void Boundary(
            string uri,
            ReachyProviderPrivacyBoundary expected,
            string description)
        {
            ReachyProviderPrivacyBoundary actual =
                ReachyProviderPrivacyBoundaryClassifier.Classify(new Uri(uri, UriKind.Absolute));
            Equal(expected, actual, description);
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }
    }
}
