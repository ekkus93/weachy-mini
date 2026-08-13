package com.ekkus93.weachy.providers;

import android.app.KeyguardManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyPermanentlyInvalidatedException;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.InvalidKeyException;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.security.UnrecoverableKeyException;

import javax.crypto.AEADBadTagException;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Android Keystore-backed provider secret storage. No plaintext secret is persisted. */
public final class ReachyProviderSecretBridge {
    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String KEY_ALIAS = "com.ekkus93.weachy.provider-secrets.v1";
    private static final String PREFERENCES = "reachy-provider-secrets-v1";
    private static final String CIPHER = "AES/GCM/NoPadding";
    private static final int GCM_TAG_BITS = 128;
    private static final int GCM_TAG_BYTES = GCM_TAG_BITS / 8;
    private static final int IV_BYTES = 12;
    private static final int MAX_SECRET_BYTES = 16 * 1024;
    private static final Object LOCK = new Object();

    private ReachyProviderSecretBridge() {
    }

    public static void put(Context context, String reference, byte[] secretUtf8) throws Exception {
        validateContext(context);
        validateReference(reference);
        validateSecret(secretUtf8);

        synchronized (LOCK) {
            SharedPreferences stored = preferences(context);
            SecretKey key = getOrCreateKey(stored);
            byte[] iv = new byte[IV_BYTES];
            new SecureRandom().nextBytes(iv);
            byte[] ciphertext = null;
            try {
                Cipher cipher = createEncryptionCipher(stored, key, reference, iv);
                ciphertext = cipher.doFinal(secretUtf8);
                SharedPreferences.Editor editor = stored.edit()
                        .putString(ivKey(reference), Base64.encodeToString(iv, Base64.NO_WRAP))
                        .putString(
                                ciphertextKey(reference),
                                Base64.encodeToString(ciphertext, Base64.NO_WRAP));
                if (!editor.commit()) {
                    throw new IllegalStateException(
                            "Provider secret ciphertext could not be committed.");
                }
            } finally {
                clear(iv);
                clear(ciphertext);
            }
        }
    }

