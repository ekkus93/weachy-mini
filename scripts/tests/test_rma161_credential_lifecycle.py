import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APPLICATION = ROOT / "Assets/ReachyMini/Runtime/Application"
PROVIDERS = ROOT / "Assets/ReachyMini/Runtime/Core/Providers"
ANDROID_BRIDGE = (
    ROOT
    / "Assets/Plugins/Android/ReachyProviderSecurity.androidlib/src/main/java"
    / "com/ekkus93/weachy/providers/ReachyProviderSecretBridge.java"
)
EDITOR_TEST = ROOT / "Assets/ReachyMini/Tests/Editor/ReachyProviderCredentialLifecycleTests.cs"


class Rma161CredentialLifecycleTests(unittest.TestCase):
    def test_android_bridge_keeps_plaintext_out_of_persistent_storage(self) -> None:
        source = ANDROID_BRIDGE.read_text(encoding="utf-8")
        for contract in (
            'private static final String KEYSTORE = "AndroidKeyStore";',
            'private static final String CIPHER = "AES/GCM/NoPadding";',
            ".setRandomizedEncryptionRequired(true)",
            ".setUserAuthenticationRequired(false)",
            "cipher.init(Cipher.ENCRYPT_MODE, key);",
            "cipher.init(Cipher.ENCRYPT_MODE, replacement);",
            "iv = cipher.getIV();",
            "cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8))",
            "Base64.encodeToString(ciphertext, Base64.NO_WRAP)",
            "clear(iv);",
            "clear(ciphertext);",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("SecureRandom", source)
        self.assertNotIn("putString(reference,", source)
        self.assertNotIn("new String(secretUtf8", source)
        self.assertNotIn("Log.", source)
        self.assertNotIn("System.out", source)

    def test_key_loss_is_fail_closed_and_requires_explicit_record_cleanup(self) -> None:
        source = ANDROID_BRIDGE.read_text(encoding="utf-8")
        for contract in (
            "KeyPermanentlyInvalidatedException",
            "UnrecoverableKeyException",
            "RMA161_KEY_UNAVAILABLE",
            "if (hasStoredSecretRecords(stored))",
            "throw keyUnavailable(null);",
            "if (!hasStoredSecretRecords(stored))",
            "deleteKeyIfPresent();",
        ):
            self.assertIn(contract, source)
        self.assertIn(
            "encrypted credential records require explicit deletion before replacement",
            source,
        )

    def test_debug_key_invalidation_hook_and_lock_state_probe_are_bounded(self) -> None:
        source = ANDROID_BRIDGE.read_text(encoding="utf-8")
        for contract in (
            "isKeyguardLocked(Context context)",
            "isDeviceSecure(Context context)",
            "hasEncryptionKey(Context context)",
            "invalidateEncryptionKeyForTesting(Context context)",
            "ApplicationInfo.FLAG_DEBUGGABLE",
            "requireDebuggableApplication(context);",
        ):
            self.assertIn(contract, source)

        managed = (APPLICATION / "ReachyAndroidProviderSecretStore.cs").read_text(encoding="utf-8")
        for contract in (
            "IsKeyguardLockedForAcceptance",
            "IsDeviceSecureForAcceptance",
            "HasEncryptionKeyForAcceptance",
            "InvalidateEncryptionKeyForAcceptance",
        ):
            self.assertIn(contract, managed)

    def test_managed_lifecycle_owns_provider_cleanup_and_zeroes_rollback_material(self) -> None:
        lifecycle = (APPLICATION / "ReachyProviderCredentialLifecycle.cs").read_text(
            encoding="utf-8"
        )
        for contract in (
            "CreateCredential",
            "UpdateCredential",
            "ReadCredential",
            "DeleteCredential",
            "RemoveProvider",
            "FindReferencesExclusiveToProvider",
            "CaptureCredentialMaterial",
            "RollBackProviderRemoval",
            "Array.Clear(credential, 0, credential.Length);",
        ):
            self.assertIn(contract, lifecycle)
        self.assertNotIn("Debug.Log", lifecycle)
        self.assertNotIn("Encoding.UTF8.GetString", lifecycle)

    def test_behavioral_fixture_covers_shared_refs_rollback_and_redaction(self) -> None:
        source = EDITOR_TEST.read_text(encoding="utf-8")
        for fixture in (
            "CreateUpdateReadAndDeleteHaveDistinctLifecycleSemantics",
            "ProviderRemovalDeletesOnlyUnsharedCredentialReferences",
            "ProviderRemovalRollsBackProfileAndSecretsWhenDeletionFails",
            "RedactedProviderExportNeverContainsCredentialValuesOrReferences",
        ):
            self.assertIn(fixture, source)
        self.assertIn("StringAssert.DoesNotContain", source)
        self.assertIn("Array.Clear", source)

        contract = (PROVIDERS / "ReachyProviderConfiguration.cs").read_text(encoding="utf-8")
        self.assertIn("interface IReachyProviderSecretStore", contract)
        self.assertIn("byte[] GetSecret(string reference);", contract)


if __name__ == "__main__":
    unittest.main()
