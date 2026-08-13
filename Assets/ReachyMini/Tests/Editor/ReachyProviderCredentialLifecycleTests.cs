#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Providers;

namespace ReachyMini.Tests
{
    public sealed class ReachyProviderCredentialLifecycleTests
    {
        [Test]
        public void CreateUpdateReadAndDeleteHaveDistinctLifecycleSemantics()
        {
            WithLifecycle((lifecycle, secretStore, _) =>
            {
                byte[] initial = Encoding.UTF8.GetBytes("rma161-initial-test-credential");
                byte[] updated = Encoding.UTF8.GetBytes("rma161-updated-test-credential");
                try
                {
                    lifecycle.CreateCredential("provider.primary", initial);
                    Assert.That(secretStore.Contains("provider.primary"), Is.True);
                    Assert.Throws<InvalidOperationException>(
                        () => lifecycle.CreateCredential("provider.primary", updated));

                    byte[] firstRead = lifecycle.ReadCredential("provider.primary");
                    try
                    {
                        CollectionAssert.AreEqual(initial, firstRead);
                    }
                    finally
                    {
                        Array.Clear(firstRead, 0, firstRead.Length);
                    }

                    lifecycle.UpdateCredential("provider.primary", updated);
                    byte[] secondRead = lifecycle.ReadCredential("provider.primary");
                    try
                    {
                        CollectionAssert.AreEqual(updated, secondRead);
                    }
                    finally
                    {
                        Array.Clear(secondRead, 0, secondRead.Length);
                    }

                    Assert.That(lifecycle.DeleteCredential("provider.primary"), Is.True);
                    Assert.That(lifecycle.DeleteCredential("provider.primary"), Is.False);
                    Assert.Throws<KeyNotFoundException>(
                        () => lifecycle.UpdateCredential("provider.primary", updated));
                    Assert.Throws<KeyNotFoundException>(
                        () => lifecycle.ReadCredential("provider.primary"));
                }
                finally
                {
                    Array.Clear(initial, 0, initial.Length);
                    Array.Clear(updated, 0, updated.Length);
                }
            });
        }

        [Test]
        public void ProviderRemovalDeletesOnlyUnsharedCredentialReferences()
        {
            WithLifecycle((lifecycle, secretStore, profileStore) =>
            {
                ReachyProviderProfile first = CreateProfile(
                    "provider-one",
                    "shared.primary",
                    "provider-one.header");
                ReachyProviderProfile second = CreateProfile(
                    "provider-two",
                    "shared.primary",
                    null);
                profileStore.Upsert(first);
                profileStore.Upsert(second);
                PutTestCredential(secretStore, "shared.primary", 0x31);
                PutTestCredential(secretStore, "provider-one.header", 0x32);

                Assert.That(lifecycle.RemoveProvider("provider-one"), Is.True);
                Assert.That(secretStore.Contains("shared.primary"), Is.True);
                Assert.That(secretStore.Contains("provider-one.header"), Is.False);
                Assert.That(profileStore.TryGet("provider-one", out _), Is.False);
                Assert.That(profileStore.TryGet("provider-two", out _), Is.True);
            });
        }

        [Test]
        public void ProviderRemovalRollsBackProfileAndSecretsWhenDeletionFails()
        {
            WithLifecycle((lifecycle, secretStore, profileStore) =>
            {
                ReachyProviderProfile profile = CreateProfile(
                    "provider-rollback",
                    "rollback.primary",
                    "rollback.header");
                profileStore.Upsert(profile);
                PutTestCredential(secretStore, "rollback.primary", 0x41);
                PutTestCredential(secretStore, "rollback.header", 0x42);
                secretStore.FailDeleteReference = "rollback.header";

                Assert.Throws<InvalidOperationException>(
                    () => lifecycle.RemoveProvider("provider-rollback"));

                Assert.That(
                    profileStore.TryGet("provider-rollback", out ReachyProviderProfile? restored),
                    Is.True);
                Assert.That(restored, Is.Not.Null);
                Assert.That(secretStore.Contains("rollback.primary"), Is.True);
                Assert.That(secretStore.Contains("rollback.header"), Is.True);
                AssertCredentialByte(secretStore, "rollback.primary", 0x41);
                AssertCredentialByte(secretStore, "rollback.header", 0x42);
            });
        }

