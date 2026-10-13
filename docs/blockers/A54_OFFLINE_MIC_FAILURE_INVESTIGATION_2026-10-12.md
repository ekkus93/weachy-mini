# A54 offline microphone failure investigation

**Recorded:** 2026-10-12
**Device:** Samsung Galaxy A54 (`SM-A546E`), Android API 36, ADB serial `R5CW31AX4FL`
**Acceptance path:** RMA-190 real offline spoken turn; RMA-125 physical offline speech
**Status:** Open. The latest captured failure occurs during Android audio-focus cleanup before the recognizer becomes ready. No successful real human utterance has been captured.

## Summary for the next investigator

The operator has repeatedly reported that the app “crashed” or disappeared as the microphone test started. The evidence distinguishes two classes of failure:

1. Some earlier attempts reached the listening state, then failed the conversation before a final ASR result. The artifact reports an `InvalidOperationException` with the generic code `conversation-failed`. The operator reports that the app disappeared before they could speak in several of these attempts.
2. The latest captured attempt (attempt 10) failed before listening began. The acceptance report identifies `audio_focus_release_failed`, `recognizer_ready=false`, and `observed_listening=false`. The harness therefore did not prompt the operator to speak.

For captured test runs, the disappearance is not currently evidenced as an Android process crash. The shell harness force-stops the app during cleanup. `dumpsys activity exit-info` was previously observed to report `USER REQUESTED` / `FORCE STOP`; this is consistent with the runner terminating the failed test process. Preserve this distinction in logs and reports. It does not rule out an additional app or platform defect during startup.

The latest failure points to the speech audio-focus release path, but the public failure code is still too generic to identify whether Android monitor teardown, focus abandonment, or a Java/.NET bridge exception caused it. Managed/Java changes are in progress to preserve that failure reason; they had not yet been rebuilt into the APK or tested on the device at the time of this report.

## Device and test setup

