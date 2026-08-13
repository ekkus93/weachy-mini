#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Text;
using ReachyMini.Providers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma161CredentialAcceptanceBootstrap
    {
        public const string AcceptancePhaseLaunchExtra =
            "reachy_rma161_acceptance_phase";
        public const string ResultFileName =
            "rma161-credential-state.json";
        public const string ObjectName =
            "ReachyRma161CredentialAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            string phase = GetAcceptancePhaseFromLaunchIntent();
            if (string.IsNullOrEmpty(phase) ||
                Object.FindAnyObjectByType<ReachyRma161CredentialAcceptance>() != null)
            {
                return;
            }

            GameObject gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            ReachyRma161CredentialAcceptance acceptance =
                gameObject.AddComponent<ReachyRma161CredentialAcceptance>();
            acceptance.Configure(phase);
        }

        internal static string GetAcceptancePhaseFromLaunchIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent");
                return intent?.Call<string>(
                    "getStringExtra",
                    AcceptancePhaseLaunchExtra) ?? string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect the RMA-161 acceptance launch phase: " +
                    exception.GetType().Name);
                return string.Empty;
            }
#else
            return string.Empty;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyRma161CredentialAcceptance : MonoBehaviour
    {
        private const string PrimaryReference = "rma161.primary";
        private const string ProviderDeleteReference = "rma161.provider-delete";
        private const string AppClearReference = "rma161.app-clear";
        private const string ProviderDeleteProfileId = "rma161-provider-delete";
        private const string AcceptanceProfileFileName =
            "rma161-acceptance-provider-profiles.json";
        private const string InitialCredential =
            "rma161-physical-secret-initial-7f20b3";
        private const string UpdatedCredential =
            "rma161-physical-secret-updated-9d51c4";
        private const string ReplacementCredential =
            "rma161-physical-secret-replacement-a31e75";
        private const string ProviderDeleteCredential =
            "rma161-physical-secret-provider-delete-1c8b42";
        private const string AppClearCredential =
            "rma161-physical-secret-app-clear-e4d907";

        private string phase = string.Empty;

        internal void Configure(string requestedPhase)
        {
            phase = requestedPhase ?? throw new ArgumentNullException(nameof(requestedPhase));
        }

        private IEnumerator Start()
        {
            yield return null;

            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma161CredentialAcceptanceBootstrap.ResultFileName);
            DeleteIfPresent(resultPath);

            Report report;
            try
            {
                report = RunPhase();
            }
            catch (Exception exception)
            {
                report = Report.Failure(phase, exception.GetType().Name);
                Debug.LogError(
                    "RMA-161 credential acceptance phase failed: " +
                    exception.GetType().Name,
                    this);
            }

            WriteAtomically(
                resultPath,
                JsonUtility.ToJson(report, prettyPrint: true));
            Destroy(gameObject);
        }

        private Report RunPhase()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ReachyAndroidProviderSecretStore secretStore =
                new ReachyAndroidProviderSecretStore();
            string profilePath = Path.Combine(
                Application.persistentDataPath,
                AcceptanceProfileFileName);
            ReachyProviderProfilePersistenceStore profileStore =
                new ReachyProviderProfilePersistenceStore(profilePath);
            profileStore.Initialize();
            ReachyProviderCredentialLifecycle lifecycle =
                new ReachyProviderCredentialLifecycle(profileStore, secretStore);

            switch (phase)
            {
                case "prepare":
                    return RunPrepare(lifecycle, secretStore);
                case "verify-after-lock":
                    return RunVerifyAfterLock(lifecycle);
                case "invalidate":
                    return RunInvalidation(
                        lifecycle,
                        secretStore,
                        profileStore);
                case "verify-cleared":
                    return RunVerifyCleared(lifecycle, secretStore);
                default:
                    throw new InvalidOperationException(
                        "Unsupported RMA-161 acceptance phase.");
            }
#else
            throw new PlatformNotSupportedException(
                "RMA-161 credential acceptance requires an Android player.");