        [Test]
        public void RedactedProviderExportNeverContainsCredentialValuesOrReferences()
        {
            WithLifecycle((_, secretStore, profileStore) =>
            {
                ReachyProviderProfile profile = CreateProfile(
                    "provider-redaction",
                    "redaction.primary",
                    "redaction.header");
                profileStore.Upsert(profile);
                byte[] credential = Encoding.UTF8.GetBytes(
                    "rma161-full-secret-value-must-not-be-exported");
                try
                {
                    secretStore.Put("redaction.primary", credential);
                    string exported = profileStore.ExportRedactedJson();
                    StringAssert.DoesNotContain(
                        "rma161-full-secret-value-must-not-be-exported",
                        exported);
                    StringAssert.DoesNotContain("redaction.primary", exported);
                    StringAssert.DoesNotContain("redaction.header", exported);
                    StringAssert.Contains("\"credentialConfigured\": true", exported);
                }
                finally
                {
                    Array.Clear(credential, 0, credential.Length);
                }
            });
        }

        private static ReachyProviderProfile CreateProfile(
            string providerId,
            string credentialReference,
            string? headerSecretReference)
        {
            ReachyProviderHeaderBinding[] headers =
                headerSecretReference == null
                    ? Array.Empty<ReachyProviderHeaderBinding>()
                    : new[]
                    {
                        new ReachyProviderHeaderBinding(
                            "X-Provider-Token",
                            ReachyProviderHeaderValueKind.SecretReference,
                            headerSecretReference),
                    };
            return new ReachyProviderProfile(
                providerId,
                providerId,
                new Uri("https://example.invalid/v1", UriKind.Absolute),
                ReachyProviderEndpointStyle.Responses,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "model-test"),
                },
                headers,
                30_000,
                streamingEnabled: true,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference);
        }

        private static void PutTestCredential(
            FakeSecretStore secretStore,
            string reference,
            byte marker)
        {
            byte[] credential = { marker, (byte)(marker + 1), (byte)(marker + 2) };
            try
            {
                secretStore.Put(reference, credential);
            }
            finally
            {
                Array.Clear(credential, 0, credential.Length);
            }
        }

        private static void AssertCredentialByte(
            FakeSecretStore secretStore,
            string reference,
            byte expected)
        {
            byte[] credential = secretStore.GetSecret(reference);
            try
            {
                Assert.That(credential[0], Is.EqualTo(expected));
            }
            finally
            {
                Array.Clear(credential, 0, credential.Length);
            }
        }

        private static void WithLifecycle(
            Action<
                ReachyProviderCredentialLifecycle,
                FakeSecretStore,
                ReachyProviderProfilePersistenceStore> action)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma161-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            FakeSecretStore secretStore = new FakeSecretStore();
            try
            {
                ReachyProviderProfilePersistenceStore profileStore =
                    new ReachyProviderProfilePersistenceStore(
                        Path.Combine(root, "providers.json"));
                profileStore.Initialize();
                ReachyProviderCredentialLifecycle lifecycle =
                    new ReachyProviderCredentialLifecycle(profileStore, secretStore);
                action(lifecycle, secretStore, profileStore);
            }
            finally
            {
                secretStore.Clear();
                Directory.Delete(root, recursive: true);
            }
        }

        private sealed class FakeSecretStore : IReachyProviderSecretStore
        {
            private readonly Dictionary<string, byte[]> values =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public string? FailDeleteReference { get; set; }

            public void Put(string reference, byte[] secretUtf8)
            {
                ReachyProviderSecretReference.Validate(reference);
                if (values.TryGetValue(reference, out byte[]? previous))
                {
                    Array.Clear(previous, 0, previous.Length);
                }
                values[reference] = (byte[])secretUtf8.Clone();
            }

            public byte[] GetSecret(string reference)
            {
                ReachyProviderSecretReference.Validate(reference);
                if (!values.TryGetValue(reference, out byte[]? value))
                {
                    throw new KeyNotFoundException(
                        "The requested provider secret reference is not configured.");
                }
                return (byte[])value.Clone();
            }

            public bool Contains(string reference)
            {
                ReachyProviderSecretReference.Validate(reference);
                return values.ContainsKey(reference);
            }

            public bool Delete(string reference)
            {
                ReachyProviderSecretReference.Validate(reference);
                if (string.Equals(
                        reference,
                        FailDeleteReference,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Synthetic provider credential deletion failure.");
                }
                if (!values.TryGetValue(reference, out byte[]? value))
                {
                    return false;
                }
                values.Remove(reference);
                Array.Clear(value, 0, value.Length);
                return true;
            }

            public void Clear()
            {
                foreach (byte[] value in values.Values)
                {
                    Array.Clear(value, 0, value.Length);
                }
                values.Clear();
            }
        }
    }
}
