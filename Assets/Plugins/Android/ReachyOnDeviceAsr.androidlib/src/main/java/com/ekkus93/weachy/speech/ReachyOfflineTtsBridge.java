package com.ekkus93.weachy.speech;

import android.app.Activity;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import android.speech.tts.Voice;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

public final class ReachyOfflineTtsBridge {
    private static final long CLOSE_TIMEOUT_SECONDS = 5L;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Object lock = new Object();
    private TextToSpeech engine;
    private boolean initializing;
    private boolean closed;
    private final List<PendingInitialization> pendingInitialization = new ArrayList<>();
    private Session activeSession;

    public interface Callback {
        void onProbe(
                String requestId,
                int apiLevel,
                boolean engineInitialized,
                String languageStatus,
                int matchingOfflineVoiceCount,
                int installedOfflineVoiceCount,
                int matchingNetworkVoiceCount,
                int maximumInputCharacters,
                String diagnostic);
        void onVoicesStarted(String requestId);
        void onVoice(
                String requestId,
                String voiceId,
                String displayName,
                String languageTag,
                String networkRequirement,
                boolean installed);
        void onVoicesCompleted(String requestId);
        void onStarted(String requestId);
        void onDone(String requestId);
        void onStopped(String requestId);
        void onFailure(String requestId, String code, String diagnostic);
    }

    public void probe(
            Activity activity,
            String requestId,
            String languageTag,
            Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireText(languageTag, "languageTag");
        requireCallback(callback);
        mainHandler.post(() -> withEngine(
                activity,
                requestId,
                callback,
                tts -> publishProbe(requestId, languageTag, callback, tts)));
    }

    public void listVoices(
            Activity activity,
            String requestId,
            String languageTag,
            Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireText(languageTag, "languageTag");
        requireCallback(callback);
        mainHandler.post(() -> withEngine(
                activity,
                requestId,
                callback,
                tts -> publishVoices(requestId, languageTag, callback, tts)));
    }

    public void start(
            Activity activity,
            String requestId,
            String text,
            String languageTag,
            String voiceId,
            Callback callback) {
        requireActivity(activity);
        requireText(requestId, "requestId");
        requireText(text, "text");
        requireText(languageTag, "languageTag");
        requireText(voiceId, "voiceId");
        requireCallback(callback);
        mainHandler.post(() -> withEngine(
                activity,
                requestId,
                callback,
                tts -> startOnMain(requestId, text, languageTag, voiceId, callback, tts)));
    }

    public void cancel(String requestId) {
        requireText(requestId, "requestId");
        mainHandler.post(() -> cancelOnMain(requestId));
    }

    public void close() {
        runOnMainThreadAndWait(this::closeOnMain);
    }

    private void withEngine(
            Activity activity,
            String requestId,
            Callback callback,
            EngineAction action) {
        TextToSpeech ready;
        synchronized (lock) {
            if (closed) {
                callback.onFailure(
                        requestId,
                        "engine_unavailable",
                        "Android offline TTS bridge is closed; no alternate TTS provider was selected.");
                return;
            }
            ready = engine;
            if (ready == null) {
                pendingInitialization.add(
                        new PendingInitialization(requestId, callback, action));
                if (initializing) {
                    return;
                }
                initializing = true;
            }
        }

        if (ready != null) {
            action.run(ready);
            return;
        }

        final TextToSpeech[] holder = new TextToSpeech[1];
        try {
            holder[0] = new TextToSpeech(
                    activity.getApplicationContext(),
                    status -> mainHandler.post(
                            () -> finishInitialization(holder[0], status)));
        } catch (RuntimeException exception) {
            failInitialization(
                    "engine_initialization_failed",
                    "Creating Android TextToSpeech failed with "
                            + exception.getClass().getSimpleName()
                            + "; no alternate TTS provider was selected.");
        }
    }

