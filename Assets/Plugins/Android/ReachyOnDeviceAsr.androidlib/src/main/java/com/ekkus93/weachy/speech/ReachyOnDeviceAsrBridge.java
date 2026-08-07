package com.ekkus93.weachy.speech;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.speech.RecognitionListener;
import android.speech.RecognitionSupport;
import android.speech.RecognitionSupportCallback;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;

import androidx.annotation.RequiresApi;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

public final class ReachyOnDeviceAsrBridge {
    private static final long CLOSE_TIMEOUT_SECONDS = 5L;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Object lock = new Object();
    private Session activeSession;
    private SpeechRecognizer supportRecognizer;
    private String supportRequestId;

    public interface Callback {
        void onProbe(
                String requestId,
                int apiLevel,
                boolean hasMicrophonePermission,
                boolean explicitOnDeviceRecognitionAvailable,
                boolean recognitionSupportCheckAvailable);
        void onSupportResult(String requestId, String state, String diagnostic);
        void onStarted(String requestId);
        void onPartialResult(String requestId, String transcript);
        void onFinalResult(String requestId, String transcript);
        void onNoMatch(String requestId);
        void onCancelled(String requestId);
        void onFailure(String requestId, String code, String diagnostic);
    }

    public void probe(Activity activity, String requestId, Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireCallback(callback);
        mainHandler.post(() -> callback.onProbe(
                requestId,
                Build.VERSION.SDK_INT,
                hasMicrophonePermission(activity),
                isExplicitOnDeviceAvailable(activity),
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU));
    }

    public void checkSupport(
            Activity activity,
            String requestId,
            String languageTag,
            Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireText(languageTag, "languageTag");
        requireCallback(callback);
        mainHandler.post(() -> checkSupportOnMain(
                activity,
                requestId,
                languageTag,
                callback));
    }

    public void start(
            Activity activity,
            String requestId,
            String languageTag,
            boolean partialResults,
            Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireText(languageTag, "languageTag");
        requireCallback(callback);
        mainHandler.post(() -> startOnMain(
                activity,
                requestId,
                languageTag,
                partialResults,
                callback));
    }

    public void cancel(String requestId) {
        requireText(requestId, "requestId");
        mainHandler.post(() -> cancelOnMain(requestId));
    }

    public void close() {
        runOnMainThreadAndWait(this::closeOnMain);
    }

    private void checkSupportOnMain(
            Activity activity,
            String requestId,
            String languageTag,
            Callback callback) {
        if (!hasMicrophonePermission(activity)) {
            callback.onSupportResult(
                    requestId,
                    "faulted",
                    "Microphone permission must be granted before explicit on-device recognition support is queried.");
            return;
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            callback.onSupportResult(
                    requestId,
                    "preflight_unavailable",
                    "This Android version does not expose per-language recognition support preflight.");
            return;
        }
        if (!isExplicitOnDeviceAvailable(activity)) {
            callback.onSupportResult(
                    requestId,
                    "faulted",
                    "Android reports no explicit on-device recognition service.");
            return;
        }

        synchronized (lock) {
            if (supportRecognizer != null || activeSession != null) {
                callback.onSupportResult(
                        requestId,
                        "faulted",
                        "The explicit on-device recognition service is busy.");
                return;
            }
            try {
                supportRecognizer = Api31.createOnDeviceRecognizer(activity);
                supportRequestId = requestId;
                Intent intent = recognitionIntent(languageTag, false);
                Api33.checkRecognitionSupport(
                        supportRecognizer,
                        intent,
                        activity,
                        new SupportCallback(requestId, languageTag, callback));
            } catch (RuntimeException exception) {
                destroySupportRecognizerLocked();
                callback.onSupportResult(
                        requestId,
                        "faulted",
                        "Creating explicit on-device recognition support failed with "
                                + exception.getClass().getSimpleName() + ".");
            }
        }
    }

