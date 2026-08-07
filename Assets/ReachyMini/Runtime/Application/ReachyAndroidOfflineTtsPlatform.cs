#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidOfflineTtsPlatform : IAndroidOfflineTtsPlatform
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.speech.ReachyOfflineTtsBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly object sync = new object();
        private AndroidJavaObject? bridge;
        private string? activeSpeechRequestId;
        private bool disposed;
#endif

        public ReachyAndroidOfflineTtsPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Android offline TTS is available only in Android player builds.");
#endif
        }

        public async ValueTask<AndroidOfflineTtsProbe> ProbeAsync(
            string languageTag,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException(
                    "Android offline TTS probe language is required.",
                    nameof(languageTag));
            }
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "offline-tts-probe-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<ProbeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ProbeCallback(requestId, completion);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    completion.TrySetCanceled(cancellationToken);
                    try
                    {
                        activeBridge.Call("cancel", requestId);
                    }
                    catch (AndroidJavaException exception)
                    {
                        Debug.LogError(
                            "RMA123_TTS_PROBE_CANCEL bridge failed: " +
                            exception.GetType().Name);
                    }
                });
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("probe", activity, requestId, languageTag, callback);
            ProbeResult result = await completion.Task.ConfigureAwait(false);
            return new AndroidOfflineTtsProbe(
                result.ApiLevel,
                result.EngineInitialized,
                result.LanguageStatus,
                result.MatchingOfflineVoiceCount,
                result.InstalledOfflineVoiceCount,
                result.MatchingNetworkVoiceCount,
                result.MaximumInputCharacters,
                result.Diagnostic);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android offline TTS is available only in Android player builds.");
#endif
        }

        public async ValueTask<IReadOnlyList<AndroidOfflineTtsPlatformVoice>> GetVoicesAsync(
            string languageTag,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException(
                    "Android offline TTS voice language is required.",
                    nameof(languageTag));
            }
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "offline-tts-voices-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<IReadOnlyList<AndroidOfflineTtsPlatformVoice>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new VoiceCallback(requestId, completion);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    completion.TrySetCanceled(cancellationToken);
                    try
                    {
                        activeBridge.Call("cancel", requestId);
                    }
                    catch (AndroidJavaException exception)
                    {
                        Debug.LogError(
                            "RMA123_TTS_VOICE_CANCEL bridge failed: " +
                            exception.GetType().Name);
                    }
                });
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("listVoices", activity, requestId, languageTag, callback);
            return await completion.Task.ConfigureAwait(false);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android offline TTS is available only in Android player builds.");
