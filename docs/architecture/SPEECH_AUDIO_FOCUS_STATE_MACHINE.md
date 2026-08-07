# RMA-125 microphone and audio-focus state machine

**Status:** Implemented; deterministic and repository validation pending  
**Date:** 2026-08-07

## Scope

RMA-125 provides one fail-closed speech-audio ownership boundary shared by ASR and TTS. It coordinates the existing RMA-121/RMA-122 ASR providers and RMA-123/RMA-124 TTS providers without changing provider selection or adding fallback behavior.

The default offline stack is intentionally narrower: `ReachyAndroidSpeechAudioStackFactory.CreateOfflineDefaultAsync` wires only RMA-121 explicit Android on-device ASR and RMA-123 installed offline TTS through one shared `SpeechAudioFocusCoordinator`. It does not substitute RMA-122 or RMA-124 when an offline component is missing.

## State model

The coordinator exposes these states:

```text
Idle
  -> Acquiring
      -> Listening
      -> Speaking
      -> Idle        (focus denied)
      -> Interrupted (focus/route/platform event)
  -> Releasing
      -> Idle
      -> Faulted     (release failure)
Faulted
  -> Disposed        (recreate required before new speech)
Disposed
```

There is exactly one active lease. A second listening or speaking request returns a structured `Busy` failure. Requests are not queued and one role never preempts the other implicitly.

The single-phone-microphone limitation is exposed directly in `SpeechAudioSnapshot`:

- `SingleMicrophoneOnly == true`;
- `MaximumConcurrentMicrophoneCaptures == 1`;
- `SupportsSimultaneousListeningAndSpeaking == false`.

This is an ownership contract, not a direction-of-arrival or multi-microphone simulation.

## Self-transcription prevention

`AudioCoordinatedAsrProvider` acquires the listening lease before entering the selected ASR provider. `AudioCoordinatedTtsProvider` acquires the speaking lease before entering the selected TTS provider.

A provider terminal event is deliberately withheld until the exact audio-focus lease has been released. Therefore an orchestrator cannot observe ASR `FinalResult` and start TTS while the microphone lease is still owned, nor observe TTS `Completed` and restart ASR before speaker focus teardown has finished.

If TTS is attempted while ASR owns the lease, or ASR is attempted while TTS owns it, the second operation receives `speech_audio_busy`. The coordinator does not cancel the first operation, queue the second operation, or guess which one should win.

## Android focus contract

`ReachySpeechAudioFocusBridge` requires API 26+, matching the current Android minimum, and uses `AudioFocusRequest` rather than the deprecated legacy focus request overload.

Listening requests `AUDIOFOCUS_GAIN_TRANSIENT_EXCLUSIVE`, which Android documents as appropriate for recording or speech recognition when competing playback should not occur. Speaking requests `AUDIOFOCUS_GAIN_TRANSIENT` with assistant/speech audio attributes. Delayed focus is disabled and ducking is treated as an interruption rather than permission to continue recognition or speech at reduced priority.

The bridge abandons the exact `AudioFocusRequest` on release or interruption. If focus release fails, the managed coordinator enters `Faulted`; it does not claim success and permit another audio owner.

Android 15 and later can reject a focus request for target-35+ applications that are neither the top app nor already running an eligible foreground service. RMA-125 surfaces that denial. It does not silently start a foreground service to manufacture focus eligibility.

## Interruption and route handling

Any permanent, transient, duck-capable, or unknown focus loss terminates the active speech operation. Focus gain after loss does not resume it automatically; a new operation requires an explicit new request.

Where Android exposes additional signals, the bridge also treats these as terminal interruptions:

- `AudioDeviceCallback` device additions/removals, including Bluetooth A2DP/SCO and wired/USB headset categories;
- `ACTION_AUDIO_BECOMING_NOISY`, covering common wired-headset unplug and A2DP-disconnect transitions before output falls back to the speaker;
- `ACTION_MICROPHONE_MUTE_CHANGED` while listening;
- API-31+ audio mode changes into ringtone, in-call, in-communication, or call-screening modes.

Phone calls and alarms can also cause ordinary audio-focus loss; those focus callbacks remain authoritative even when Android does not expose a more specific cause. RMA-125 does not request `READ_PHONE_STATE`, inspect call logs, or infer phone state through privileged APIs.

A Bluetooth/headphone route change cancels the operation instead of allowing Android to silently continue the same utterance on a new route. A later user/orchestrator action may start a fresh operation after the route is stable.

## Exact identity and stale callbacks

The coordinator creates an opaque per-lease audio session ID. Android callbacks include that ID. A callback for any stale or different session is ignored by the coordinator and cannot cancel the active lease.

The Unity bridge also validates callback identity before completing focus/release tasks. Request cancellation asks the Java bridge to release the same session so a late focus grant cannot leak ownership.

## Failure semantics

RMA-125 preserves the RMA-120 no-fallback policy:

- no automatic provider substitution;
- no automatic utterance retry;
- no delayed-focus queue;
- no automatic resume after focus regain;
- no background/foreground-service workaround;
- no silent route continuation;
- no conversion of focus loss into successful ASR/TTS completion.

Focus/route interruptions map to structured `ServiceFailure` errors with exact provider-safe codes. Caller cancellation remains `Cancelled` when there was no platform interruption. A focus release failure replaces an otherwise successful terminal event with a visible failure and faults the coordinator.

## Permissions and privacy

RMA-125 retains the existing `RECORD_AUDIO` permission required by ASR. It adds no phone-state, call-log, contacts, Bluetooth scan, location, or network permission.

The audio-focus bridge does not capture, persist, inspect, or log microphone samples, transcripts, TTS text, device names, Bluetooth addresses, phone numbers, call metadata, or alarm content. Route diagnostics expose only broad categories such as Bluetooth audio or headset.

## Offline default boundary

The offline default stack consists of:

```text
RMA-121 explicit Android on-device ASR
        -> AudioCoordinatedAsrProvider
        -> shared SpeechAudioFocusCoordinator
        <- AudioCoordinatedTtsProvider
RMA-123 installed offline Android TTS
```

RMA-121 already fails visibly when explicit on-device recognition, permission, language support, or downloaded recognition data is missing. RMA-123 already fails visibly when an installed exact-locale offline voice is missing. RMA-125 preserves those setup states and does not redirect either failure to network-capable providers.

## Validation boundary

The permanent RMA-125 deterministic suite verifies the coordinator and decorators without a device. Hosted Android CI compiles the Java bridge with Java 17 `-Xlint:all -Werror` and Android lint warnings-as-errors through the existing `android-plugin` source set.

Those gates can prove state-machine semantics, packaging, API/lint compatibility, and no-fallback source contracts. They cannot prove audible synthesis or live recognition on a particular handset. The TODO's offline-speech acceptance remains open until a physical device with installed services demonstrates RMA-121 recognition followed by RMA-123 speech with networking disabled and records visible setup guidance for unavailable services.
