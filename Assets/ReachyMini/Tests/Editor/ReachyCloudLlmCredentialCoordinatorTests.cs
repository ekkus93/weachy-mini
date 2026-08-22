#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Providers;

namespace ReachyMini.Tests
{
    // RMA-195 phase D: covers ReachyCloudLlmCredentialCoordinator, the
    // settings-facing counterpart to ReachyLocalLlmProviderApplicationService's
    // cloud LLM path. EditMode tests never run on Android, so SaveApiKey/
    // GrantAuthorization can only exercise their fail-closed
    // "requires an Android device" / "save an API key first" paths here --
    // the real Android-Keystore-backed success path is covered by the
    // RMA-134/135-style physical acceptance harnesses, matching the same
    // off-Android testing boundary already established for
    // ReachyLocalLlmProviderApplicationServiceTests.
    public sealed class ReachyCloudLlmCredentialCoordinatorTests
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
                    Is.EqualTo(ReachyCloudLlmCredentialCoordinator.ProviderId));
                Assert.That(
                    profile.BaseUri,
                    Is.EqualTo(new Uri("https://api.openai.com")));
                Assert.That(
                    profile.EndpointStyle,
                    Is.EqualTo(ReachyProviderEndpointStyle.ChatCompletions));
                Assert.That(
                    profile.GetModelId(ReachyProviderModelRole.Text),
                    Is.EqualTo("gpt-4o-mini"));
                Assert.That(
                    profile.CredentialReference,
                    Is.EqualTo(ReachyCloudLlmCredentialCoordinator.CredentialReference));
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
        public void SaveProfileRejectsAnHttpBaseUrl()
        {
            WithCoordinator(coordinator =>
            {
                string result = coordinator.SaveProfile(
                    "http://api.openai.com",
                    ReachyProviderEndpointStyle.ChatCompletions,
                    "gpt-4o-mini");

                Assert.That(result, Does.Contain("Cloud LLM provider profile is invalid"));
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
                Assert.That(result, Does.Contain("Save a cloud LLM provider profile"));
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
            Action<ReachyCloudLlmCredentialCoordinator> action)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma195-cloud-llm-" + Guid.NewGuid().ToString("N"));
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
                ReachyCloudLlmCredentialCoordinator coordinator =
                    new ReachyCloudLlmCredentialCoordinator(profileStore, fallbackStore);
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
