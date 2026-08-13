#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Providers;

namespace ReachyMini.AppState
{
    public sealed class ReachyProviderCredentialLifecycle
    {
        private const int MaximumCredentialBytes = 16 * 1024;

        private readonly object gate = new object();
        private readonly ReachyProviderProfilePersistenceStore profileStore;
        private readonly IReachyProviderSecretStore secretStore;

        public ReachyProviderCredentialLifecycle(
            ReachyProviderProfilePersistenceStore profileStore,
            IReachyProviderSecretStore secretStore)
        {
            this.profileStore = profileStore ??
                throw new ArgumentNullException(nameof(profileStore));
            this.secretStore = secretStore ??
                throw new ArgumentNullException(nameof(secretStore));
        }

        public void CreateCredential(string reference, byte[] credentialUtf8)
        {
            ReachyProviderSecretReference.Validate(reference);
            ValidateCredentialBytes(credentialUtf8, nameof(credentialUtf8));
            lock (gate)
            {
                if (secretStore.Contains(reference))
                {
                    throw new InvalidOperationException(
                        "The provider credential reference is already configured.");
                }
                secretStore.Put(reference, credentialUtf8);
            }
        }

        public void UpdateCredential(string reference, byte[] credentialUtf8)
        {
            ReachyProviderSecretReference.Validate(reference);
            ValidateCredentialBytes(credentialUtf8, nameof(credentialUtf8));
            lock (gate)
            {
                if (!secretStore.Contains(reference))
                {
                    throw new KeyNotFoundException(
                        "The provider credential reference is not configured.");
                }
                secretStore.Put(reference, credentialUtf8);
            }
        }

        public byte[] ReadCredential(string reference)
        {
            ReachyProviderSecretReference.Validate(reference);
            lock (gate)
            {
                byte[] credential = secretStore.GetSecret(reference);
                try
                {
                    ValidateStoredCredentialBytes(credential);
                    return credential;
                }
                catch
                {
                    Array.Clear(credential, 0, credential.Length);
                    throw;
                }
            }
        }

        public bool DeleteCredential(string reference)
        {
            ReachyProviderSecretReference.Validate(reference);
            lock (gate)
            {
                return secretStore.Delete(reference);
            }
        }

        public bool RemoveProvider(string providerId)
        {
            if (providerId == null)
            {
                throw new ArgumentNullException(nameof(providerId));
            }

            lock (gate)
            {
                if (!profileStore.TryGet(
                        providerId,
                        out ReachyProviderProfile? profile) ||
                    profile == null)
                {
                    return false;
                }

                HashSet<string> orphanReferences =
                    FindReferencesExclusiveToProvider(profile);
                Dictionary<string, byte[]> rollbackMaterial =
                    new Dictionary<string, byte[]>(StringComparer.Ordinal);
                try
                {
                    CaptureCredentialMaterial(
                        orphanReferences,
                        rollbackMaterial);
                    if (!profileStore.Remove(providerId))
                    {
                        throw new InvalidOperationException(
                            "Provider profile disappeared before credential cleanup could begin.");
                    }

                    try
                    {
                        DeleteCapturedCredentials(
                            orphanReferences,
                            rollbackMaterial);
                    }
                    catch (Exception deletionException)
                    {
                        RollBackProviderRemoval(
                            profile,
                            rollbackMaterial,
                            deletionException);
                    }
                    return true;
                }
                finally
                {
                    ClearCredentialMaterial(rollbackMaterial);
                }
            }
        }

        private HashSet<string> FindReferencesExclusiveToProvider(
            ReachyProviderProfile removedProfile)
        {
            HashSet<string> candidates = CollectSecretReferences(removedProfile);
            IReadOnlyList<ReachyProviderProfile> profiles = profileStore.Profiles;
            for (int index = 0; index < profiles.Count; ++index)
            {
                ReachyProviderProfile profile = profiles[index];
                if (string.Equals(
                        profile.ProviderId,
                        removedProfile.ProviderId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                candidates.ExceptWith(CollectSecretReferences(profile));
            }
            return candidates;
        }

        private void CaptureCredentialMaterial(
            HashSet<string> references,
            Dictionary<string, byte[]> rollbackMaterial)
        {
            foreach (string reference in references)
            {
                if (!secretStore.Contains(reference))
                {
                    continue;
                }
                byte[] credential = secretStore.GetSecret(reference);
                try
                {
                    ValidateStoredCredentialBytes(credential);
                    rollbackMaterial.Add(reference, credential);
                }
                catch
                {
                    Array.Clear(credential, 0, credential.Length);
                    throw;
                }
            }
        }

        private void DeleteCapturedCredentials(
            HashSet<string> references,
            Dictionary<string, byte[]> rollbackMaterial)
        {
            foreach (string reference in references)
            {
                bool deleted = secretStore.Delete(reference);
                if (rollbackMaterial.ContainsKey(reference) && !deleted)
                {
                    throw new InvalidOperationException(
                        "A captured provider credential disappeared during provider deletion.");
                }
            }
        }

        private void RollBackProviderRemoval(
            ReachyProviderProfile profile,
            Dictionary<string, byte[]> rollbackMaterial,
            Exception deletionException)
        {
            List<Exception> rollbackFailures = new List<Exception>();
            foreach (KeyValuePair<string, byte[]> credential in rollbackMaterial)
            {
                try
                {
                    secretStore.Put(credential.Key, credential.Value);
                }
                catch (Exception exception)
                {
                    rollbackFailures.Add(exception);
                }
            }

            try
            {
                profileStore.Upsert(profile);
            }
            catch (Exception exception)
            {
                rollbackFailures.Add(exception);
            }

            if (rollbackFailures.Count != 0)
            {
                List<Exception> failures =
                    new List<Exception>(rollbackFailures.Count + 1)
                {
                    deletionException,
                };
                failures.AddRange(rollbackFailures);
                throw new AggregateException(
                    "Provider deletion failed and credential rollback was incomplete.",
                    failures);
            }

            throw new InvalidOperationException(
                "Provider deletion failed; the original profile and " +
                "credential material were restored.",
                deletionException);
        }

        private static HashSet<string> CollectSecretReferences(
            ReachyProviderProfile profile)
        {
            HashSet<string> references =
                new HashSet<string>(StringComparer.Ordinal);
            if (profile.CredentialReference != null)
            {
                references.Add(profile.CredentialReference);
            }
            for (int index = 0; index < profile.Headers.Count; ++index)
            {
                ReachyProviderHeaderBinding header = profile.Headers[index];
                if (header.ValueKind == ReachyProviderHeaderValueKind.SecretReference)
                {
                    references.Add(header.ValueOrSecretReference);
                }
            }
            return references;
        }

        private static void ClearCredentialMaterial(
            Dictionary<string, byte[]> rollbackMaterial)
        {
            foreach (byte[] credential in rollbackMaterial.Values)
            {
                Array.Clear(credential, 0, credential.Length);
            }
            rollbackMaterial.Clear();
        }

        private static void ValidateStoredCredentialBytes(byte[] credential)
        {
            if (credential.Length == 0 || credential.Length > MaximumCredentialBytes)
            {
                throw new InvalidOperationException(
                    "Stored provider credential bytes are empty or exceed the allowed size.");
            }
        }

        private static void ValidateCredentialBytes(
            byte[] credentialUtf8,
            string parameterName)
        {
            if (credentialUtf8 == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (credentialUtf8.Length == 0 ||
                credentialUtf8.Length > MaximumCredentialBytes)
            {
                throw new ArgumentException(
                    "Provider credential bytes are empty or exceed the allowed size.",
                    parameterName);
            }
        }
    }
}