- Device: Samsung SM-A546E / Galaxy A54, Android API 36; connected ADB serial `R5CW31AX4FL`.
- The test installs the shipping ARM64 app and selects the on-device LLM through the production package manager. The selected model is `qwen3-0.6b`, SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`.
- The app reports microphone available, explicit on-device ASR preflight `Available`, and the production behavior service ready.
- The shell harness disables connectivity before app launch and captures `airplane_mode=1`, `wifi_on=0`, and `mobile_data=0` in attempt 10.
- No synthetic transcript, cloud credentials, system-ASR fallback, network-TTS fallback, transcript persistence, or raw-audio persistence is used by this harness.
- A start gate keeps the test waiting until the operator explicitly triggers the microphone. After the trigger, the runner starts its bounded timeout. This is intended to prevent the operator’s response time from consuming the app-run timeout.
- The test must not be triggered until the operator is present and ready. A readiness reply is not itself evidence that the user got the later “speak now” prompt.

## Observed attempt history

The build artifacts in `build/rma190-offline-speech-attempt-*` are local diagnostic files, not release acceptance evidence. Some attempts have no final report because they ended before the app wrote one.

| Attempt | Captured outcome | Human speech reached? |
| --- | --- | --- |
| 3 | `real_spoken_turn`, generic `conversation-failed`; listening observed, no transcribing/thinking/speaking | No final ASR result in artifact; operator reported app disappeared before speaking on repeated attempts |
| 4 | Same generic conversation failure after listening; ASR preflight `Available`; no transcription | Not evidenced |
| 5 | No acceptance JSON present | Not evidenced; incomplete runner artifact |
| 6 | Same generic conversation failure after listening | Not evidenced |
| 7 | `wait_for_listening`, failure code empty, recognizer not ready, listening not observed | No |
| 8 | `wait_for_listening`, generic `conversation-failed`, recognizer not ready, listening not observed | No |
| 9 | No acceptance JSON present. A runner timeout expired while waiting at the manual start gate, before the operator trigger | No; harness timeout/cleanup, not an ASR test |
| 10 | `wait_for_listening`, `audio_focus_release_failed`, recognizer not ready, listening not observed | No; the harness did not reach its speak prompt |

The operator has provided multiple readiness replies and repeatedly reported that the app disappeared/crashed before they could speak. In several exchanges there was no explicit prompt to speak before failure. Do not count any attempt as a negative human-utterance/recognition test unless the harness report proves `recognizer_ready=true` and the operator confirms they spoke after the prompt.

## Latest captured report

Source artifact: `build/rma190-offline-speech-attempt-10/rma190-offline-speech-end-to-end-acceptance.json` (also copied to `device-report.json`). Key fields:

```text
status=failed
failure_stage=wait_for_listening
failure_code=audio_focus_release_failed
last_interaction_state=Error
microphone_available=true
asr_preflight_state=Available
recognizer_ready=false
observed_listening=false
selected_llm_execution=OnDevice
local_model_id=qwen3-0.6b
synthetic_speech_injected=false
transcript_persisted=false
raw_audio_persisted=false
system_asr_fallback_used=false
network_tts_fallback_used=false
```

Attempt 10 `network-disabled.txt` records airplane mode on, Wi-Fi off, mobile data off. `focus.txt` shows the Unity activity had foreground focus at capture time. The report’s `behavior_revision_before=25` and `behavior_revision_after=0` indicate that no behavior was executed in this failed attempt.

The test wrapper subsequently restores the prior network state and force-stops the app as part of cleanup. The phone was reported online and usable after cleanup.

## Relevant code path and current hypothesis

The acceptance harness calls the shipping `ReachyMainScreen.RequestMicrophone()` path. The ASR coordinator first acquires the listening audio-focus lease, then starts Android on-device recognition. The harness now separately waits for the ASR `Started` event before announcing readiness; that event means recognizer-ready and should leave the interaction state as `Listening`. Only partial/final recognition results should advance to transcription.

The Android Java bridge’s cleanup path is `ReachySpeechAudioFocusBridge.cleanupSession()` in `Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachySpeechAudioFocusBridge.java`. It stops the interruption monitor and calls `AudioManager.abandonAudioFocusRequest()`. Before the current in-progress diagnostic changes, cleanup errors were collapsed to the managed code `audio_focus_release_failed`, hiding the specific failing operation.

The current working-tree investigation adds typed `SpeechAudioFocusException` propagation and Java reason codes:

- `audio_focus_monitor_stop_failed`
- `audio_focus_abandon_failed`
- `audio_focus_abandon_exception`
- `audio_focus_release_exception`

This is a diagnostic change, not a demonstrated fix. It preserves a bounded code while continuing to redact exception messages. Inspect the complete diff, compile/test it, build a new APK, and only then use a further device attempt to learn the exact cleanup result. The existing attempt 10 APK predates this reason-code propagation.

Potential areas to inspect after the precise reason is known:

1. Whether the interruption monitor is stopping listeners on the correct Android thread and whether stop is idempotent after successful start.
2. Whether the exact `AudioFocusRequest` instance and owning `AudioManager` are retained across Java/.NET calls and cleanup.
3. Whether `abandonAudioFocusRequest()` returns a failure code in this device/service sequence, and whether the session has already been interrupted or abandoned.
4. Whether a platform exception crosses the AndroidJavaObject callback boundary; retain only exception class and safe code, never raw messages or audio data.
5. Whether an earlier error is being replaced by cleanup failure, masking the original ASR startup failure. Preserve primary and cleanup errors separately if both occur.
6. Whether the app process remains alive through the failure and only the shell cleanup terminates it. Capture PID/process exit reason before harness cleanup to establish this conclusively.

Do not automatically retry focus acquisition, silently switch to Android system ASR, or enable network speech as a workaround. Keep the operation visibly failed and the coordinator faulted if the exact focus session cannot be safely released.

## Earlier listening-state failures

Attempts 3, 4, and 6 reached `observed_listening=true` but did not reach `observed_transcribing=true`. Their reports expose only the generic `conversation-failed` code and `InvalidOperationException`; they do not contain the recognizer callback cause. This is a separate observed symptom from attempt 10’s pre-listening focus-release failure and may be a secondary error path or a different failure. The updated harness diagnostic is intended to capture the app’s safe failure code and state at the wait boundary. Inspect logcat and the full report for those older attempts if retained; their current JSON alone is insufficient to determine whether Android returned an error callback, timed out, or the operator never spoke.

## Unit-test coverage and limits

A managed regression test was added to the core conversation state-machine suite: an ASR `Started` event must leave the orchestrator in `Listening` and retain its ASR lease; a final result advances once to transcription. This addresses a discovered event-ordering bug where any ASR event, including `Started`, could transition the app to `Transcribing` prematurely.

An additional speech audio-focus contract test is currently in the working tree to verify that a typed Android release reason survives the managed coordinator boundary and that a failed release leaves the coordinator faulted. It still needs to be run against the present working tree. These are deterministic managed tests. They do not invoke a physical microphone, Android `SpeechRecognizer`, Samsung audio-focus implementation, or the JNI callback path, and cannot prove that listening crashes (or does not crash) on the A54.

Tests reported passing before the newest reason-code changes:

- `managed/ReachyMini.Core.Tests` — core conversation contracts, including recognizer-ready sequencing.
- `managed/ReachyMini.AndroidOnDeviceAsr.Tests` — 35 tests.
- `python3 scripts/tests/test_rma190_offline_speech_end_to_end.py` — 9 runner contract tests.
- `bash -n scripts/run_rma190_offline_speech_end_to_end_android.sh` and `git diff --check`.

The speech-audio-focus contract suite, full current diff, Java compilation, and a newly built APK must be revalidated because diagnostic code changed after those results. No test run should be described as having proven physical mic stability.

## Runner and process-termination detail

The runner is `scripts/run_rma190_offline_speech_end_to_end_android.sh`. It explicitly force-stops the package before setup/install and during cleanup. The operator start gate is implemented with a `start-microphone.trigger` file. Attempt 9 exposed a harness issue: a timeout started before the indefinite gate, so it expired while waiting for the operator and terminated the app before the trigger. The runner has since been changed to reset the bounded timeout after the trigger; its Python contract tests cover this timing behavior. This was a harness timeout, not a demonstrated app microphone crash.

The harness’s cleanup and Android process-exit diagnostics must be considered when interpreting disappearance. A failed JSON with a stage and failure code is stronger evidence than visual disappearance alone. An Android fatal exception/native tombstone or a crash exit reason tied to the app PID would establish an actual process crash; the available attempt-10 report instead shows a managed interaction failure followed by harness cleanup.

## Current implementation changes relevant to this incident

Working-tree changes include:

- A manual start gate and post-trigger timeout reset in the runner.
- Separate recognizer-ready signaling, with ASR `Started` remaining in `Listening`.
- A safe empty-transcript diagnostic and more specific diagnostics captured at the wait-for-listening boundary.
- Typed audio-focus release failures and Android cleanup reason codes, still pending test/build/device verification.
- Contract tests for state sequencing and reason-code propagation.

The workspace may contain other unrelated changes and generated Unity files. Inspect `git status` and the focused diffs; do not discard user or Unity artifacts during investigation.

## Recommended next steps

1. Review the focused source diff, especially Java cleanup and callback handling; ensure a cleanup error cannot overwrite the initiating ASR error without recording both safely.
2. Run the speech audio-focus tests, core tests, Android on-device ASR tests, RMA-190 runner contract tests, Java/Android build checks, and `git diff --check`.
3. Build and audit a fresh ARM64 APK; record its SHA-256 and exact source revision/worktree state in the next test record.
4. Verify ADB device identity and capture logcat/process identity before launching. Keep network restoration in the runner’s guaranteed cleanup.
5. Start the runner, wait at the setup-ready gate, tell the operator explicitly that the app is ready and mic is still closed, and wait for a clear ready response before writing the trigger. After triggering, confirm the harness itself emits its “speak now” prompt before asking whether speech occurred.
6. If failure repeats, collect the sanitized acceptance JSON, app-scoped logcat, PID, `dumpsys activity exit-info` before cleanup if possible, and native tombstone evidence. Distinguish process death from app state `Error` plus runner force-stop.
7. If recognizer-ready is reached, the operator speaks a short request immediately after the prompt. Capture whether `PartialResult`/`FinalResult` arrives, then local model intent, behavior state, listening-focus release, speaking-focus acquisition, audible offline TTS, and return to idle.
8. Do not claim RMA-125 or RMA-190 physical speech acceptance until a real utterance and audible response are proven with networking off. The interruption, route-change, unavailable-service/setup-guidance, and privacy negative paths remain separate acceptance needs.

## Evidence limitations

- Artifacts referenced here are local under `build/`; they are not uploaded, immutable CI artifacts.
- Attempts have not all been captured; attempts 5 and 9 lack acceptance JSON in the available tree.
- The current exact source tree includes in-progress uncommitted changes; attempt 10 predates the newest diagnostic propagation.
- No successful manual human utterance has been evidenced. The repeated operator readiness replies must not be interpreted as proof they were prompted or spoke.
- This incident report records diagnosis and next actions; it does not close any acceptance checkbox.

## Follow-up investigation, later on 2026-10-12

The A54's retained `dumpsys audio` focus history identifies the attempt-10 process (`pid=23388`) requesting exclusive assistant speech focus at `16:05:06.423` and abandoning it at `16:05:06.499`, about 76 ms later. The report file was written at `16:05:06.871`. `dumpsys activity exit-info` then records this process exiting at `16:05:06.971` with `reason=USER REQUESTED`, `subreason=FORCE STOP`; the runner's cleanup contains the matching `am force-stop`. This is direct device evidence for an immediate speech-start failure followed by runner termination, rather than a recorded Android fatal process crash. The focus history proves an abandon call was seen by Android; it does not prove that Java's `abandonAudioFocusRequest()` returned success or that interruption-monitor cleanup succeeded.

A concrete source-level threading defect was found in the shipping ASR chain. `ReachyConversationTurnOrchestrator.RunAsrAsync()` starts on Unity's synchronization context, but `AudioCoordinatedAsrProvider.RecognizeAsync()` used `ConfigureAwait(false)` after asynchronous focus acquisition and after each inner recognizer move. This could resume the wrapper on an unattached worker thread immediately before invoking `ReachyAndroidOnDeviceAsrPlatform` through Unity `AndroidJavaObject`. The inner on-device provider and platform also used `ConfigureAwait(false)` at event-read points preceding subsequent JNI cleanup. Unity's Android JNI API requires a worker thread to be attached to the JVM before JNI calls. The code comments intended to preserve Unity's context, but the outer wrapper discarded it first. This is a plausible initiating cause of the pre-listening failure; the captured report cannot yet prove it was the only cause.

The working tree now preserves the caller's synchronization context across focus acquisition, inner ASR event moves, focus release/disposal, and the Android platform event queue. The previously pending typed audio-focus cleanup codes are also included in a freshly built APK. Focused checks passed:

- RMA-125 speech audio-focus contracts: 19 passed, including preserved release reason and a delayed-focus test that verifies recognizer start and focus release resume on the caller's synchronization context.
- RMA-121 Android on-device ASR contracts: 35 passed.
- Core conversation contracts passed.
- RMA-190 runner tests: 9 passed; shell syntax and `git diff --check` passed.
- Unity ARM64 API-26 APK build and release package audit passed: SHA-256 `02767f5797637bb131681af89ba567be5a9d450f9442ddc210c9f9343c502465`, 13 ARM64 native libraries, zero deferred payloads.

These checks prove compilation and deterministic contracts. The revised APK has not yet been exercised through the A54 microphone. A physical retest must wait until the operator is present, the runner has reached its setup gate, and the operator explicitly confirms readiness. If it still fails, capture the new specific audio-focus code and the first ASR terminal code; the current wrapper can still mask a primary recognizer failure with a later release error.

## Physical attempt 11 on the revised APK

The operator confirmed readiness. The runner installed APK SHA-256 `02767f5797637bb131681af89ba567be5a9d450f9442ddc210c9f9343c502465`, reached its manual setup gate, and was triggered only after an explicit setup-complete message. It then emitted its listening marker. The report at `build/rma190-offline-speech-attempt-11/rma190-offline-speech-end-to-end-acceptance.json` says `recognizer_ready=true`, `observed_listening=true`, `failure_stage=real_spoken_turn`, and `failure_code=android_on_device_asr_no_match`. The listening marker was written at `16:30:50.521` local time and the failure report at `16:30:55.697`, leaving about 5.2 seconds before the recognizer ended without speech.

The operator answered that they did **not** get to speak before it ended. Thus this is evidence that the revised app reached the recognizer-ready state, but it is not a negative test of recognizing the operator's actual utterance. The runner restored connectivity (`airplane_mode=0`, `wifi_on=1`, `mobile_data=1`). Android exit-info records the app's subsequent exit as `USER REQUESTED` / `FORCE STOP`, matching harness cleanup. The original immediate pre-listening audio-focus failure did not recur in this attempt; the thread-context fix is supported by this observation but a successful spoken turn is still required to confirm the path.

## Physical attempt 12: ASR succeeds, local LLM fails

The same revised APK was installed for attempt 12. After the setup gate, the operator was given a GO prompt at the microphone trigger. The acceptance report at `build/rma190-offline-speech-attempt-12/rma190-offline-speech-end-to-end-acceptance.json` records `recognizer_ready=true`, `observed_listening=true`, `observed_transcribing=true`, and `observed_thinking=true`. In the production orchestrator, entering Thinking requires a nonempty final ASR transcript; the report therefore establishes that the explicit on-device recognizer completed a nonempty recognition result. The transcript itself was not persisted. Operator confirmation of what they spoke was requested separately.

The next stage failed with `failure_code=llm-local-governed-failure`, before behavior or TTS (`observed_speaking=false`, `behavior_revision_after=0`). That code covers several `LocalLlmGovernedGenerationStatus` values and does not establish whether the A54's resource governor suspended inference, resource monitoring failed, memory was exhausted, or the provider was busy. Inspect the actual governed status safely before attributing the failure to thermal pressure. The app process was again force-stopped by harness cleanup, with network restored to airplane mode off, Wi-Fi on, and mobile data on.

The operator subsequently confirmed they spoke the full command after GO. Together with the observed nonempty final ASR result and transition to Thinking, attempt 12 physically verifies microphone startup and real speech recognition on the A54. It does not verify a successful local-model response or audible TTS.

The subsequent working-tree change appends the governed status enum name to this safe failure code. Its core contracts, RMA-173 source regressions, and Unity ARM64 build/package audit passed. The resulting APK SHA-256 is `926844e8147c0525dc96641312520b5d41bcf39bdaaa8f73769e5ed5ec24e1c7` (13 ARM64 native libraries; zero deferred payloads). This APK has been used in model-only A54 governor runs, but has not yet been retested through the microphone. It does not change the governor's thresholds or bypass its refusal to run inference.

## Model-only governor follow-up

The existing RMA-135 device acceptance was run without microphone input using the status-instrumented APK. Local artifact: `build/rma135-mic-followup-2026-10-12/`. It failed at `production_physics_runtime_preserved` after eight post-recovery generation attempts; this is a real acceptance failure, not evidence of a completed RMA-135 gate. Safe checkpoint 20 records `ResourceCancelledDuringGeneration`, governor mode `Minimal`, reasons `DeviceProfileLimit, PhysicsAtRisk`, and seven worker deadline misses. Later safe checkpoints include `ResourceSuspendedBeforeStart` under `RecoveryHold` and `PhysicsSignalUnavailable`, and another cancellation under `PhysicsBudgetExceeded`. Checkpoint 42 records eight exhausted attempts, with the last status `ResourceSuspendedBeforeStart` and seven deadline misses in the retry window.

This independent device run establishes that the A54 can refuse or cancel local generation when protecting production physics. It supports a resource-governor explanation for attempt 12's generic LLM failure, but does not identify attempt 12's exact governed status. The phone's launcher was restored after the diagnostic run; airplane mode remained off and Wi-Fi/mobile data on. No microphone was started for this follow-up.

The same run's admission checkpoint reported a conservative device profile, reduced governor mode, 768-token context, one inference thread, and thermal status `None`. Its generation checkpoints then showed physics `AtRisk`/`Exceeded` and real worker deadline misses. This particular run therefore should not be labeled a proven thermal-triggered cancellation; the measured limiting signal was production physics pressure even at the already reduced inference profile. Older A54 thermal findings remain separate evidence for their own runs.

## Thread-level profiling follow-up

Two more model-only RMA-135 runs used the same APK and exact selected model, without microphone input: `build/rma135-thread-profile-2026-10-12/` and `build/rma135-renice-experiment-2026-10-12/`. Both admitted the conservative, reduced profile with thermal status `None`, a 768-token context, and one inference thread. Both failed after eight post-recovery attempts with zero text events. The first recorded seven physics worker deadline misses during its retry window; the second recorded eight. In the second run, each attempted generation was cancelled with `PhysicsAtRisk` or `PhysicsBudgetExceeded` and a real deadline miss before any text event. This reproduces the production physics pressure independently of the spoken-turn harness.

The first run sampled per-thread `/proc` scheduling counters in `thread-samples.tsv`. The authoritative physics worker was observed at Linux nice `0`, as was the app's main Unity thread. That is a scheduling clue, not proof of the cause of a deadline miss: the samples are sparse and also show other active app threads. A temporary attempt to raise only the physics worker to nice `-8` through `run-as ... renice` was denied by Android (`Permission denied`), so the second run is **not** a priority-adjusted comparison and cannot be used to claim that changing priority helps. No production scheduling code, governor thresholds, physics timestep, selected model, or retry budget was changed.

The evidence supports treating the remaining spoken-turn blocker as A54 local-inference/physics coexistence until a controlled scheduling or profiling experiment demonstrates an improvement. The failed runs do not close RMA-135 or the full offline speech/TTS gate.

An isolated managed-priority experiment then set `Thread.Priority = AboveNormal` on the authoritative simulation worker in an Android-only build. The APK passed the release package audit (SHA-256 `cd65dce95961db28936394e95368b20a713c0f9d996e805349822aac59d2c5d9`). During the model-only run in `build/rma135-priority-experiment-2026-10-12/`, `/proc` still showed the worker at Linux priority `20`, nice `0`; the intended scheduling change did not reach the kernel. The run failed with zero text events and 12 worker deadline misses across its eight-attempt retry window. This is not evidence that a real Android thread-priority change would fail or help. The ineffective managed-priority source edit was reverted; no priority change is being kept in production code.

A separate native experiment lowered the Android inference worker to nice `5` at generation start, leaving the physics worker and governor unchanged. The APK passed the package audit (SHA-256 `3b97d20faf5e990cc5b12e5fdc50a8a2e6dc7e3954d9382fda0f7e7a4da75fd9`). In `build/rma135-lower-llm-experiment-2026-10-12/`, 82 `/proc` samples of the named inference worker all reported Linux priority `25`, nice `5`, so this scheduling change genuinely took effect. Nevertheless, the model-only RMA-135 run failed with zero text events and 10 physics deadline misses across its eight-attempt retry window. One A54 run is not a controlled statistical estimate, but it rules out treating a simple inference-worker nice adjustment as a demonstrated fix. The experimental native source and staged library were reverted after the run.
