#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Providers;

namespace ReachyMini.Tests
{
    // RMA-195 phase D (VLM half): covers ReachyCloudVlmCredentialCoordinator,
    // mirroring ReachyCloudLlmCredentialCoordinatorTests exactly. EditMode
    // tests never run on Android, so SaveApiKey/GrantAuthorization can only
    // exercise their fail-closed "requires an Android device" / "save an API
    // key first" paths here -- the real Android-Keystore-backed success path
    // remains physical-acceptance-only.
    public sealed class ReachyCloudVlmCredentialCoordinatorTests
    {
        [Test]
        public void SaveProfilePersistsAValidProfile()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "https://api.openai.com",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "gpt-4o-mini");

                Assert.That(result, Does.Contain("saved"));
                ReachyProviderProfile? profile = coordinator.CurrentProfile;
                Assert.That(profile, Is.Not.Null);
                Assert.That(
                    profile!.ProviderId,
                    Is.EqualTo(ReachyCloudVlmCredentialCoordinator.ProviderId));
                Assert.That(
                    profile.BaseUri,
                    Is.EqualTo(new Uri("https://api.openai.com")));
                Assert.That(
                    profile.EndpointStyle,
                    Is.EqualTo(ReachyProviderEndpointStyle.ChatCompletions));
                Assert.That(
                    profile.GetModelId(ReachyProviderModelRole.Vision),
                    Is.EqualTo("gpt-4o-mini"));
                Assert.That(
                    profile.CredentialReference,
                    Is.EqualTo(ReachyCloudVlmCredentialCoordinator.CredentialReference));
            });
        }

        [Test]
        public void SaveProfileAcceptsALoopbackHttpUrlAsLocalDevelopmentCleartext()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "http://127.0.0.1:11434",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "qwen3-vl:8b-instruct");

                Assert.That(result, Does.Contain("local-development cleartext"));
                ReachyProviderProfile? profile = coordinator.CurrentProfile;
                Assert.That(profile, Is.Not.Null);
                Assert.That(profile!.UsesCleartextLocalDevelopmentTransport, Is.True);
            });
        }

        [Test]
        public void SaveProfileRejectsAPublicHttpUrl()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "http://example.com",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "gpt-4o-mini");

                Assert.That(result, Does.Contain("Cloud VLM provider profile is invalid"));
                Assert.That(coordinator.CurrentProfile, Is.Null);
            });
        }

        [Test]
        public void SaveProfileRejectsAnInvalidBaseUrl()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "not-a-url",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "gpt-4o-mini");

                Assert.That(result, Does.Contain("valid absolute base URL"));
                Assert.That(coordinator.CurrentProfile, Is.Null);
            });
        }

        [Test]
        public void SaveProfileRejectsABlankModelId()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "https://api.openai.com",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "   ");

                Assert.That(result, Does.Contain("model ID"));
                Assert.That(coordinator.CurrentProfile, Is.Null);
            });
        }

        [Test]
        public void SaveApiKeyFailsClosedOffAndroid()
        {
            WithCoordinator(coordinator =>
            {
                Assert.That(coordinator.SecretStoreAvailable, Is.False);
                string result = coordinator.SaveApiKey("sk-test-key");
                Assert.That(result, Does.Contain("requires an Android device"));
                Assert.That(coordinator.HasApiKey, Is.False);
            });
        }

        [Test]
        public void IsAuthorizedDefaultsFalse()
        {
            WithCoordinator(coordinator =>
            {
                Assert.That(coordinator.IsAuthorized, Is.False);
            });
        }

        [Test]
        public void GrantAuthorizationRequiresAProfileFirst()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.GrantAuthorization();
                Assert.That(result, Does.Contain("Save a cloud VLM provider profile"));
                Assert.That(coordinator.IsAuthorized, Is.False);
            });
        }

        [Test]
        public void GrantAuthorizationRequiresAnApiKeyOffAndroid()
        {
            WithCoordinator(coordinator =>
            {
                coordinator.SaveProfile(
                    "https://api.openai.com",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "gpt-4o-mini");

                string result = coordinator.GrantAuthorization();
                Assert.That(result, Does.Contain("Save an API key"));
                Assert.That(coordinator.IsAuthorized, Is.False);
            });
        }

        [Test]
        public void RevokeAuthorizationIsSafeWhenNeverGranted()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.RevokeAuthorization();
                Assert.That(result, Does.Contain("revoked"));
                Assert.That(coordinator.IsAuthorized, Is.False);
            });
        }

        private static void WithCoordinator(
            Action<ReachyCloudVlmCredentialCoordinator> action)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma195-cloud-vlm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ReachyProviderProfilePersistenceStore profileStore =
                    new ReachyProviderProfilePersistenceStore(
                        Path.Combine(root, "providers.json"));
                ReachyFallbackPolicyPersistenceStore fallbackStore =
                    new ReachyFallbackPolicyPersistenceStore(
                        new ReachyProviderFallbackPolicyEngine(),
                        Path.Combine(root, "fallback-policies.json"));
                ReachyCloudVlmCredentialCoordinator coordinator =
                    new ReachyCloudVlmCredentialCoordinator(profileStore, fallbackStore);
                coordinator.Initialize();
                action(coordinator);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
