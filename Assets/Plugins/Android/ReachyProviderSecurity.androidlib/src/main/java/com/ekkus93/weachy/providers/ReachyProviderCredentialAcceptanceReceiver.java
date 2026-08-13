package com.ekkus93.weachy.providers;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;

import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/**
 * ADB-only, debug-build acceptance receiver for the Android Keystore credential boundary.
 *
 * <p>The manifest requires android.permission.DUMP, which ordinary applications do not hold.
 * The receiver also rejects non-debuggable builds before touching credential state.</p>
 */
public final class ReachyProviderCredentialAcceptanceReceiver extends BroadcastReceiver {
    public static final String ACTION =
            "com.ekkus93.weachy.providers.RMA161_CREDENTIAL_ACCEPTANCE";
    public static final String EXTRA_PHASE = "phase";
    public static final String RESULT_FILE_NAME = "rma161-credential-state.json";

    private static final String PRIMARY_REFERENCE = "rma161.primary";
    private static final String APP_CLEAR_REFERENCE = "rma161.app-clear";
    private static final String PROVIDER_DELETE_REFERENCE = "rma161.provider-delete";
    private static final String KEY_UNAVAILABLE = "RMA161_KEY_UNAVAILABLE";

    private static final byte[] INITIAL_CREDENTIAL =
            "rma161-physical-secret-initial-7f20b3".getBytes(StandardCharsets.UTF_8);
    private static final byte[] UPDATED_CREDENTIAL =
            "rma161-physical-secret-updated-9d51c4".getBytes(StandardCharsets.UTF_8);
    private static final byte[] REPLACEMENT_CREDENTIAL =
            "rma161-physical-secret-replacement-a31e75".getBytes(StandardCharsets.UTF_8);
    private static final byte[] APP_CLEAR_CREDENTIAL =
            "rma161-physical-secret-app-clear-e4d907".getBytes(StandardCharsets.UTF_8);

    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null || intent == null || !ACTION.equals(intent.getAction())) {
            return;
        }

        String phase = intent.getStringExtra(EXTRA_PHASE);
        Report report;
        try {
            requireDebuggableApplication(context);
            report = runPhase(context, phase);
        } catch (Exception exception) {
            report = Report.failure(phase, exception.getClass().getSimpleName());
        }

        try {
            writeReport(context, report);
        } catch (Exception exception) {
            throw new IllegalStateException(
                    "RMA-161 acceptance report could not be written.",
                    exception);
        }
    }

    private static Report runPhase(Context context, String phase) throws Exception {
        if ("prepare".equals(phase)) {
            return runPrepare(context);
        }
        if ("verify-after-lock".equals(phase)) {
            return runVerifyAfterLock(context);
        }
        if ("invalidate".equals(phase)) {
            return runInvalidation(context);
        }
        if ("verify-cleared".equals(phase)) {
            return runVerifyCleared(context);
        }
        throw new IllegalArgumentException("Unsupported RMA-161 acceptance phase.");
    }

    private static Report runPrepare(Context context) throws Exception {
        cleanupReference(context, PRIMARY_REFERENCE);
        cleanupReference(context, PROVIDER_DELETE_REFERENCE);
        cleanupReference(context, APP_CLEAR_REFERENCE);

        putCopy(context, PRIMARY_REFERENCE, INITIAL_CREDENTIAL);
        requireCredential(context, PRIMARY_REFERENCE, INITIAL_CREDENTIAL);
        putCopy(context, PRIMARY_REFERENCE, UPDATED_CREDENTIAL);
        requireCredential(context, PRIMARY_REFERENCE, UPDATED_CREDENTIAL);

        if (!ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE)
                || !ReachyProviderSecretBridge.hasEncryptionKey(context)) {
            throw new IllegalStateException(
                    "RMA-161 prepare did not retain the encrypted credential and key.");
        }

        Report report = Report.success("prepare");
        report.credentialRoundTrip = true;
        report.keyPresent = true;
        return report;
    }

    private static Report runVerifyAfterLock(Context context) throws Exception {
        if (!ReachyProviderSecretBridge.isDeviceSecure(context)
                || !ReachyProviderSecretBridge.isKeyguardLocked(context)) {
            throw new IllegalStateException(
                    "RMA-161 locked verification requires an active secure keyguard.");
        }
        requireCredential(context, PRIMARY_REFERENCE, UPDATED_CREDENTIAL);

        Report report = Report.success("verify-after-lock");
        report.credentialRoundTrip = true;
        report.lockTransitionVerified = true;
        return report;
    }

    private static Report runInvalidation(Context context) throws Exception {
        if (!ReachyProviderSecretBridge.isKeyguardLocked(context)) {
            throw new IllegalStateException(
                    "RMA-161 invalidation requires the keyguard to remain active.");
        }
        if (!ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE)
                || !ReachyProviderSecretBridge.hasEncryptionKey(context)) {
            throw new IllegalStateException(
                    "RMA-161 invalidation requires the prepared credential and key.");
        }
        if (!ReachyProviderSecretBridge.invalidateEncryptionKeyForTesting(context)) {
            throw new IllegalStateException(
                    "RMA-161 test invalidation did not remove the prepared Keystore key.");
        }

        boolean readFailedClosed = expectKeyUnavailable(
                () -> ReachyProviderSecretBridge.get(context, PRIMARY_REFERENCE));
        boolean updateFailedClosed = expectKeyUnavailable(
                () -> putCopy(context, PRIMARY_REFERENCE, REPLACEMENT_CREDENTIAL));
        boolean retainedEncryptedRecord =
                ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE);
        boolean explicitDeleteSucceeded =
                ReachyProviderSecretBridge.delete(context, PRIMARY_REFERENCE);

        if (ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE)
                || ReachyProviderSecretBridge.hasEncryptionKey(context)) {
            throw new IllegalStateException(
                    "RMA-161 explicit deletion did not clear invalidated state.");
        }

        putCopy(context, PRIMARY_REFERENCE, REPLACEMENT_CREDENTIAL);
        requireCredential(context, PRIMARY_REFERENCE, REPLACEMENT_CREDENTIAL);
        boolean replacementKeyCreated = ReachyProviderSecretBridge.hasEncryptionKey(context);

        putCopy(context, APP_CLEAR_REFERENCE, APP_CLEAR_CREDENTIAL);
        requireCredential(context, APP_CLEAR_REFERENCE, APP_CLEAR_CREDENTIAL);
        ReachyProviderSecretBridge.delete(context, PRIMARY_REFERENCE);

        if (!readFailedClosed
                || !updateFailedClosed
                || !retainedEncryptedRecord
                || !explicitDeleteSucceeded
                || !replacementKeyCreated
                || !ReachyProviderSecretBridge.contains(context, APP_CLEAR_REFERENCE)) {
            throw new IllegalStateException(
                    "RMA-161 invalidation did not satisfy every required invariant.");
        }

        Report report = Report.success("invalidate");
        report.invalidationTriggered = true;
        report.readFailedClosedAfterInvalidation = true;
        report.updateFailedClosedAfterInvalidation = true;
        report.encryptedRecordRetainedAfterInvalidation = true;
        report.explicitDeleteSucceeded = true;
        report.replacementKeyCreated = true;
        report.appClearCredentialPrepared = true;
        return report;
    }

    private static Report runVerifyCleared(Context context) throws Exception {
        boolean appDataClearRemovedCredential =
                !ReachyProviderSecretBridge.contains(context, APP_CLEAR_REFERENCE)
                        && !ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE)
                        && !ReachyProviderSecretBridge.contains(context, PROVIDER_DELETE_REFERENCE);
        if (!appDataClearRemovedCredential) {
            throw new IllegalStateException(
                    "RMA-161 app-data clear left a provider credential record configured.");
        }

        putCopy(context, PRIMARY_REFERENCE, REPLACEMENT_CREDENTIAL);
        requireCredential(context, PRIMARY_REFERENCE, REPLACEMENT_CREDENTIAL);
        boolean postClearCreateReadSucceeded =
                ReachyProviderSecretBridge.delete(context, PRIMARY_REFERENCE);
        if (!postClearCreateReadSucceeded
                || ReachyProviderSecretBridge.contains(context, PRIMARY_REFERENCE)) {
            throw new IllegalStateException(
                    "RMA-161 credential storage was not usable after app-data clear.");
        }

        Report report = Report.success("verify-cleared");
        report.appDataClearRemovedCredential = true;
        report.postClearCreateReadSucceeded = true;
        return report;
    }

    private static void cleanupReference(Context context, String reference) throws Exception {
        ReachyProviderSecretBridge.delete(context, reference);
    }

    private static void putCopy(Context context, String reference, byte[] source) throws Exception {
        byte[] value = Arrays.copyOf(source, source.length);
        try {
            ReachyProviderSecretBridge.put(context, reference, value);
        } finally {
            clear(value);
        }
    }

    private static void requireCredential(Context context, String reference, byte[] expected)
            throws Exception {
        byte[] actual = ReachyProviderSecretBridge.get(context, reference);
        if (actual == null) {
            throw new IllegalStateException("RMA-161 credential was not configured.");
        }
        try {
            if (!constantTimeEquals(actual, expected)) {
                throw new IllegalStateException(
                        "RMA-161 credential bytes changed during the lifecycle operation.");
            }
        } finally {
            clear(actual);
        }
    }

    private static boolean expectKeyUnavailable(ThrowingOperation operation) {
        try {
            byte[] result = operation.run();
            clear(result);
            return false;
        } catch (Exception exception) {
            return exception.toString().contains(KEY_UNAVAILABLE);
        }
    }

    private static boolean constantTimeEquals(byte[] left, byte[] right) {
        if (left.length != right.length) {
            return false;
        }
        int difference = 0;
        for (int index = 0; index < left.length; ++index) {
            difference |= left[index] ^ right[index];
        }
        return difference == 0;
    }

    private static void clear(byte[] value) {
        if (value != null) {
            Arrays.fill(value, (byte) 0);
        }
    }

    private static void requireDebuggableApplication(Context context) {
        if ((context.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) {
            throw new SecurityException(
                    "RMA-161 credential acceptance is available only in debuggable builds.");
        }
    }

    private static void writeReport(Context context, Report report) throws Exception {
        File root = context.getExternalFilesDir(null);
        if (root == null) {
            throw new IllegalStateException("Android external app-files directory is unavailable.");
        }
        if (!root.exists() && !root.mkdirs()) {
            throw new IllegalStateException("RMA-161 report directory could not be created.");
        }

        File result = new File(root, RESULT_FILE_NAME);
        File temporary = new File(root, RESULT_FILE_NAME + ".tmp");
        byte[] json = report.toJson().toString().getBytes(StandardCharsets.UTF_8);
        try (FileOutputStream output = new FileOutputStream(temporary, false)) {
            output.write(json);
            output.getFD().sync();
        } finally {
            clear(json);
        }

        if (result.exists() && !result.delete()) {
            throw new IllegalStateException("Existing RMA-161 report could not be replaced.");
        }
        if (!temporary.renameTo(result)) {
            throw new IllegalStateException("RMA-161 report could not be committed atomically.");
        }
    }

    private interface ThrowingOperation {
        byte[] run() throws Exception;
    }

    private static final class Report {
        private final String status;
        private final String phase;
        private final String message;
        private boolean credentialRoundTrip;
        private boolean lockTransitionVerified;
        private boolean keyPresent;
        private boolean invalidationTriggered;
        private boolean readFailedClosedAfterInvalidation;
        private boolean updateFailedClosedAfterInvalidation;
        private boolean encryptedRecordRetainedAfterInvalidation;
        private boolean explicitDeleteSucceeded;
        private boolean replacementKeyCreated;
        private boolean appClearCredentialPrepared;
        private boolean appDataClearRemovedCredential;
        private boolean postClearCreateReadSucceeded;

        private Report(String status, String phase, String message) {
            this.status = status;
            this.phase = phase == null ? "" : phase;
            this.message = message;
        }

        private static Report success(String phase) {
            return new Report(
                    "passed",
                    phase,
                    "RMA-161 credential lifecycle acceptance phase passed.");
        }

        private static Report failure(String phase, String exceptionType) {
            return new Report(
                    "failed",
                    phase,
                    "RMA-161 credential lifecycle acceptance phase failed: " + exceptionType);
        }

        private JSONObject toJson() throws Exception {
            JSONObject json = new JSONObject();
            json.put("status", status);
            json.put("phase", phase);
            json.put("credential_round_trip", credentialRoundTrip);
            json.put("lock_transition_verified", lockTransitionVerified);
            json.put("key_present", keyPresent);
            json.put("invalidation_triggered", invalidationTriggered);
            json.put("read_failed_closed_after_invalidation", readFailedClosedAfterInvalidation);
            json.put("update_failed_closed_after_invalidation", updateFailedClosedAfterInvalidation);
            json.put(
                    "encrypted_record_retained_after_invalidation",
                    encryptedRecordRetainedAfterInvalidation);
            json.put("explicit_delete_succeeded", explicitDeleteSucceeded);
            json.put("replacement_key_created", replacementKeyCreated);
            json.put("provider_delete_removed_credential", false);
            json.put("app_clear_credential_prepared", appClearCredentialPrepared);
            json.put("app_data_clear_removed_credential", appDataClearRemovedCredential);
            json.put("post_clear_create_read_succeeded", postClearCreateReadSucceeded);
            json.put("full_secret_in_report", false);
            json.put("message", message);
            return json;
        }
    }
}
