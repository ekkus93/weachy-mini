package com.ekkus93.weachy.speech;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.media.AudioDeviceCallback;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.os.Build;
import android.os.Handler;

import java.util.HashSet;
import java.util.Set;

final class ReachySpeechAudioInterruptionMonitor {
    interface Listener {
        void onInterrupted(String code, String diagnostic);
    }

    private final Context context;
    private final AudioManager manager;
    private final boolean listening;
    private final Handler handler;
    private final Listener listener;
    private final Set<Integer> knownDeviceIds = new HashSet<>();
    private AudioDeviceCallback deviceCallback;
    private BroadcastReceiver receiver;
    private Object modeChangedListener;
    private boolean deviceRegistered;
    private boolean receiverRegistered;
    private boolean modeRegistered;

    ReachySpeechAudioInterruptionMonitor(
            Context context,
            AudioManager manager,
            boolean listening,
            Handler handler,
            Listener listener) {
        this.context = require(context, "context");
        this.manager = require(manager, "manager");
        this.listening = listening;
        this.handler = require(handler, "handler");
        this.listener = require(listener, "listener");
    }

    void start() {
        int deviceDirections =
                AudioManager.GET_DEVICES_INPUTS | AudioManager.GET_DEVICES_OUTPUTS;
        for (AudioDeviceInfo device : manager.getDevices(deviceDirections)) {
            knownDeviceIds.add(device.getId());
        }

        deviceCallback = new AudioDeviceCallback() {
            @Override
            public void onAudioDevicesAdded(AudioDeviceInfo[] addedDevices) {
                boolean changed = false;
                String category = "audio route";
                for (AudioDeviceInfo device : addedDevices) {
                    if (knownDeviceIds.add(device.getId())) {
                        changed = true;
                        category = routeCategory(device);
                    }
                }
                if (changed) {
                    listener.onInterrupted(
                            "audio_route_added",
                            "A " + category
                                    + " device was connected while speech audio was active; the operation was cancelled so routing can be re-evaluated explicitly.");
                }
            }

            @Override
            public void onAudioDevicesRemoved(AudioDeviceInfo[] removedDevices) {
                boolean changed = false;
                String category = "audio route";
                for (AudioDeviceInfo device : removedDevices) {
                    if (knownDeviceIds.remove(device.getId())) {
                        changed = true;
                        category = routeCategory(device);
                    }
                }
                if (changed) {
                    listener.onInterrupted(
                            "audio_route_removed",
                            "A " + category
                                    + " device was disconnected while speech audio was active; the operation was cancelled before Android can silently move it to another route.");
                }
            }
        };
        manager.registerAudioDeviceCallback(deviceCallback, handler);
        deviceRegistered = true;

        receiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context receiverContext, Intent intent) {
                String action = intent.getAction();
                if (AudioManager.ACTION_AUDIO_BECOMING_NOISY.equals(action)) {
                    listener.onInterrupted(
                            "audio_becoming_noisy",
                            "Android reported an imminent headphone or Bluetooth output-route change; speech was stopped before audio can unexpectedly move to the speaker.");
                } else if (Build.VERSION.SDK_INT >= 28
                        && MicrophoneMuteApi28.isMuteChangedAction(action)
                        && listening
                        && manager.isMicrophoneMute()) {
                    listener.onInterrupted(
                            "microphone_muted",
                            "Android reports that the single phone microphone is muted; listening was stopped visibly.");
                }
            }
        };
        registerReceiverCompat(context, receiver);
        receiverRegistered = true;

        if (Build.VERSION.SDK_INT >= 31) {
            modeChangedListener = ModeMonitorApi31.register(
                    manager,
                    context,
                    mode -> {
                        if (isCallOrCommunicationMode(mode)) {
                            listener.onInterrupted(
                                    "phone_or_communication_audio_mode",
                                    "Android entered a phone or communication audio mode while speech was active; the operation was cancelled and is not resumed automatically.");
                        }
                    });
            modeRegistered = true;
        }

        if (listening && manager.isMicrophoneMute()) {
            listener.onInterrupted(
                    "microphone_muted",
                    "Android reports that the single phone microphone is muted; listening cannot start.");
            return;
        }
        if (isCallOrCommunicationMode(manager.getMode())) {
            listener.onInterrupted(
                    "phone_or_communication_audio_mode",
                    "Android is already in a phone or communication audio mode; speech was stopped rather than competing for the route.");
        }
    }

    void stop() {
        RuntimeException failure = null;
        if (modeRegistered && modeChangedListener != null) {
            try {
                ModeMonitorApi31.unregister(manager, modeChangedListener);
                modeRegistered = false;
            } catch (RuntimeException exception) {
                failure = exception;
            }
        }
        if (receiverRegistered && receiver != null) {
            try {
                context.unregisterReceiver(receiver);
                receiverRegistered = false;
            } catch (RuntimeException exception) {
                if (failure == null) {
                    failure = exception;
                }
            }
        }
        if (deviceRegistered && deviceCallback != null) {
            try {
                manager.unregisterAudioDeviceCallback(deviceCallback);
                deviceRegistered = false;
            } catch (RuntimeException exception) {
                if (failure == null) {
                    failure = exception;
                }
            }
        }
        if (failure != null) {
            throw failure;
        }
        knownDeviceIds.clear();
    }

    private static boolean isCallOrCommunicationMode(int mode) {
        return mode == AudioManager.MODE_RINGTONE
                || mode == AudioManager.MODE_IN_CALL
                || mode == AudioManager.MODE_IN_COMMUNICATION
                || (Build.VERSION.SDK_INT >= 30
                    && CallScreeningApi30.isCallScreeningMode(mode));
    }

    private static String routeCategory(AudioDeviceInfo device) {
        switch (device.getType()) {
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP:
            case AudioDeviceInfo.TYPE_BLUETOOTH_SCO:
                return "Bluetooth audio";
            case AudioDeviceInfo.TYPE_WIRED_HEADSET:
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES:
            case AudioDeviceInfo.TYPE_USB_HEADSET:
                return "headphone/headset";
            default:
                return "audio route";
        }
    }

    private static void registerReceiverCompat(
            Context context,
            BroadcastReceiver receiver) {
        if (Build.VERSION.SDK_INT >= 33) {
            ReceiverApi33.register(context, receiver);
        } else if (Build.VERSION.SDK_INT >= 28) {
            ReceiverApi28.register(context, receiver);
        } else {
            ReceiverApi26.register(context, receiver);
        }
    }

    private static <T> T require(T value, String name) {
        if (value == null) {
            throw new IllegalArgumentException(name + " is required");
        }
        return value;
    }

    private interface ModeObserver {
        void onModeChanged(int mode);
    }

    private static final class ReceiverApi26 {
        private ReceiverApi26() {
        }

        static void register(
                Context context,
                BroadcastReceiver receiver) {
            context.registerReceiver(
                    receiver,
                    new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
        }
    }

    private static final class ReceiverApi28 {
        private ReceiverApi28() {
        }

        static void register(
                Context context,
                BroadcastReceiver receiver) {
            if (Build.VERSION.SDK_INT < 28) {
                throw new IllegalStateException(
                        "Microphone-mute receiver registration requires Android API 28 or newer.");
            }
            IntentFilter filter =
                    new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY);
            filter.addAction(AudioManager.ACTION_MICROPHONE_MUTE_CHANGED);
            context.registerReceiver(receiver, filter);
        }
    }

    private static final class MicrophoneMuteApi28 {
        private MicrophoneMuteApi28() {
        }

        static boolean isMuteChangedAction(String action) {
            if (Build.VERSION.SDK_INT < 28) {
                throw new IllegalStateException(
                        "Microphone-mute broadcast inspection requires Android API 28 or newer.");
            }
            return AudioManager.ACTION_MICROPHONE_MUTE_CHANGED.equals(action);
        }
    }

    private static final class CallScreeningApi30 {
        private CallScreeningApi30() {
        }

        static boolean isCallScreeningMode(int mode) {
            if (Build.VERSION.SDK_INT < 30) {
                throw new IllegalStateException(
                        "Call-screening audio-mode inspection requires Android API 30 or newer.");
            }
            return mode == AudioManager.MODE_CALL_SCREENING;
        }
    }

    private static final class ModeMonitorApi31 {
        private ModeMonitorApi31() {
        }

        static Object register(
                AudioManager manager,
                Context context,
                ModeObserver observer) {
            if (Build.VERSION.SDK_INT < 31) {
                throw new IllegalStateException(
                        "Audio-mode listener registration requires Android API 31 or newer.");
            }
            AudioManager.OnModeChangedListener value = observer::onModeChanged;
            manager.addOnModeChangedListener(context.getMainExecutor(), value);
            return value;
        }

        static void unregister(AudioManager manager, Object value) {
            if (Build.VERSION.SDK_INT < 31) {
                throw new IllegalStateException(
                        "Audio-mode listener removal requires Android API 31 or newer.");
            }
            manager.removeOnModeChangedListener(
                    (AudioManager.OnModeChangedListener) value);
        }
    }

    private static final class ReceiverApi33 {
        private ReceiverApi33() {
        }

        static void register(
                Context context,
                BroadcastReceiver receiver) {
            if (Build.VERSION.SDK_INT < 33) {
                throw new IllegalStateException(
                        "Non-exported receiver registration requires Android API 33 or newer.");
            }
            IntentFilter filter =
                    new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY);
            filter.addAction(AudioManager.ACTION_MICROPHONE_MUTE_CHANGED);
            context.registerReceiver(
                    receiver,
                    filter,
                    Context.RECEIVER_NOT_EXPORTED);
        }
    }
}