    private void finishInitialization(TextToSpeech created, int status) {
        List<PendingInitialization> pending;
        TextToSpeech ready = null;
        synchronized (lock) {
            if (!initializing) {
                if (created != null) {
                    created.shutdown();
                }
                return;
            }
            initializing = false;
            pending = new ArrayList<>(pendingInitialization);
            pendingInitialization.clear();

            if (!closed && status == TextToSpeech.SUCCESS && created != null) {
                engine = created;
                ready = created;
                installProgressListener(created);
            }
        }

        if (ready == null) {
            if (created != null) {
                created.shutdown();
            }
            for (PendingInitialization item : pending) {
                item.callback.onFailure(
                        item.requestId,
                        "engine_unavailable",
                        "Android TextToSpeech initialization failed; configure or install a TTS engine. No alternate TTS provider was selected.");
            }
            return;
        }

        for (PendingInitialization item : pending) {
            item.action.run(ready);
        }
    }

    private void failInitialization(String code, String diagnostic) {
        List<PendingInitialization> pending;
        synchronized (lock) {
            initializing = false;
            pending = new ArrayList<>(pendingInitialization);
            pendingInitialization.clear();
        }
        for (PendingInitialization item : pending) {
            item.callback.onFailure(item.requestId, code, diagnostic);
        }
    }

    private void publishProbe(
            String requestId,
            String languageTag,
            Callback callback,
            TextToSpeech tts) {
        Locale locale = Locale.forLanguageTag(languageTag);
        int status = tts.isLanguageAvailable(locale);
        VoiceCounts counts = countVoices(tts, languageTag);
        callback.onProbe(
                requestId,
                Build.VERSION.SDK_INT,
                true,
                languageStatus(status),
                counts.matchingOffline,
                counts.installedOffline,
                counts.matchingNetwork,
                TextToSpeech.getMaxSpeechInputLength(),
                "Android TextToSpeech initialized; only exact-locale voices declaring no network requirement are eligible for RMA-123.");
    }

    private void publishVoices(
            String requestId,
            String languageTag,
            Callback callback,
            TextToSpeech tts) {
        callback.onVoicesStarted(requestId);
        List<Voice> voices = matchingVoices(tts, languageTag);
        for (Voice voice : voices) {
            callback.onVoice(
                    requestId,
                    voice.getName(),
                    voice.getName(),
                    voice.getLocale().toLanguageTag(),
                    voice.isNetworkConnectionRequired() ? "required" : "none",
                    isVoiceInstalled(voice));
        }
        callback.onVoicesCompleted(requestId);
    }

    private void startOnMain(
            String requestId,
            String text,
            String languageTag,
            String voiceId,
            Callback callback,
            TextToSpeech tts) {
        synchronized (lock) {
            if (activeSession != null) {
                callback.onFailure(
                        requestId,
                        "tts_busy",
                        "Android offline TTS is busy; the request was not queued.");
                return;
            }

            Voice voice = findExactVoice(tts, languageTag, voiceId);
            if (voice == null) {
                callback.onFailure(
                        requestId,
                        "voice_unavailable",
                        "The requested exact-locale TTS voice is unavailable; no alternate voice was selected.");
                return;
            }
            if (voice.isNetworkConnectionRequired()) {
                callback.onFailure(
                        requestId,
                        "voice_rejected",
                        "The requested Android TTS voice requires networking and is prohibited by RMA-123.");
                return;
            }
            if (!isVoiceInstalled(voice)) {
                callback.onFailure(
                        requestId,
                        "missing_voice_data",
                        "The requested offline TTS voice data is not installed. Install it from Android Text-to-speech settings; no network voice was selected.");
                return;
            }

            int setVoiceResult = tts.setVoice(voice);
            if (setVoiceResult != TextToSpeech.SUCCESS) {
                callback.onFailure(
                        requestId,
                        "voice_rejected",
                        "Android TextToSpeech rejected the exact offline voice; no alternate voice was selected.");
                return;
            }
            Voice selected = tts.getVoice();
            if (selected == null
                    || !voiceId.equals(selected.getName())
                    || !languageTag.equalsIgnoreCase(selected.getLocale().toLanguageTag())
                    || selected.isNetworkConnectionRequired()
                    || !isVoiceInstalled(selected)) {
                callback.onFailure(
                        requestId,
                        "voice_rejected",
                        "Android TextToSpeech did not retain the exact installed offline voice; synthesis was rejected before audio output.");
                return;
            }

            Session session = new Session(requestId, callback);
            activeSession = session;
            int result = tts.speak(text, TextToSpeech.QUEUE_ADD, null, requestId);
            if (result != TextToSpeech.SUCCESS) {
                activeSession = null;
                session.terminal = true;
                callback.onFailure(
                        requestId,
                        "synthesis_failure",
                        "Android TextToSpeech rejected offline synthesis; no alternate TTS provider was selected.");
            }
        }
    }