#endif
        }

        public async IAsyncEnumerable<AndroidOfflineTtsPlatformEvent> SpeakAsync(
            string requestId,
            string text,
            string languageTag,
            string voiceId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException(
                    "Android offline TTS request identity is required.",
                    nameof(requestId));
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(
                    "Android offline TTS text is required.",
                    nameof(text));
            }
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException(
                    "Android offline TTS language is required.",
                    nameof(languageTag));
            }
            if (string.IsNullOrWhiteSpace(voiceId))
            {
                throw new ArgumentException(
                    "Android offline TTS voice identity is required.",
                    nameof(voiceId));
            }
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject? selectedBridge = null;
            bool busy;
            lock (sync)
            {
                ThrowIfDisposed();
                busy = activeSpeechRequestId != null;
                if (!busy)
                {
                    activeSpeechRequestId = requestId;
                    selectedBridge = bridge ?? throw new ObjectDisposedException(
                        nameof(ReachyAndroidOfflineTtsPlatform));
                }
            }
            if (busy)
            {
                yield return Failure(
                    requestId,
                    AndroidOfflineTtsFailureKind.Busy,
                    "tts_busy",
                    "Android offline TTS already has an active utterance; requests are not queued.");
                yield break;
            }
            AndroidJavaObject activeBridge = selectedBridge ??
                throw new InvalidOperationException(
                    "Android offline TTS operation acquired without a Java bridge.");

            using var queue = new SpeechEventQueue(requestId);
            var callback = new SpeechCallback(requestId, queue);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    try
                    {
                        activeBridge.Call("cancel", requestId);
                        queue.Publish(new AndroidOfflineTtsPlatformEvent(
                            requestId,
                            AndroidOfflineTtsPlatformEventKind.Cancelled));
                    }
                    catch (AndroidJavaException exception)
                    {
                        queue.PublishTerminalFailure(
                            AndroidOfflineTtsFailureKind.ServiceFailure,
                            "java_bridge_cancel_failed",
                            "Cancelling Android offline TTS failed with " +
                                exception.GetType().Name + ".");
                    }
                });

            try
            {
                using AndroidJavaObject activity = GetCurrentActivity();
                try
                {
                    activeBridge.Call(
                        "start",
                        activity,
                        requestId,
                        text,
                        languageTag,
                        voiceId,
                        callback);
                }
                catch (AndroidJavaException exception)
                {
                    queue.PublishTerminalFailure(
                        AndroidOfflineTtsFailureKind.ServiceFailure,
                        "java_bridge_start_failed",
                        "Starting Android offline TTS failed with " +
                            exception.GetType().Name + ".");
                }

                bool terminal = false;
                while (!terminal)
                {
                    AndroidOfflineTtsPlatformEvent value =
                        await queue.ReadAsync().ConfigureAwait(false);
                    terminal = value.IsTerminal;
                    yield return value;
                }
            }
            finally
            {
                try
                {
                    activeBridge.Call("cancel", requestId);
                }
                catch (AndroidJavaException exception)
                {
                    Debug.LogError(
                        "RMA123_TTS_CLEANUP cancel bridge failed: " +
                        exception.GetType().Name);
                }
                lock (sync)
                {
                    if (string.Equals(
                        activeSpeechRequestId,
                        requestId,
                        StringComparison.Ordinal))
                    {
                        activeSpeechRequestId = null;
                    }
                }
            }
#else
            await Task.Yield();
            yield return Failure(
                requestId,
                AndroidOfflineTtsFailureKind.ServiceFailure,
                "android_player_required",
                "Android offline TTS requires an Android player.");
#endif
        }

        public ValueTask DisposeAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject? value;
            lock (sync)
            {
                if (disposed)
                {
                    GC.SuppressFinalize(this);
                    return default;
                }
                disposed = true;
                value = bridge;
                bridge = null;
                activeSpeechRequestId = null;
            }
            if (value != null)
            {
                try
                {
                    value.Call("close");
                }
                finally
                {
                    value.Dispose();
                }
            }
