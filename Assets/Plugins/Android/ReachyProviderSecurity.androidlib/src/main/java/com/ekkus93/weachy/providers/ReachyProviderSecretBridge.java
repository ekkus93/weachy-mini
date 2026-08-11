package com.ekkus93.weachy.providers;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;

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
    private static final int IV_BYTES = 12;
    private static final int MAX_SECRET_BYTES = 16 * 1024;
    private static final Object LOCK = new Object();

    private ReachyProviderSecretBridge() {
    }

    public static void put(Context context, String reference, byte[] secretUtf8) throws Exception {
        validateContext(context);
        validateReference(reference);
        if (secretUtf8 == null || secretUtf8.length == 0 || secretUtf8.length > MAX_SECRET_BYTES) {
            throw new IllegalArgumentException("Provider secret bytes are empty or exceed the allowed size.");
        }

        synchronized (LOCK) {
            SecretKey key = getOrCreateKey();
            byte[] iv = new byte[IV_BYTES];
            new SecureRandom().nextBytes(iv);
            Cipher cipher = Cipher.getInstance(CIPHER);
            cipher.init(Cipher.ENCRYPT_MODE, key, new GCMParameterSpec(GCM_TAG_BITS, iv));
            cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8));
            byte[] ciphertext = cipher.doFinal(secretUtf8);

            SharedPreferences.Editor editor = preferences(context).edit()
                    .putString(ivKey(reference), Base64.encodeToString(iv, Base64.NO_WRAP))
                    .putString(ciphertextKey(reference), Base64.encodeToString(ciphertext, Base64.NO_WRAP));
            if (!editor.commit()) {
                throw new IllegalStateException("Provider secret ciphertext could not be committed.");
            }
        }
    }

    public static byte[] get(Context context, String reference) throws Exception {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences preferences = preferences(context);
            String ivText = preferences.getString(ivKey(reference), null);
            String ciphertextText = preferences.getString(ciphertextKey(reference), null);
            if (ivText == null && ciphertextText == null) {
                return null;
            }
            if (ivText == null || ciphertextText == null) {
                throw new IllegalStateException("Provider secret ciphertext metadata is incomplete.");
            }

            byte[] iv = Base64.decode(ivText, Base64.NO_WRAP);
            byte[] ciphertext = Base64.decode(ciphertextText, Base64.NO_WRAP);
            if (iv.length != IV_BYTES || ciphertext.length == 0) {
                throw new IllegalStateException("Provider secret ciphertext metadata is invalid.");
            }

            KeyStore keyStore = KeyStore.getInstance(KEYSTORE);
            keyStore.load(null);
            if (!keyStore.containsAlias(KEY_ALIAS)) {
                throw new IllegalStateException("Provider secret encryption key is unavailable.");
            }
            SecretKey key = (SecretKey) keyStore.getKey(KEY_ALIAS, null);
            Cipher cipher = Cipher.getInstance(CIPHER);
            cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(GCM_TAG_BITS, iv));
            cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8));
            return cipher.doFinal(ciphertext);
        }
    }

    public static boolean contains(Context context, String reference) {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences preferences = preferences(context);
            boolean hasIv = preferences.contains(ivKey(reference));
            boolean hasCiphertext = preferences.contains(ciphertextKey(reference));
            if (hasIv != hasCiphertext) {
                throw new IllegalStateException("Provider secret ciphertext metadata is incomplete.");
            }
            return hasIv;
        }
    }

    public static boolean delete(Context context, String reference) {
        validateContext(context);
        validateReference(reference);
        synchronized (LOCK) {
            SharedPreferences preferences = preferences(context);
            boolean existed = preferences.contains(ivKey(reference)) ||
                    preferences.contains(ciphertextKey(reference));
            if (!preferences.edit()
                    .remove(ivKey(reference))
                    .remove(ciphertextKey(reference))
                    .commit()) {
                throw new IllegalStateException("Provider secret ciphertext could not be deleted.");
            }
            return existed;
        }
    }

    private static SecretKey getOrCreateKey() throws Exception {
        KeyStore keyStore = KeyStore.getInstance(KEYSTORE);
        keyStore.load(null);
        if (keyStore.containsAlias(KEY_ALIAS)) {
            return (SecretKey) keyStore.getKey(KEY_ALIAS, null);
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
                .build();
        generator.init(specification);
        return generator.generateKey();
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

    private static void validateContext(Context context) {
        if (context == null) {
            throw new IllegalArgumentException("Android context is required for provider secret storage.");
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
}