    private void installProgressListener(TextToSpeech tts) {
        tts.setOnUtteranceProgressListener(new UtteranceProgressListener() {
            @Override
            public void onStart(String utteranceId) {
                Session session = currentSession(utteranceId);
                if (session != null) {
                    session.callback.onStarted(session.requestId);
                }
            }

            @Override
            public void onDone(String utteranceId) {
                Session session = finishSession(utteranceId);
                if (session != null) {
                    session.callback.onDone(session.requestId);
                }
            }

            @Override
            @Deprecated
            public void onError(String utteranceId) {
                failSession(utteranceId, TextToSpeech.ERROR, "synthesis_failure");
            }

            @Override
            public void onError(String utteranceId, int errorCode) {
                failSession(utteranceId, errorCode, ttsErrorCode(errorCode));
            }

            @Override
            public void onStop(String utteranceId, boolean interrupted) {
                Session session = finishSession(utteranceId);
                if (session != null) {
                    session.callback.onStopped(session.requestId);
                }
            }
        });
    }

    private Session currentSession(String utteranceId) {
        synchronized (lock) {
            Session session = activeSession;
            if (session == null
                    || session.terminal
                    || !session.requestId.equals(utteranceId)) {
                return null;
            }
            return session;
        }
    }

    private Session finishSession(String utteranceId) {
        synchronized (lock) {
            Session session = currentSession(utteranceId);
            if (session == null) {
                return null;
            }
            session.terminal = true;
            activeSession = null;
            return session;
        }
    }

    private void failSession(String utteranceId, int errorCode, String code) {
        Session session = finishSession(utteranceId);
        if (session != null) {
            session.callback.onFailure(
                    session.requestId,
                    code,
                    ttsErrorDiagnostic(errorCode));
        }
    }

    private void cancelOnMain(String requestId) {
        Session session;
        TextToSpeech tts;
        synchronized (lock) {
            pendingInitialization.removeIf(
                    pending -> requestId.equals(pending.requestId));
            session = activeSession;
            tts = engine;
            if (session == null || !requestId.equals(session.requestId)) {
                return;
            }
            session.terminal = true;
            activeSession = null;
        }
        if (tts != null) {
            tts.stop();
        }
        session.callback.onStopped(requestId);
    }

    private void closeOnMain() {
        Session session;
        TextToSpeech tts;
        List<PendingInitialization> pending;
        synchronized (lock) {
            if (closed) {
                return;
            }
            closed = true;
            session = activeSession;
            activeSession = null;
            if (session != null) {
                session.terminal = true;
            }
            tts = engine;
            engine = null;
            pending = new ArrayList<>(pendingInitialization);
            pendingInitialization.clear();
            initializing = false;
        }
        if (tts != null) {
            tts.stop();
            tts.shutdown();
        }
        if (session != null) {
            session.callback.onStopped(session.requestId);
        }
        for (PendingInitialization item : pending) {
            item.callback.onFailure(
                    item.requestId,
                    "engine_unavailable",
                    "Android offline TTS bridge closed during initialization; no alternate provider was selected.");
        }
    }

    private static Voice findExactVoice(
            TextToSpeech tts,
            String languageTag,
            String voiceId) {
        Voice match = null;
        for (Voice voice : matchingVoices(tts, languageTag)) {
            if (!voiceId.equals(voice.getName())) {
                continue;
            }
            if (match != null) {
                return null;
            }
            match = voice;
        }
        return match;
    }

    private static VoiceCounts countVoices(TextToSpeech tts, String languageTag) {
        int offline = 0;
        int installedOffline = 0;
        int network = 0;
        for (Voice voice : matchingVoices(tts, languageTag)) {
            if (voice.isNetworkConnectionRequired()) {
                network++;
            } else {
                offline++;
                if (isVoiceInstalled(voice)) {
                    installedOffline++;
                }
            }
        }
        return new VoiceCounts(offline, installedOffline, network);
    }

