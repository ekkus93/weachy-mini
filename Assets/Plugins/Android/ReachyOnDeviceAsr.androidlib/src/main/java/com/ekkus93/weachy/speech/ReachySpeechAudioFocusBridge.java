package com.ekkus93.weachy.speech;

import android.app.Activity;
import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioManager;
import android.os.Handler;
import android.os.Looper;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

public final class ReachySpeechAudioFocusBridge {
    private static final long CLOSE_TIMEOUT_SECONDS = 5L;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Object lock = new Object();
    private Session activeSession;
    private boolean closed;

    public interface Callback {
        void onFocusGranted(String sessionId);
        void onFocusDenied(String sessionId, String code, String diagnostic);
        void onReleased(String sessionId);
        void onReleaseFailed(String sessionId, String code, String diagnostic);
        void onInterrupted(String sessionId, String code, String diagnostic);
    }

    public void request(
            Activity activity,
            String sessionId,
            String role,
            Callback callback) {
        requireActivity(activity);
        requireText(sessionId, "sessionId");
        requireText(role, "role");
        requireCallback(callback);
        Role parsedRole = parseRole(role);
        mainHandler.post(() -> requestOnMain(
                activity,
                sessionId,
                parsedRole,
                callback));
    }

    public void release(String sessionId, Callback callback) {
        requireText(sessionId, "sessionId");
        requireCallback(callback);
        mainHandler.post(() -> releaseOnMain(sessionId, callback));
    }

    public void close() {
        runOnMainThreadAndWait(this::closeOnMain);
    }

    private void requestOnMain(
            Activity activity,
            String sessionId,
            Role role,
            Callback callback) {
        Session session;
        synchronized (lock) {
            if (closed) {
                callback.onFocusDenied(
                        sessionId,
                        "audio_focus_bridge_closed",
                        "Android speech audio-focus bridge is closed; no fallback audio path was selected.");
                return;
            }
            if (activeSession != null) {
                callback.onFocusDenied(
                        sessionId,
                        "audio_focus_busy",
                        "Another speech audio session already owns the single audio path; requests are not queued.");
                return;
            }

            AudioManager manager = requireAudioManager(activity);
            int gain = role == Role.LISTENING
                    ? AudioManager.AUDIOFOCUS_GAIN_TRANSIENT_EXCLUSIVE
                    : AudioManager.AUDIOFOCUS_GAIN_TRANSIENT;
            AudioAttributes attributes = new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ASSISTANT)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                    .build();
            session = new Session(
                    activity.getApplicationContext(),
                    manager,
                    sessionId,
                    role,
                    callback);
            AudioManager.OnAudioFocusChangeListener listener =
                    focusChange -> onFocusChange(sessionId, focusChange);
            session.focusRequest = new AudioFocusRequest.Builder(gain)
                    .setAudioAttributes(attributes)
                    .setAcceptsDelayedFocusGain(false)
                    .setWillPauseWhenDucked(true)
                    .setOnAudioFocusChangeListener(listener, mainHandler)
                    .build();
            activeSession = session;
        }

        final int result;
        try {
            result = session.manager.requestAudioFocus(session.focusRequest);
        } catch (RuntimeException exception) {
            clearActive(session);
            callback.onFocusDenied(
                    sessionId,
                    "audio_focus_request_exception",
                    "Android AudioManager.requestAudioFocus failed with "
                            + exception.getClass().getSimpleName()
                            + "; no retry, foreground-service workaround, or provider fallback was attempted.");
            return;
        }

        if (result != AudioManager.AUDIOFOCUS_REQUEST_GRANTED) {
            clearActive(session);
            callback.onFocusDenied(
                    sessionId,
                    result == AudioManager.AUDIOFOCUS_REQUEST_DELAYED
                            ? "audio_focus_delayed_rejected"
                            : "audio_focus_denied",
                    "Android did not grant immediate speech audio focus. Delayed focus is disabled. "
                            + "On Android 15+ a target-35+ app must be the top app or already run an eligible foreground service; "
                            + "RMA-125 does not start one automatically.");
            return;
        }

        session.focusHeld = true;
        session.monitor = new ReachySpeechAudioInterruptionMonitor(
                session.context,
                session.manager,
                role == Role.LISTENING,
                mainHandler,
                (code, diagnostic) -> interruptSession(session, code, diagnostic));
        try {
            session.monitor.start();
        } catch (RuntimeException exception) {
            interruptSession(
                    session,
                    "audio_monitor_setup_failed",
                    "Speech audio interruption monitoring failed with "
                            + exception.getClass().getSimpleName()
                            + "; focus was abandoned and the operation was stopped.");
            return;
        }

