#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void ProviderConfigurationRequiresVerifiedArtifacts()
        {
            Throws<ArgumentException>(
                () => new LocalVlmProviderConfiguration(
                    Manifest(),
                    "/models/example",
                    "provider-instance",
                    artifactIntegrityVerified: false),
                "unverified artifacts");
        }

        private static void ProviderConfigurationRejectsNetworkRoots()
        {
            Throws<ArgumentException>(
                () => Configuration("https://example.com/models"),
                "https artifact root");
            Throws<ArgumentException>(
                () => Configuration("ftp://example.com/models"),
                "ftp artifact root");
            Throws<ArgumentException>(
                () => Configuration("models/example"),
                "relative artifact root");
            Throws<ArgumentException>(
                () => Configuration("file://server/share"),
                "remote file URI");
            Throws<ArgumentException>(
                () => Configuration(
                    new string((char)92, 2) +
                    "server" + (char)92 + "share"),
                "UNC artifact root");
            Throws<ArgumentException>(
                () => Configuration("//server/share"),
                "slash network share");
            Throws<ArgumentException>(
                () => Configuration("content:///models/example"),
                "content URI without authority");
        }

        private static void ProviderConfigurationAcceptsLocalRoots()
        {
            Equal("/models/example", Configuration("/models/example").LocalArtifactRoot, "unix path");
            Equal(
                "file:///models/example",
                Configuration("file:///models/example").LocalArtifactRoot,
                "file URI");
            Equal(
                "content://models/example",
                Configuration("content://models/example").LocalArtifactRoot,
                "content URI");
            string windowsRoot =
                "C:" + (char)92 + "models" + (char)92 + "example";
            Equal(
                windowsRoot,
                Configuration(windowsRoot).LocalArtifactRoot,
                "Windows drive path");
        }
    }
}