#endif
        }

        private static Report RunPrepare(
            ReachyProviderCredentialLifecycle lifecycle,
            ReachyAndroidProviderSecretStore secretStore)
        {
            CleanupReference(lifecycle, PrimaryReference);
            CleanupReference(lifecycle, ProviderDeleteReference);
            CleanupReference(lifecycle, AppClearReference);

            byte[] initial = Encoding.UTF8.GetBytes(InitialCredential);
            byte[] updated = Encoding.UTF8.GetBytes(UpdatedCredential);
            try
            {
                lifecycle.CreateCredential(PrimaryReference, initial);
                RequireCredential(lifecycle, PrimaryReference, initial);
                lifecycle.UpdateCredential(PrimaryReference, updated);
                RequireCredential(lifecycle, PrimaryReference, updated);
            }
            finally
            {
                Array.Clear(initial, 0, initial.Length);
                Array.Clear(updated, 0, updated.Length);
            }

            if (!secretStore.Contains(PrimaryReference) ||
                !ReachyAndroidProviderSecretStore.HasEncryptionKeyForAcceptance())
            {
                throw new InvalidOperationException(
                    "RMA-161 prepare did not retain the encrypted credential and key.");
            }

            return Report.Success(
                "prepare",
                credentialRoundTrip: true,
                keyPresent: true);
        }

        private static Report RunVerifyAfterLock(
            ReachyProviderCredentialLifecycle lifecycle)
        {
            byte[] expected = Encoding.UTF8.GetBytes(UpdatedCredential);
            try
            {
                RequireCredential(lifecycle, PrimaryReference, expected);
            }
            finally
            {
                Array.Clear(expected, 0, expected.Length);
            }

            return Report.Success(
                "verify-after-lock",
                credentialRoundTrip: true,
                lockTransitionVerified: true);
        }

        private static Report RunInvalidation(
            ReachyProviderCredentialLifecycle lifecycle,
            ReachyAndroidProviderSecretStore secretStore,
            ReachyProviderProfilePersistenceStore profileStore)
        {
            if (!secretStore.Contains(PrimaryReference))
            {
                throw new InvalidOperationException(
                    "RMA-161 invalidation phase requires the prepared credential.");
            }
            if (!ReachyAndroidProviderSecretStore.HasEncryptionKeyForAcceptance())
            {
                throw new InvalidOperationException(
                    "RMA-161 invalidation phase requires the prepared Keystore key.");
            }
            if (!ReachyAndroidProviderSecretStore.InvalidateEncryptionKeyForAcceptance())
            {
                throw new InvalidOperationException(
                    "RMA-161 test invalidation did not remove the prepared Keystore key.");
            }

            bool readFailedClosed = ExpectKeyUnavailable(
                () =>
                {
                    byte[] value = lifecycle.ReadCredential(PrimaryReference);
                    Array.Clear(value, 0, value.Length);
                });

            byte[] failedUpdate = Encoding.UTF8.GetBytes(ReplacementCredential);
            bool updateFailedClosed;
            try
            {
                updateFailedClosed = ExpectKeyUnavailable(
                    () => lifecycle.UpdateCredential(
                        PrimaryReference,
                        failedUpdate));
            }
            finally
            {
                Array.Clear(failedUpdate, 0, failedUpdate.Length);
            }

            bool retainedEncryptedRecord = secretStore.Contains(PrimaryReference);
            bool explicitDeleteSucceeded = lifecycle.DeleteCredential(PrimaryReference);
            if (secretStore.Contains(PrimaryReference) ||
                ReachyAndroidProviderSecretStore.HasEncryptionKeyForAcceptance())
            {
                throw new InvalidOperationException(
                    "RMA-161 explicit deletion did not clear the invalidated record/key state.");
            }

            byte[] replacement = Encoding.UTF8.GetBytes(ReplacementCredential);
            try
            {
                lifecycle.CreateCredential(PrimaryReference, replacement);
                RequireCredential(lifecycle, PrimaryReference, replacement);
            }
            finally
            {
                Array.Clear(replacement, 0, replacement.Length);
            }
            bool replacementKeyCreated =
                ReachyAndroidProviderSecretStore.HasEncryptionKeyForAcceptance();

            byte[] providerDelete = Encoding.UTF8.GetBytes(ProviderDeleteCredential);
            try
            {
                lifecycle.CreateCredential(ProviderDeleteReference, providerDelete);
            }
            finally
            {
                Array.Clear(providerDelete, 0, providerDelete.Length);
            }
            profileStore.Upsert(CreateProviderDeletionProfile());
            bool providerRemoved = lifecycle.RemoveProvider(ProviderDeleteProfileId);
            bool providerCredentialRemoved =
                providerRemoved && !secretStore.Contains(ProviderDeleteReference);

            byte[] appClear = Encoding.UTF8.GetBytes(AppClearCredential);
            try
            {
                lifecycle.CreateCredential(AppClearReference, appClear);
                RequireCredential(lifecycle, AppClearReference, appClear);
            }
            finally
            {
                Array.Clear(appClear, 0, appClear.Length);
            }
            lifecycle.DeleteCredential(PrimaryReference);

            if (!readFailedClosed ||
                !updateFailedClosed ||
                !retainedEncryptedRecord ||
                !explicitDeleteSucceeded ||
                !replacementKeyCreated ||
                !providerCredentialRemoved ||
                !secretStore.Contains(AppClearReference))
            {
                throw new InvalidOperationException(
                    "RMA-161 invalidation lifecycle did not satisfy every required invariant.");
            }

            return Report.Success(
                "invalidate",
                invalidationTriggered: true,
                readFailedClosedAfterInvalidation: true,
                updateFailedClosedAfterInvalidation: true,
                encryptedRecordRetainedAfterInvalidation: true,
                explicitDeleteSucceeded: true,
                replacementKeyCreated: true,
                providerDeleteRemovedCredential: true,
                appClearCredentialPrepared: true);
        }

        private static Report RunVerifyCleared(
            ReachyProviderCredentialLifecycle lifecycle,
            ReachyAndroidProviderSecretStore secretStore)
        {
            bool appDataClearRemovedCredential =
                !secretStore.Contains(AppClearReference) &&
                !secretStore.Contains(PrimaryReference) &&
                !secretStore.Contains(ProviderDeleteReference);
            if (!appDataClearRemovedCredential)
            {
                throw new InvalidOperationException(
                    "RMA-161 app-data clear left a provider credential record configured.");
            }

            byte[] replacement = Encoding.UTF8.GetBytes(ReplacementCredential);
            try
            {
                lifecycle.CreateCredential(PrimaryReference, replacement);
                RequireCredential(lifecycle, PrimaryReference, replacement);
            }
            finally
            {
                Array.Clear(replacement, 0, replacement.Length);
            }
            bool postClearCreateReadSucceeded = lifecycle.DeleteCredential(PrimaryReference);
            if (!postClearCreateReadSucceeded || secretStore.Contains(PrimaryReference))
            {
                throw new InvalidOperationException(
                    "RMA-161 credential store did not return to a clean " +
                    "usable state after app-data clear.");
            }

            return Report.Success(
                "verify-cleared",
                appDataClearRemovedCredential: true,
                postClearCreateReadSucceeded: true);
        }

        private static ReachyProviderProfile CreateProviderDeletionProfile()
        {
            return new ReachyProviderProfile(
                ProviderDeleteProfileId,
                "RMA-161 deletion acceptance",
                new Uri("https://example.invalid/v1", UriKind.Absolute),
                ReachyProviderEndpointStyle.Responses,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "rma161-test-model"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30_000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                ProviderDeleteReference);
        }

        private static bool ExpectKeyUnavailable(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception exception)
            {
                return exception.ToString().Contains(
                    "RMA161_KEY_UNAVAILABLE",
                    StringComparison.Ordinal);
            }
        }

        private static void RequireCredential(
            ReachyProviderCredentialLifecycle lifecycle,
            string reference,
            byte[] expected)
        {
            byte[] actual = lifecycle.ReadCredential(reference);
            try
            {
                if (!BytesEqual(actual, expected))
                {
                    throw new InvalidOperationException(
                        "RMA-161 credential bytes changed during the lifecycle operation.");
                }
            }
            finally
            {
                Array.Clear(actual, 0, actual.Length);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; ++index)
            {
                difference |= left[index] ^ right[index];
            }
            return difference == 0;
        }

        private static void CleanupReference(
            ReachyProviderCredentialLifecycle lifecycle,
            string reference)
        {
            lifecycle.DeleteCredential(reference);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            string temporaryPath = path + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporaryPath, path);
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "failed";
            public string phase = string.Empty;
            public bool credential_round_trip;
            public bool lock_transition_verified;
            public bool key_present;
            public bool invalidation_triggered;
            public bool read_failed_closed_after_invalidation;
            public bool update_failed_closed_after_invalidation;
            public bool encrypted_record_retained_after_invalidation;
            public bool explicit_delete_succeeded;
            public bool replacement_key_created;
            public bool provider_delete_removed_credential;
            public bool app_clear_credential_prepared;
            public bool app_data_clear_removed_credential;
            public bool post_clear_create_read_succeeded;
            public bool full_secret_in_report;
            public string message = string.Empty;

            public static Report Success(
                string phase,
                bool credentialRoundTrip = false,
                bool lockTransitionVerified = false,
                bool keyPresent = false,
                bool invalidationTriggered = false,
                bool readFailedClosedAfterInvalidation = false,
                bool updateFailedClosedAfterInvalidation = false,
                bool encryptedRecordRetainedAfterInvalidation = false,
                bool explicitDeleteSucceeded = false,
                bool replacementKeyCreated = false,
                bool providerDeleteRemovedCredential = false,
                bool appClearCredentialPrepared = false,
                bool appDataClearRemovedCredential = false,
                bool postClearCreateReadSucceeded = false)
            {
                return new Report
                {
                    status = "passed",
                    phase = phase,
                    credential_round_trip = credentialRoundTrip,
                    lock_transition_verified = lockTransitionVerified,
                    key_present = keyPresent,
                    invalidation_triggered = invalidationTriggered,
                    read_failed_closed_after_invalidation =
                        readFailedClosedAfterInvalidation,
                    update_failed_closed_after_invalidation =
                        updateFailedClosedAfterInvalidation,
                    encrypted_record_retained_after_invalidation =
                        encryptedRecordRetainedAfterInvalidation,
                    explicit_delete_succeeded = explicitDeleteSucceeded,
                    replacement_key_created = replacementKeyCreated,
                    provider_delete_removed_credential = providerDeleteRemovedCredential,
                    app_clear_credential_prepared = appClearCredentialPrepared,
                    app_data_clear_removed_credential = appDataClearRemovedCredential,
                    post_clear_create_read_succeeded = postClearCreateReadSucceeded,
                    full_secret_in_report = false,
                    message = "RMA-161 credential lifecycle acceptance phase passed.",
                };
            }

            public static Report Failure(string phase, string exceptionType)
            {
                return new Report
                {
                    status = "failed",
                    phase = phase,
                    full_secret_in_report = false,
                    message =
                        "RMA-161 credential lifecycle acceptance phase failed: " +
                        exceptionType,
                };
            }
        }
    }
}