        if (!session.interrupted) {
            callback.onFocusGranted(sessionId);
        }
    }

    private void releaseOnMain(String sessionId, Callback callback) {
        Session session = currentSession(sessionId);
        if (session == null) {
            synchronized (lock) {
                if (activeSession == null) {
                    callback.onReleased(sessionId);
                } else {
                    callback.onReleaseFailed(
                            sessionId,
                            "audio_focus_session_mismatch",
                            "The requested speech audio release did not match the active session; another session was not abandoned.");
                }
            }
            return;
        }

        RuntimeException cleanupFailure = cleanupSession(session);
        if (cleanupFailure != null) {
            callback.onReleaseFailed(
                    sessionId,
                    "audio_focus_release_failed",
                    "Releasing Android speech audio focus failed with "
                            + cleanupFailure.getClass().getSimpleName()
                            + "; the exact session remains retained for close-time cleanup and the managed coordinator must remain faulted.");
            return;
        }

        clearActive(session);
        callback.onReleased(sessionId);
    }

    private void onFocusChange(String sessionId, int focusChange) {
        Session session = currentSession(sessionId);
        if (session == null || session.interrupted) {
            return;
        }

        switch (focusChange) {
            case AudioManager.AUDIOFOCUS_LOSS:
                interruptSession(
                        session,
                        "audio_focus_loss_permanent",
                        "Android permanently revoked speech audio focus, including for higher-priority phone, alarm, or media activity where exposed.");
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT:
                interruptSession(
                        session,
                        "audio_focus_loss_transient",
                        "Android temporarily revoked speech audio focus; the active utterance was cancelled instead of being resumed automatically.");
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK:
                interruptSession(
                        session,
                        "audio_focus_duck_rejected",
                        "Android requested ducking, but recognition and assistant speech stop instead of continuing at reduced priority.");
                break;
            case AudioManager.AUDIOFOCUS_GAIN:
                // RMA-125 never auto-resumes a cancelled operation after focus gain.
                break;
            default:
                interruptSession(
                        session,
                        "audio_focus_change_unknown",
                        "Android reported an unknown audio-focus change; the active speech operation was stopped rather than guessed through.");
                break;
        }
    }

    private void interruptSession(
            Session session,
            String code,
            String diagnostic) {
        synchronized (lock) {
            if (closed || activeSession != session || session.interrupted) {
                return;
            }
            session.interrupted = true;
        }

        RuntimeException cleanupFailure = cleanupSession(session);
        if (cleanupFailure != null) {
            code = "audio_focus_interrupt_cleanup_failed";
            diagnostic = "Speech audio interruption cleanup failed with "
                    + cleanupFailure.getClass().getSimpleName()
                    + "; the operation was still cancelled and the exact session remains retained for an explicit release retry.";
        }
        session.callback.onInterrupted(session.sessionId, code, diagnostic);
    }

    private RuntimeException cleanupSession(Session session) {
        RuntimeException failure = null;
        if (session.monitor != null) {
            try {
                session.monitor.stop();
            } catch (RuntimeException exception) {
                failure = exception;
            }
        }
        if (session.focusHeld && session.focusRequest != null) {
            try {
                int result = session.manager.abandonAudioFocusRequest(session.focusRequest);
                if (result == AudioManager.AUDIOFOCUS_REQUEST_GRANTED) {
                    session.focusHeld = false;
                } else if (failure == null) {
                    failure = new IllegalStateException(
                            "AudioManager.abandonAudioFocusRequest did not report success.");
                }
            } catch (RuntimeException exception) {
                if (failure == null) {
                    failure = exception;
                }
            }
        }
        return failure;
    }

    private void closeOnMain() {
        Session session;
        synchronized (lock) {
            if (closed) {
                return;
            }
            closed = true;
            session = activeSession;
        }
        if (session != null) {
            RuntimeException cleanupFailure = cleanupSession(session);
            if (cleanupFailure != null) {
                throw new IllegalStateException(
                        "Closing Android speech audio focus could not release the exact session.",
                        cleanupFailure);
            }
            clearActive(session);
        }
    }

    private Session currentSession(String sessionId) {
        synchronized (lock) {
            Session session = activeSession;
            return session != null && session.sessionId.equals(sessionId)
                    ? session
                    : null;
        }
    }

    private void clearActive(Session session) {
        synchronized (lock) {
            if (activeSession == session) {
                activeSession = null;
            }
        }
    }

    private void runOnMainThreadAndWait(Runnable action) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            action.run();
            return;
        }

        CountDownLatch latch = new CountDownLatch(1);
        AtomicReference<RuntimeException> failure = new AtomicReference<>();
        mainHandler.post(() -> {
            try {
                action.run();
            } catch (RuntimeException exception) {
                failure.set(exception);
            } finally {
                latch.countDown();
            }
        });
        try {
            if (!latch.await(CLOSE_TIMEOUT_SECONDS, TimeUnit.SECONDS)) {
                throw new IllegalStateException(
                        "Timed out waiting for Android speech audio bridge main-thread teardown.");
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException(
                    "Interrupted while waiting for Android speech audio bridge teardown.",
                    exception);
        }
        RuntimeException value = failure.get();
        if (value != null) {
            throw value;
        }
    }

    private static AudioManager requireAudioManager(Activity activity) {
        Object service = activity.getSystemService(Context.AUDIO_SERVICE);
        if (!(service instanceof AudioManager)) {
            throw new IllegalStateException(
                    "Android AudioManager is unavailable; speech audio focus cannot be managed.");
        }
        return (AudioManager) service;
    }

    private static Role parseRole(String role) {
        if ("listening".equals(role)) {
            return Role.LISTENING;
        }
        if ("speaking".equals(role)) {
            return Role.SPEAKING;
        }
        throw new IllegalArgumentException("Unsupported speech audio role: " + role);
    }

    private static void requireActivity(Activity activity) {
        if (activity == null) {
            throw new IllegalArgumentException("activity is required");
        }
    }

    private static void requireCallback(Callback callback) {
        if (callback == null) {
            throw new IllegalArgumentException("callback is required");
        }
    }

    private static void requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required");
        }
    }

    private enum Role {
        LISTENING,
        SPEAKING,
    }

    private static final class Session {
        final Context context;
        final AudioManager manager;
        final String sessionId;
        final Role role;
        final Callback callback;
        AudioFocusRequest focusRequest;
        ReachySpeechAudioInterruptionMonitor monitor;
        boolean focusHeld;
        boolean interrupted;

        Session(
                Context context,
                AudioManager manager,
                String sessionId,
                Role role,
                Callback callback) {
            this.context = context;
            this.manager = manager;
            this.sessionId = sessionId;
            this.role = role;
            this.callback = callback;
        }
    }
}