    private void startOnMain(
            Activity activity,
            String requestId,
            String languageTag,
            boolean partialResults,
            Callback callback) {
        if (!hasMicrophonePermission(activity)) {
            callback.onFailure(
                    requestId,
                    "permission_denied",
                    "Microphone permission is required before creating an explicit on-device recognizer.");
            return;
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S
                || !isExplicitOnDeviceAvailable(activity)) {
            callback.onFailure(
                    requestId,
                    "service_failure",
                    "Android reports no explicit on-device SpeechRecognizer.");
            return;
        }

        synchronized (lock) {
            if (activeSession != null || supportRecognizer != null) {
                callback.onFailure(
                        requestId,
                        "recognizer_busy",
                        "The explicit on-device SpeechRecognizer is busy; the request was not queued.");
                return;
            }
            try {
                SpeechRecognizer recognizer = Api31.createOnDeviceRecognizer(activity);
                Session session = new Session(requestId, recognizer, callback);
                activeSession = session;
                recognizer.setRecognitionListener(session.listener);
                recognizer.startListening(recognitionIntent(languageTag, partialResults));
            } catch (RuntimeException exception) {
                Session failed = activeSession;
                activeSession = null;
                if (failed != null) {
                    failed.recognizer.destroy();
                }
                callback.onFailure(
                        requestId,
                        "service_failure",
                        "Starting explicit on-device recognition failed with "
                                + exception.getClass().getSimpleName() + ".");
            }
        }
    }

    private void cancelOnMain(String requestId) {
        synchronized (lock) {
            if (requestId.equals(supportRequestId)) {
                destroySupportRecognizerLocked();
                return;
            }
            Session session = activeSession;
            if (session == null || !requestId.equals(session.requestId)) {
                return;
            }
            activeSession = null;
            session.terminal = true;
            try {
                session.recognizer.cancel();
            } finally {
                session.recognizer.destroy();
            }
            session.callback.onCancelled(requestId);
        }
    }

    private void closeOnMain() {
        synchronized (lock) {
            destroySupportRecognizerLocked();
            Session session = activeSession;
            activeSession = null;
            if (session != null) {
                session.terminal = true;
                try {
                    session.recognizer.cancel();
                } finally {
                    session.recognizer.destroy();
                }
                session.callback.onCancelled(session.requestId);
            }
        }
    }

    private void finishSession(Session session) {
        synchronized (lock) {
            if (activeSession != session || session.terminal) {
                return;
            }
            session.terminal = true;
            activeSession = null;
            session.recognizer.destroy();
        }
    }

    private void failSession(Session session, int error) {
        if (!isCurrent(session)) {
            return;
        }
        String code = errorCode(error);
        String diagnostic = errorDiagnostic(error);
        finishSession(session);
        if (SpeechRecognizer.ERROR_NO_MATCH == error) {
            session.callback.onNoMatch(session.requestId);
        } else {
            session.callback.onFailure(session.requestId, code, diagnostic);
        }
    }

    private boolean isCurrent(Session session) {
        synchronized (lock) {
            return activeSession == session && !session.terminal;
        }
    }

    private void destroySupportRecognizerLocked() {
        if (supportRecognizer != null) {
            supportRecognizer.destroy();
        }
        supportRecognizer = null;
        supportRequestId = null;
    }