    public static byte[] get(Context context, String reference) throws Exception {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences stored = preferences(context);
            String ivText = stored.getString(ivKey(reference), null);
            String ciphertextText = stored.getString(ciphertextKey(reference), null);
            if (ivText == null && ciphertextText == null) {
                return null;
            }
            if (ivText == null || ciphertextText == null) {
                throw new IllegalStateException(
                        "Provider secret ciphertext metadata is incomplete.");
            }

            byte[] iv = null;
            byte[] ciphertext = null;
            try {
                iv = decodeBase64(ivText, "initialization vector");
                ciphertext = decodeBase64(ciphertextText, "ciphertext");
                if (iv.length != IV_BYTES ||
                        ciphertext.length <= GCM_TAG_BYTES ||
                        ciphertext.length > MAX_SECRET_BYTES + GCM_TAG_BYTES) {
                    throw new IllegalStateException(
                            "Provider secret ciphertext metadata is invalid.");
                }

                SecretKey key = requireExistingKey(stored);
                Cipher cipher = Cipher.getInstance(CIPHER);
                try {
                    cipher.init(
                            Cipher.DECRYPT_MODE,
                            key,
                            new GCMParameterSpec(GCM_TAG_BITS, iv));
                } catch (KeyPermanentlyInvalidatedException exception) {
                    throw keyUnavailable(exception);
                } catch (InvalidKeyException exception) {
                    throw keyUnavailable(exception);
                }
                cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8));
                try {
                    return cipher.doFinal(ciphertext);
                } catch (AEADBadTagException exception) {
                    throw new IllegalStateException(
                            "Provider secret ciphertext authentication failed.",
                            exception);
                }
            } finally {
                clear(iv);
                clear(ciphertext);
            }
        }
    }

    public static boolean contains(Context context, String reference) {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences stored = preferences(context);
            boolean hasIv = stored.contains(ivKey(reference));
            boolean hasCiphertext = stored.contains(ciphertextKey(reference));
            if (hasIv != hasCiphertext) {
                throw new IllegalStateException(
                        "Provider secret ciphertext metadata is incomplete.");
            }
            return hasIv;
        }
    }

    public static boolean delete(Context context, String reference) throws Exception {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences stored = preferences(context);
            boolean existed = stored.contains(ivKey(reference)) ||
                    stored.contains(ciphertextKey(reference));
            if (!stored.edit()
                    .remove(ivKey(reference))
                    .remove(ciphertextKey(reference))
                    .commit()) {
                throw new IllegalStateException(
                        "Provider secret ciphertext could not be deleted.");
            }
            if (!hasStoredSecretRecords(stored)) {
                deleteKeyIfPresent();
            }
            return existed;
        }
    }

    public static boolean isKeyguardLocked(Context context) {
        validateContext(context);
        KeyguardManager keyguard =
                (KeyguardManager) context.getSystemService(Context.KEYGUARD_SERVICE);
        if (keyguard == null) {
            throw new IllegalStateException("Android keyguard service is unavailable.");
        }
        return keyguard.isKeyguardLocked();
    }

    public static boolean isDeviceSecure(Context context) {
        validateContext(context);
        KeyguardManager keyguard =
                (KeyguardManager) context.getSystemService(Context.KEYGUARD_SERVICE);
        if (keyguard == null) {
            throw new IllegalStateException("Android keyguard service is unavailable.");
        }
        return keyguard.isDeviceSecure();
    }

    public static boolean hasEncryptionKey(Context context) throws Exception {
        validateContext(context);
        synchronized (LOCK) {
            KeyStore keyStore = loadKeyStore();
            return keyStore.containsAlias(KEY_ALIAS);
        }
    }

    public static boolean invalidateEncryptionKeyForTesting(Context context) throws Exception {
        validateContext(context);
        requireDebuggableApplication(context);
        synchronized (LOCK) {
            KeyStore keyStore = loadKeyStore();
            boolean existed = keyStore.containsAlias(KEY_ALIAS);
            if (existed) {
                keyStore.deleteEntry(KEY_ALIAS);
            }
            return existed;
        }
    }

    private static Cipher createEncryptionCipher(
            SharedPreferences stored,
            SecretKey key,
            String reference,
            byte[] iv) throws Exception {
        Cipher cipher = Cipher.getInstance(CIPHER);
        try {
            cipher.init(
                    Cipher.ENCRYPT_MODE,
                    key,
                    new GCMParameterSpec(GCM_TAG_BITS, iv));
        } catch (KeyPermanentlyInvalidatedException exception) {
            return retryEncryptionWithReplacementKey(
                    stored,
                    reference,
                    iv,
                    exception);
        } catch (InvalidKeyException exception) {
            return retryEncryptionWithReplacementKey(
                    stored,
                    reference,
                    iv,
                    exception);
        }
        cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8));
        return cipher;
    }

    private static Cipher retryEncryptionWithReplacementKey(
            SharedPreferences stored,
            String reference,
            byte[] iv,
            InvalidKeyException invalidKeyException) throws Exception {
        if (hasStoredSecretRecords(stored)) {
            throw keyUnavailable(invalidKeyException);
        }
        deleteKeyIfPresent();
        SecretKey replacement = getOrCreateKey(stored);
        Cipher cipher = Cipher.getInstance(CIPHER);
        try {
            cipher.init(
                    Cipher.ENCRYPT_MODE,
                    replacement,
                    new GCMParameterSpec(GCM_TAG_BITS, iv));
        } catch (InvalidKeyException exception) {
            throw keyUnavailable(exception);
        }
        cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8));
        return cipher;
    }

    private static SecretKey getOrCreateKey(SharedPreferences stored) throws Exception {
        KeyStore keyStore = loadKeyStore();
        if (keyStore.containsAlias(KEY_ALIAS)) {
            try {
                SecretKey existing = (SecretKey) keyStore.getKey(KEY_ALIAS, null);
                if (existing == null) {
                    throw new IllegalStateException(
                            "Provider secret encryption key could not be loaded.");
                }
                return existing;
            } catch (UnrecoverableKeyException exception) {
                if (hasStoredSecretRecords(stored)) {
                    throw keyUnavailable(exception);
                }
                keyStore.deleteEntry(KEY_ALIAS);
            }
        } else if (hasStoredSecretRecords(stored)) {
            throw keyUnavailable(null);
        }

        KeyGenerator generator = KeyGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_AES,
                KEYSTORE);
        KeyGenParameterSpec specification = new KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .setUserAuthenticationRequired(false)
                .build();
        generator.init(specification);
        return generator.generateKey();
    }

    private static SecretKey requireExistingKey(SharedPreferences stored) throws Exception {
        KeyStore keyStore = loadKeyStore();
        if (!keyStore.containsAlias(KEY_ALIAS)) {
            throw keyUnavailable(null);
        }
        try {
            SecretKey key = (SecretKey) keyStore.getKey(KEY_ALIAS, null);
            if (key == null) {
                throw keyUnavailable(null);
            }
            return key;
        } catch (UnrecoverableKeyException exception) {
            if (hasStoredSecretRecords(stored)) {
                throw keyUnavailable(exception);
            }
            throw new IllegalStateException(
                    "Provider secret encryption key could not be loaded.",
                    exception);
        }
    }

    private static KeyStore loadKeyStore() throws Exception {
        KeyStore keyStore = KeyStore.getInstance(KEYSTORE);
        keyStore.load(null);
        return keyStore;
    }

    private static void deleteKeyIfPresent() throws Exception {
        KeyStore keyStore = loadKeyStore();
        if (keyStore.containsAlias(KEY_ALIAS)) {
            keyStore.deleteEntry(KEY_ALIAS);
        }
    }

    private static boolean hasStoredSecretRecords(SharedPreferences stored) {
        return !stored.getAll().isEmpty();
    }

    private static SharedPreferences preferences(Context context) {
        return context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    private static String ivKey(String reference) {
        return reference + ".iv";
    }

    private static String ciphertextKey(String reference) {
        return reference + ".ciphertext";
    }

    private static byte[] decodeBase64(String value, String description) {
        try {
            return Base64.decode(value, Base64.NO_WRAP);
        } catch (IllegalArgumentException exception) {
            throw new IllegalStateException(
                    "Provider secret " + description + " is not valid base64.",
                    exception);
        }
    }

    private static IllegalStateException keyUnavailable(Exception cause) {
        return new IllegalStateException(
                "RMA161_KEY_UNAVAILABLE: provider credential encryption key is unavailable; " +
                        "encrypted credential records require explicit deletion before replacement.",
                cause);
    }

    private static void requireDebuggableApplication(Context context) {
        if ((context.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) {
            throw new SecurityException(
                    "Provider credential test hooks require a debuggable application build.");
        }
    }

    private static void validateContext(Context context) {
        if (context == null) {
            throw new IllegalArgumentException(
                    "Android context is required for provider secret storage.");
        }
    }

    private static void validateReference(String reference) {
        if (reference == null || reference.length() == 0 || reference.length() > 96) {
            throw new IllegalArgumentException("Provider secret reference is invalid.");
        }
        for (int index = 0; index < reference.length(); ++index) {
            char character = reference.charAt(index);
            boolean valid = character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' || character == '_' || character == '-';
            if (!valid) {
                throw new IllegalArgumentException("Provider secret reference is invalid.");
            }
        }
    }

    private static void validateSecret(byte[] secretUtf8) {
        if (secretUtf8 == null ||
                secretUtf8.length == 0 ||
                secretUtf8.length > MAX_SECRET_BYTES) {
            throw new IllegalArgumentException(
                    "Provider secret bytes are empty or exceed the allowed size.");
        }
    }

    private static void clear(byte[] value) {
        if (value == null) {
            return;
        }
        for (int index = 0; index < value.length; ++index) {
            value[index] = 0;
        }
    }
}