#endif
            GC.SuppressFinalize(this);
            return default;
        }

        private static AndroidOfflineTtsPlatformEvent Failure(
            string requestId,
            AndroidOfflineTtsFailureKind kind,
            string code,
            string diagnostic)
        {
            return new AndroidOfflineTtsPlatformEvent(
                requestId,
                AndroidOfflineTtsPlatformEventKind.Failed,
                new AndroidOfflineTtsPlatformFailure(kind, code, diagnostic));
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject GetBridge()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                return bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidOfflineTtsPlatform));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReachyAndroidOfflineTtsPlatform));
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static AndroidOfflineTtsLanguageStatus ParseLanguageStatus(string value)
        {
            return value switch
            {
                "exact_available" => AndroidOfflineTtsLanguageStatus.ExactAvailable,
                "country_available" => AndroidOfflineTtsLanguageStatus.CountryAvailable,
                "language_available" => AndroidOfflineTtsLanguageStatus.LanguageAvailable,
                "missing_data" => AndroidOfflineTtsLanguageStatus.MissingData,
                "not_supported" => AndroidOfflineTtsLanguageStatus.NotSupported,
                _ => AndroidOfflineTtsLanguageStatus.Unknown,
            };
        }

        private static SpeechNetworkRequirement ParseNetworkRequirement(string value)
        {
            return value switch
            {
                "none" => SpeechNetworkRequirement.None,
                "provider_controlled" => SpeechNetworkRequirement.ProviderControlled,
                "required" => SpeechNetworkRequirement.Required,
                _ => throw new InvalidOperationException(
                    "Android TTS returned an unknown voice network requirement."),
            };
        }

        private static AndroidOfflineTtsFailureKind ParseFailureKind(string value)
        {
            return value switch
            {
                "engine_unavailable" => AndroidOfflineTtsFailureKind.EngineUnavailable,
                "engine_initialization_failed" => AndroidOfflineTtsFailureKind.EngineUnavailable,
                "missing_voice_data" => AndroidOfflineTtsFailureKind.MissingVoiceData,
                "network_failure" => AndroidOfflineTtsFailureKind.NetworkFailure,
                "network_timeout" => AndroidOfflineTtsFailureKind.NetworkTimeout,
                "output_failure" => AndroidOfflineTtsFailureKind.OutputFailure,
                "service_failure" => AndroidOfflineTtsFailureKind.ServiceFailure,
                "synthesis_failure" => AndroidOfflineTtsFailureKind.SynthesisFailure,
                "invalid_request" => AndroidOfflineTtsFailureKind.InvalidRequest,
                "tts_busy" => AndroidOfflineTtsFailureKind.Busy,
                "voice_unavailable" => AndroidOfflineTtsFailureKind.VoiceUnavailable,
                "voice_rejected" => AndroidOfflineTtsFailureKind.VoiceRejected,
                _ => AndroidOfflineTtsFailureKind.Unknown,
            };
        }

        private sealed class ProbeCallback : AndroidJavaProxy
        {
            private readonly string requestId;
            private readonly TaskCompletionSource<ProbeResult> completion;

            public ProbeCallback(
                string requestId,
                TaskCompletionSource<ProbeResult> completion)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.completion = completion;
            }

            public void onProbe(
                string callbackRequestId,
                int apiLevel,
                bool engineInitialized,
                string languageStatus,
                int matchingOfflineVoiceCount,
                int installedOfflineVoiceCount,
                int matchingNetworkVoiceCount,
                int maximumInputCharacters,
                string diagnostic)
            {
                if (!ValidateIdentity(callbackRequestId))
                {
                    return;
                }
                completion.TrySetResult(new ProbeResult(
                    apiLevel,
                    engineInitialized,
                    ParseLanguageStatus(languageStatus),
                    matchingOfflineVoiceCount,
                    installedOfflineVoiceCount,
                    matchingNetworkVoiceCount,
                    maximumInputCharacters,
                    diagnostic));
            }

            public void onFailure(
                string callbackRequestId,
                string code,
                string diagnostic)
            {
                if (!ValidateIdentity(callbackRequestId))
                {
                    return;
                }
                completion.TrySetResult(new ProbeResult(
                    0,
                    false,
                    AndroidOfflineTtsLanguageStatus.Unknown,
                    0,
                    0,
                    0,
                    AndroidOfflineTtsProvider.DefaultMaximumInputCharacters,
                    diagnostic + " (" + code + ")"));
            }

            private bool ValidateIdentity(string callbackRequestId)
            {
                if (string.Equals(requestId, callbackRequestId, StringComparison.Ordinal))
                {
                    return true;
                }
                completion.TrySetException(IdentityMismatch("probe"));
                return false;
            }
        }

        private sealed class VoiceCallback : AndroidJavaProxy
        {
            private readonly object voiceSync = new object();
            private readonly string requestId;
            private readonly TaskCompletionSource<IReadOnlyList<AndroidOfflineTtsPlatformVoice>>
                completion;
            private readonly List<AndroidOfflineTtsPlatformVoice> voices =
                new List<AndroidOfflineTtsPlatformVoice>();
            private bool started;
            private bool terminal;

            public VoiceCallback(
                string requestId,
                TaskCompletionSource<IReadOnlyList<AndroidOfflineTtsPlatformVoice>> completion)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.completion = completion;
            }

            public void onVoicesStarted(string callbackRequestId)
            {
                lock (voiceSync)
                {
                    if (!ValidateIdentityLocked(callbackRequestId) || terminal)
                    {
                        return;
                    }
                    started = true;
                }
            }

            public void onVoice(
                string callbackRequestId,
                string voiceId,
                string displayName,
                string languageTag,
                string networkRequirement,
                bool installed)
            {
                lock (voiceSync)
                {
                    if (!ValidateIdentityLocked(callbackRequestId) || terminal)
                    {
                        return;
                    }
                    if (!started)
                    {
                        terminal = true;
                        completion.TrySetException(new InvalidOperationException(
                            "Android offline TTS voice callback arrived before voice enumeration started."));
                        return;
                    }
                    try
                    {
                        voices.Add(new AndroidOfflineTtsPlatformVoice(
                            voiceId,
                            displayName,
                            languageTag,
                            ParseNetworkRequirement(networkRequirement),
                            installed));
                    }
                    catch (Exception exception)
                    {
                        terminal = true;
                        completion.TrySetException(exception);
                    }
                }
            }

            public void onVoicesCompleted(string callbackRequestId)
            {
                lock (voiceSync)
                {
                    if (!ValidateIdentityLocked(callbackRequestId) || terminal)
                    {
                        return;
                    }
                    if (!started)
                    {
                        terminal = true;
                        completion.TrySetException(new InvalidOperationException(
                            "Android offline TTS voice enumeration completed before it started."));
                        return;
                    }
                    terminal = true;
                    completion.TrySetResult(voices.AsReadOnly());
                }
            }

            public void onFailure(
                string callbackRequestId,
                string code,
                string diagnostic)
            {
                lock (voiceSync)
                {
                    if (!ValidateIdentityLocked(callbackRequestId) || terminal)
                    {
                        return;
                    }
                    terminal = true;
                    completion.TrySetException(new InvalidOperationException(
                        "Android offline TTS voice enumeration failed: " +
                        code + ": " + diagnostic));
                }
            }

            private bool ValidateIdentityLocked(string callbackRequestId)
            {
                if (string.Equals(requestId, callbackRequestId, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!terminal)
                {
                    terminal = true;
                    completion.TrySetException(IdentityMismatch("voice"));
                }
                return false;
            }
        }

        private sealed class SpeechCallback : AndroidJavaProxy
        {
            private readonly string requestId;
            private readonly SpeechEventQueue queue;

            public SpeechCallback(string requestId, SpeechEventQueue queue)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.queue = queue;
            }

            public void onStarted(string callbackRequestId) =>
                Publish(
                    callbackRequestId,
                    new AndroidOfflineTtsPlatformEvent(
                        requestId,
                        AndroidOfflineTtsPlatformEventKind.Started));

            public void onDone(string callbackRequestId) =>
                Publish(
                    callbackRequestId,
                    new AndroidOfflineTtsPlatformEvent(
                        requestId,
                        AndroidOfflineTtsPlatformEventKind.Completed));

            public void onStopped(string callbackRequestId) =>
                Publish(
                    callbackRequestId,
                    new AndroidOfflineTtsPlatformEvent(
                        requestId,
                        AndroidOfflineTtsPlatformEventKind.Cancelled));

            public void onFailure(
                string callbackRequestId,
                string code,
                string diagnostic)
            {
                if (!ValidateIdentity(callbackRequestId))
                {
                    return;
                }
                queue.PublishTerminalFailure(
                    ParseFailureKind(code),
                    code,
                    diagnostic);
            }

            private void Publish(
                string callbackRequestId,
                AndroidOfflineTtsPlatformEvent value)
            {
                if (ValidateIdentity(callbackRequestId))
                {
                    queue.Publish(value);
                }
            }

            private bool ValidateIdentity(string callbackRequestId)
            {
                if (string.Equals(requestId, callbackRequestId, StringComparison.Ordinal))
                {
                    return true;
                }
                queue.PublishTerminalFailure(
                    AndroidOfflineTtsFailureKind.Unknown,
                    "callback_request_identity_mismatch",
                    "Android offline TTS Java callback returned a different request identifier.");
                return false;
            }
        }

        private sealed class ProbeResult
        {
            public ProbeResult(
                int apiLevel,
                bool engineInitialized,
                AndroidOfflineTtsLanguageStatus languageStatus,
                int matchingOfflineVoiceCount,
                int installedOfflineVoiceCount,
                int matchingNetworkVoiceCount,
                int maximumInputCharacters,
                string diagnostic)
            {
                ApiLevel = apiLevel;
                EngineInitialized = engineInitialized;
                LanguageStatus = languageStatus;
                MatchingOfflineVoiceCount = matchingOfflineVoiceCount;
                InstalledOfflineVoiceCount = installedOfflineVoiceCount;
                MatchingNetworkVoiceCount = matchingNetworkVoiceCount;
                MaximumInputCharacters = maximumInputCharacters;
                Diagnostic = diagnostic;
            }

            public int ApiLevel { get; }
            public bool EngineInitialized { get; }
            public AndroidOfflineTtsLanguageStatus LanguageStatus { get; }
            public int MatchingOfflineVoiceCount { get; }
            public int InstalledOfflineVoiceCount { get; }
            public int MatchingNetworkVoiceCount { get; }
            public int MaximumInputCharacters { get; }
            public string Diagnostic { get; }
        }

        private sealed class SpeechEventQueue : IDisposable
        {
            private const int MaximumQueuedEvents = 32;
            private readonly object queueSync = new object();
            private readonly Queue<AndroidOfflineTtsPlatformEvent> events =
                new Queue<AndroidOfflineTtsPlatformEvent>();
            private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            private readonly string requestId;
            private bool terminal;
            private bool queueDisposed;

            public SpeechEventQueue(string requestId)
            {
                this.requestId = requestId;
            }

            public void Publish(AndroidOfflineTtsPlatformEvent value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                lock (queueSync)
                {
                    if (terminal || queueDisposed)
                    {
                        return;
                    }
                    if (events.Count >= MaximumQueuedEvents)
                    {
                        while (signal.Wait(0))
                        {
                        }
                        events.Clear();
                        events.Enqueue(Failure(
                            requestId,
                            AndroidOfflineTtsFailureKind.ServiceFailure,
                            "callback_queue_overflow",
                            "Android offline TTS callback queue overflowed; no lifecycle event was silently dropped."));
                        terminal = true;
                        signal.Release();
                        return;
                    }
                    events.Enqueue(value);
                    terminal = value.IsTerminal;
                    signal.Release();
                }
            }

            public void PublishTerminalFailure(
                AndroidOfflineTtsFailureKind kind,
                string code,
                string diagnostic) =>
                Publish(Failure(requestId, kind, code, diagnostic));

            public async ValueTask<AndroidOfflineTtsPlatformEvent> ReadAsync()
            {
                await signal.WaitAsync().ConfigureAwait(false);
                lock (queueSync)
                {
                    if (events.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Android offline TTS callback queue ended without a readable event.");
                    }
                    return events.Dequeue();
                }
            }

            public void Dispose()
            {
                lock (queueSync)
                {
                    if (queueDisposed)
                    {
                        return;
                    }
                    queueDisposed = true;
                    events.Clear();
                    terminal = true;
                }
                signal.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private static InvalidOperationException IdentityMismatch(string callbackKind) =>
            new InvalidOperationException(
                "Android offline TTS " + callbackKind +
                " callback returned a different request identifier.");
#endif
    }

    public static class ReachyAndroidOfflineTtsProviderFactory
    {
        public static ITtsProvider Create(string instanceId, string languageTag)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidOfflineTtsProvider(
                new ReachyAndroidOfflineTtsPlatform(),
                instanceId,
                languageTag);
#else
            throw new PlatformNotSupportedException(
                "Android offline TTS provider creation requires an Android player build.");
#endif
        }
    }
}