    private static List<Voice> matchingVoices(TextToSpeech tts, String languageTag) {
        List<Voice> result = new ArrayList<>();
        Set<Voice> voices = tts.getVoices();
        if (voices == null) {
            return result;
        }
        for (Voice voice : voices) {
            if (voice != null
                    && languageTag.equalsIgnoreCase(voice.getLocale().toLanguageTag())) {
                result.add(voice);
            }
        }
        result.sort(Comparator.comparing(Voice::getName));
        return result;
    }

    private static boolean isVoiceInstalled(Voice voice) {
        Set<String> features = voice.getFeatures();
        return features == null
                || !features.contains(TextToSpeech.Engine.KEY_FEATURE_NOT_INSTALLED);
    }

    private static String languageStatus(int status) {
        switch (status) {
            case TextToSpeech.LANG_AVAILABLE:
                return "language_available";
            case TextToSpeech.LANG_COUNTRY_AVAILABLE:
                return "country_available";
            case TextToSpeech.LANG_COUNTRY_VAR_AVAILABLE:
                return "exact_available";
            case TextToSpeech.LANG_MISSING_DATA:
                return "missing_data";
            case TextToSpeech.LANG_NOT_SUPPORTED:
                return "not_supported";
            default:
                return "unknown";
        }
    }

    private static String ttsErrorCode(int errorCode) {
        switch (errorCode) {
            case TextToSpeech.ERROR_NETWORK:
                return "network_failure";
            case TextToSpeech.ERROR_NETWORK_TIMEOUT:
                return "network_timeout";
            case TextToSpeech.ERROR_NOT_INSTALLED_YET:
                return "missing_voice_data";
            case TextToSpeech.ERROR_OUTPUT:
                return "output_failure";
            case TextToSpeech.ERROR_SERVICE:
                return "service_failure";
            case TextToSpeech.ERROR_SYNTHESIS:
                return "synthesis_failure";
            case TextToSpeech.ERROR_INVALID_REQUEST:
                return "invalid_request";
            default:
                return "unknown_android_offline_tts_error";
        }
    }

    private static String ttsErrorDiagnostic(int errorCode) {
        if (errorCode == TextToSpeech.ERROR_NETWORK
                || errorCode == TextToSpeech.ERROR_NETWORK_TIMEOUT) {
            return "Android reported network use/failure while an installed offline voice was selected. RMA-123 treats this as an offline-provider contract violation and did not select another provider.";
        }
        return "Android offline TextToSpeech failed with error code " + errorCode
                + "; no alternate TTS provider was selected.";
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
                        "Timed out releasing Android offline TTS resources.");
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException(
                    "Interrupted while releasing Android offline TTS resources.",
                    exception);
        }
        RuntimeException exception = failure.get();
        if (exception != null) {
            throw exception;
        }
    }

    private interface EngineAction {
        void run(TextToSpeech tts);
    }

    private static final class PendingInitialization {
        private final String requestId;
        private final Callback callback;
        private final EngineAction action;

        private PendingInitialization(
                String requestId,
                Callback callback,
                EngineAction action) {
            this.requestId = requestId;
            this.callback = callback;
            this.action = action;
        }
    }

    private static final class VoiceCounts {
        private final int matchingOffline;
        private final int installedOffline;
        private final int matchingNetwork;

        private VoiceCounts(int matchingOffline, int installedOffline, int matchingNetwork) {
            this.matchingOffline = matchingOffline;
            this.installedOffline = installedOffline;
            this.matchingNetwork = matchingNetwork;
        }
    }

    private static final class Session {
        private final String requestId;
        private final Callback callback;
        private boolean terminal;

        private Session(String requestId, Callback callback) {
            this.requestId = requestId;
            this.callback = callback;
        }
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("The Unity activity is required.");
        }
    }

    private static void requireCallback(Callback callback) {
        if (callback == null) {
            throw new IllegalArgumentException("The offline TTS callback is required.");
        }
    }

    private static void requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required.");
        }
    }
}