    private static Intent recognitionIntent(
            String languageTag,
            boolean partialResults) {
        Intent intent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        intent.putExtra(
                RecognizerIntent.EXTRA_LANGUAGE_MODEL,
                RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, languageTag);
        intent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, partialResults);
        intent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1);
        return intent;
    }

    private static String firstTranscript(Bundle results) {
        ArrayList<String> values =
                results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
        if (values == null || values.isEmpty()) {
            return null;
        }
        String value = values.get(0);
        return value == null || value.trim().isEmpty() ? null : value;
    }

    private static boolean hasMicrophonePermission(Activity activity) {
        return activity.checkSelfPermission(Manifest.permission.RECORD_AUDIO)
                == PackageManager.PERMISSION_GRANTED;
    }

    private static boolean isExplicitOnDeviceAvailable(Context context) {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
                && Api31.isOnDeviceAvailable(context);
    }

    private static String errorCode(int error) {
        switch (error) {
            case SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS:
                return "permission_denied";
            case SpeechRecognizer.ERROR_AUDIO:
                return "audio_failure";
            case SpeechRecognizer.ERROR_SPEECH_TIMEOUT:
                return "speech_timeout";
            case SpeechRecognizer.ERROR_RECOGNIZER_BUSY:
                return "recognizer_busy";
            case SpeechRecognizer.ERROR_TOO_MANY_REQUESTS:
                return "too_many_requests";
            case SpeechRecognizer.ERROR_SERVER_DISCONNECTED:
                return "service_disconnected";
            case SpeechRecognizer.ERROR_LANGUAGE_NOT_SUPPORTED:
                return "language_not_supported";
            case SpeechRecognizer.ERROR_LANGUAGE_UNAVAILABLE:
                return "language_model_unavailable";
            case SpeechRecognizer.ERROR_NETWORK:
            case SpeechRecognizer.ERROR_NETWORK_TIMEOUT:
                return "unexpected_network_error";
            case SpeechRecognizer.ERROR_CLIENT:
                return "client_failure";
            case SpeechRecognizer.ERROR_SERVER:
                return "service_failure";
            default:
                return "unknown_android_asr_error";
        }
    }

    private static String errorDiagnostic(int error) {
        if (error == SpeechRecognizer.ERROR_NETWORK
                || error == SpeechRecognizer.ERROR_NETWORK_TIMEOUT) {
            return "The explicit on-device SpeechRecognizer reported a network error. RMA-121 treats this as a locality contract violation and does not fall back.";
        }
        return "Explicit Android on-device SpeechRecognizer failed with error code " + error + ".";
    }

    private static boolean containsLanguage(List<String> languages, String languageTag) {
        for (String candidate : languages) {
            if (candidate != null
                    && candidate.toLowerCase(Locale.ROOT)
                            .equals(languageTag.toLowerCase(Locale.ROOT))) {
                return true;
            }
        }
        return false;
    }

    private void runOnMainThreadAndWait(Runnable action) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            action.run();
            return;
        }
        CountDownLatch done = new CountDownLatch(1);
        AtomicReference<RuntimeException> failure = new AtomicReference<>();
        mainHandler.post(() -> {
            try {
                action.run();
            } catch (RuntimeException exception) {
                failure.set(exception);
            } finally {
                done.countDown();
            }
        });
        try {
            if (!done.await(CLOSE_TIMEOUT_SECONDS, TimeUnit.SECONDS)) {
                throw new IllegalStateException(
                        "Timed out destroying explicit Android on-device ASR resources.");
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException(
                    "Interrupted while destroying explicit Android on-device ASR resources.",
                    exception);
        }
        RuntimeException exception = failure.get();
        if (exception != null) {
            throw exception;
        }
    }

    private final class Session {
        private final String requestId;
        private final SpeechRecognizer recognizer;
        private final Callback callback;
        private final RecognitionListener listener;
        private boolean terminal;

        private Session(
                String requestId,
                SpeechRecognizer recognizer,
                Callback callback) {
            this.requestId = requestId;
            this.recognizer = recognizer;
            this.callback = callback;
            this.listener = new RecognitionListener() {
                @Override
                public void onReadyForSpeech(Bundle params) {
                    if (isCurrent(Session.this)) {
                        callback.onStarted(requestId);
                    }
                }

                @Override
                public void onBeginningOfSpeech() {
                }

                @Override
                public void onRmsChanged(float rmsdB) {
                }

                @Override
                public void onBufferReceived(byte[] buffer) {
                }

                @Override
                public void onEndOfSpeech() {
                }

                @Override
                public void onError(int error) {
                    failSession(Session.this, error);
                }

                @Override
                public void onResults(Bundle results) {
                    if (!isCurrent(Session.this)) {
                        return;
                    }
                    String transcript = firstTranscript(results);
                    finishSession(Session.this);
                    if (transcript == null) {
                        callback.onNoMatch(requestId);
                    } else {
                        callback.onFinalResult(requestId, transcript);
                    }
                }

                @Override
                public void onPartialResults(Bundle partialResults) {
                    if (!isCurrent(Session.this)) {
                        return;
                    }
                    String transcript = firstTranscript(partialResults);
                    if (transcript != null) {
                        callback.onPartialResult(requestId, transcript);
                    }
                }

                @Override
                public void onEvent(int eventType, Bundle params) {
                }
            };
        }
    }

    @RequiresApi(Build.VERSION_CODES.TIRAMISU)
    private final class SupportCallback implements RecognitionSupportCallback {
        private final String requestId;
        private final String languageTag;
        private final Callback callback;

        private SupportCallback(
                String requestId,
                String languageTag,
                Callback callback) {
            this.requestId = requestId;
            this.languageTag = languageTag;
            this.callback = callback;
        }

        @Override
        public void onSupportResult(RecognitionSupport support) {
            synchronized (lock) {
                if (!requestId.equals(supportRequestId)) {
                    return;
                }
                String state;
                String diagnostic;
                if (containsLanguage(
                        support.getInstalledOnDeviceLanguages(), languageTag)) {
                    state = "installed";
                    diagnostic = "The selected language is installed for explicit on-device recognition.";
                } else if (containsLanguage(
                        support.getPendingOnDeviceLanguages(), languageTag)) {
                    state = "model_download_pending";
                    diagnostic = "The selected on-device language model is pending installation.";
                } else if (containsLanguage(
                        support.getSupportedOnDeviceLanguages(), languageTag)) {
                    state = "model_download_required";
                    diagnostic = "The selected language is supported on device but its model is not installed.";
                } else if (containsLanguage(
                        support.getOnlineLanguages(), languageTag)) {
                    state = "unsupported_language";
                    diagnostic = "The selected language is reported only as online-capable; the explicit on-device provider refuses it.";
                } else {
                    state = "unsupported_language";
                    diagnostic = "The explicit on-device recognition service does not report support for the selected language.";
                }
                destroySupportRecognizerLocked();
                callback.onSupportResult(requestId, state, diagnostic);
            }
        }

        @Override
        public void onError(int error) {
            synchronized (lock) {
                if (!requestId.equals(supportRequestId)) {
                    return;
                }
                destroySupportRecognizerLocked();
                if (error == SpeechRecognizer.ERROR_CANNOT_CHECK_SUPPORT) {
                    callback.onSupportResult(
                            requestId,
                            "preflight_unavailable",
                            "The explicit on-device recognition service cannot preflight this language. Runtime failures remain authoritative.");
                } else {
                    callback.onSupportResult(
                            requestId,
                            "faulted",
                            "Explicit on-device recognition support preflight failed with error code "
                                    + error + ".");
                }
            }
        }
    }

    @RequiresApi(Build.VERSION_CODES.S)
    private static final class Api31 {
        private Api31() {
        }

        private static boolean isOnDeviceAvailable(Context context) {
            return SpeechRecognizer.isOnDeviceRecognitionAvailable(context);
        }

        private static SpeechRecognizer createOnDeviceRecognizer(Context context) {
            return SpeechRecognizer.createOnDeviceSpeechRecognizer(context);
        }
    }

    @RequiresApi(Build.VERSION_CODES.TIRAMISU)
    private static final class Api33 {
        private Api33() {
        }

        private static void checkRecognitionSupport(
                SpeechRecognizer recognizer,
                Intent intent,
                Context context,
                RecognitionSupportCallback callback) {
            recognizer.checkRecognitionSupport(intent, context.getMainExecutor(), callback);
        }
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("The Unity activity is required.");
        }
    }

    private static void requireCallback(Callback callback) {
        if (callback == null) {
            throw new IllegalArgumentException("The ASR callback is required.");
        }
    }

    private static void requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required.");
        }
    }
}
