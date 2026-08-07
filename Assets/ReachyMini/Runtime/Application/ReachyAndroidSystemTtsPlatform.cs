#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidSystemTtsPlatform : IAndroidSystemTtsPlatform
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.speech.ReachySystemTtsBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly object sync = new object();
        private AndroidJavaObject? bridge;
        private string? activeSpeechRequestId;
        private bool disposed;
#endif

        public ReachyAndroidSystemTtsPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Android system/network TTS is available only in Android player builds.");
#endif
        }

        public async ValueTask<AndroidSystemTtsProbe> ProbeAsync(
            string languageTag,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException(
                    "Android system TTS probe language is required.",
                    nameof(languageTag));
            }
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "system-tts-probe-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<ProbeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ProbeCallback(requestId, completion);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    completion.TrySetCanceled(cancellationToken);
                    TryCancel(activeBridge, requestId, "RMA124_TTS_PROBE_CANCEL");
                });
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("probe", activity, requestId, languageTag, callback);
            ProbeResult result = await completion.Task.ConfigureAwait(false);
            return new AndroidSystemTtsProbe(
                result.ApiLevel,
                result.EngineInitialized,
                result.MatchingVoiceCount,
                result.MatchingOfflineVoiceCount,
                result.MatchingNetworkVoiceCount,
                result.MaximumInputCharacters,
                result.Diagnostic);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android system/network TTS is available only in Android player builds.");
#endif
        }

        public async ValueTask<IReadOnlyList<AndroidSystemTtsPlatformVoice>> GetVoicesAsync(
            string languageTag,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException(
                    "Android system TTS voice language is required.",
                    nameof(languageTag));
            }
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "system-tts-voices-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<IReadOnlyList<AndroidSystemTtsPlatformVoice>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new VoiceCallback(requestId, completion);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    completion.TrySetCanceled(cancellationToken);
                    TryCancel(activeBridge, requestId, "RMA124_TTS_VOICE_CANCEL");
                });
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("listVoices", activity, requestId, languageTag, callback);
            return await completion.Task.ConfigureAwait(false);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android system/network TTS is available only in Android player builds.");
#endif
        }

        public async IAsyncEnumerable<AndroidSystemTtsPlatformEvent> SpeakAsync(
            string requestId,
            string text,
            string languageTag,
            string voiceId,
            bool networkVoiceApproved,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequireText(requestId, nameof(requestId));
            RequireText(text, nameof(text));
            RequireText(languageTag, nameof(languageTag));
            RequireText(voiceId, nameof(voiceId));
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
                        nameof(ReachyAndroidSystemTtsPlatform));
                }
            }
            if (busy)
            {
                yield return Failure(
                    requestId,
                    AndroidSystemTtsFailureKind.Busy,
                    "tts_busy",
                    "Android system TTS already has an active utterance; requests are not queued.");
                yield break;
            }

            AndroidJavaObject activeBridge = selectedBridge ??
                throw new InvalidOperationException(
                    "Android system TTS operation acquired without a Java bridge.");
            using var queue = new SpeechEventQueue(requestId);
            var callback = new SpeechCallback(requestId, queue);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () =>
                {
                    try
                    {
                        activeBridge.Call("cancel", requestId);
                        queue.Publish(new AndroidSystemTtsPlatformEvent(
                            requestId,
                            AndroidSystemTtsPlatformEventKind.Cancelled));
                    }
                    catch (AndroidJavaException exception)
                    {
                        queue.PublishTerminalFailure(
                            AndroidSystemTtsFailureKind.ServiceFailure,
                            "java_bridge_cancel_failed",
                            "Cancelling Android system TTS failed with " +
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
                        networkVoiceApproved,
                        callback);
                }
                catch (AndroidJavaException exception)
                {
                    queue.PublishTerminalFailure(
                        AndroidSystemTtsFailureKind.ServiceFailure,
                        "java_bridge_start_failed",
                        "Starting Android system TTS failed with " +
                            exception.GetType().Name + ".");
                }

                bool terminal = false;
                while (!terminal)
                {
                    AndroidSystemTtsPlatformEvent value =
                        await queue.ReadAsync().ConfigureAwait(false);
                    terminal = value.IsTerminal;
                    yield return value;
                }
            }
            finally
            {
                TryCancel(activeBridge, requestId, "RMA124_TTS_CLEANUP");
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
                AndroidSystemTtsFailureKind.ServiceFailure,
                "android_player_required",
                "Android system/network TTS requires an Android player.");
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

        private static AndroidSystemTtsPlatformEvent Failure(
            string requestId,
            AndroidSystemTtsFailureKind kind,
            string code,
            string diagnostic) =>
            new AndroidSystemTtsPlatformEvent(
                requestId,
                AndroidSystemTtsPlatformEventKind.Failed,
                new AndroidSystemTtsPlatformFailure(kind, code, diagnostic));

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Android system TTS text is required.", name);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject GetBridge()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                return bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidSystemTtsPlatform));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReachyAndroidSystemTtsPlatform));
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static void TryCancel(
            AndroidJavaObject activeBridge,
            string requestId,
            string context)
        {
            try
            {
                activeBridge.Call("cancel", requestId);
            }
            catch (AndroidJavaException exception)
            {
                Debug.LogError(context + " bridge failed: " + exception.GetType().Name);
            }
        }

        private static SpeechNetworkRequirement ParseNetworkRequirement(string value) =>
            value switch
            {
                "none" => SpeechNetworkRequirement.None,
                "required" => SpeechNetworkRequirement.Required,
                _ => throw new InvalidOperationException(
                    "Android system TTS returned an unknown voice network requirement."),
            };

        private static AndroidSystemTtsFailureKind ParseFailureKind(string value) =>
            value switch
            {
                "engine_unavailable" => AndroidSystemTtsFailureKind.EngineUnavailable,
                "engine_initialization_failed" => AndroidSystemTtsFailureKind.EngineUnavailable,
                "missing_voice_data" => AndroidSystemTtsFailureKind.MissingVoiceData,
                "network_failure" => AndroidSystemTtsFailureKind.NetworkFailure,
                "network_timeout" => AndroidSystemTtsFailureKind.NetworkTimeout,
                "output_failure" => AndroidSystemTtsFailureKind.OutputFailure,
                "service_failure" => AndroidSystemTtsFailureKind.ServiceFailure,
                "synthesis_failure" => AndroidSystemTtsFailureKind.SynthesisFailure,
                "invalid_request" => AndroidSystemTtsFailureKind.InvalidRequest,
                "tts_busy" => AndroidSystemTtsFailureKind.Busy,
                "voice_unavailable" => AndroidSystemTtsFailureKind.VoiceUnavailable,
                "voice_rejected" => AndroidSystemTtsFailureKind.VoiceRejected,
                "network_voice_not_approved" => AndroidSystemTtsFailureKind.VoiceRejected,
                "voice_approval_mismatch" => AndroidSystemTtsFailureKind.VoiceRejected,
                _ => AndroidSystemTtsFailureKind.Unknown,
            };

        private static InvalidOperationException IdentityMismatch(string operation) =>
            new InvalidOperationException(
                "Android system TTS " + operation +
                " callback request identity did not match the active request.");

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
                int matchingVoiceCount,
                int matchingOfflineVoiceCount,
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
                    matchingVoiceCount,
                    matchingOfflineVoiceCount,
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
                    0,
                    0,
                    0,
                    AndroidSystemTtsProvider.DefaultMaximumInputCharacters,
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
            private readonly TaskCompletionSource<IReadOnlyList<AndroidSystemTtsPlatformVoice>>
                completion;
            private readonly List<AndroidSystemTtsPlatformVoice> voices =
                new List<AndroidSystemTtsPlatformVoice>();
            private bool started;
            private bool terminal;

            public VoiceCallback(
                string requestId,
                TaskCompletionSource<IReadOnlyList<AndroidSystemTtsPlatformVoice>> completion)
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
                            "Android system TTS voice callback arrived before enumeration started."));
                        return;
                    }
                    try
                    {
                        voices.Add(new AndroidSystemTtsPlatformVoice(
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
                            "Android system TTS voice enumeration completed before it started."));
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
                        "Android system TTS voice enumeration failed: " +
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
                Publish(callbackRequestId, AndroidSystemTtsPlatformEventKind.Started);

            public void onDone(string callbackRequestId) =>
                Publish(callbackRequestId, AndroidSystemTtsPlatformEventKind.Completed);

            public void onStopped(string callbackRequestId) =>
                Publish(callbackRequestId, AndroidSystemTtsPlatformEventKind.Cancelled);

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
                AndroidSystemTtsPlatformEventKind kind)
            {
                if (!ValidateIdentity(callbackRequestId))
                {
                    return;
                }
                queue.Publish(new AndroidSystemTtsPlatformEvent(requestId, kind));
            }

            private bool ValidateIdentity(string callbackRequestId)
            {
                if (string.Equals(requestId, callbackRequestId, StringComparison.Ordinal))
                {
                    return true;
                }
                queue.PublishTerminalFailure(
                    AndroidSystemTtsFailureKind.VoiceRejected,
                    "callback_request_identity_mismatch",
                    "Android system TTS callback request identity did not match the active request.");
                return false;
            }
        }

        private sealed class SpeechEventQueue : IDisposable
        {
            private const int MaximumQueuedEvents = 32;
            private readonly object queueSync = new object();
            private readonly string requestId;
            private readonly Queue<AndroidSystemTtsPlatformEvent> values =
                new Queue<AndroidSystemTtsPlatformEvent>();
            private readonly SemaphoreSlim available = new SemaphoreSlim(0);
            private bool terminalPublished;
            private bool disposedQueue;

            public SpeechEventQueue(string requestId)
            {
                this.requestId = requestId;
            }

            public void Publish(AndroidSystemTtsPlatformEvent value)
            {
                lock (queueSync)
                {
                    if (disposedQueue || terminalPublished)
                    {
                        return;
                    }
                    if (values.Count >= MaximumQueuedEvents)
                    {
                        values.Clear();
                        values.Enqueue(Failure(
                            requestId,
                            AndroidSystemTtsFailureKind.ServiceFailure,
                            "callback_queue_overflow",
                            "Android system TTS callback queue overflowed; the utterance failed visibly."));
                        terminalPublished = true;
                        available.Release();
                        return;
                    }
                    values.Enqueue(value);
                    if (value.IsTerminal)
                    {
                        terminalPublished = true;
                    }
                    available.Release();
                }
            }

            public void PublishTerminalFailure(
                AndroidSystemTtsFailureKind kind,
                string code,
                string diagnostic) =>
                Publish(Failure(requestId, kind, code, diagnostic));

            public async Task<AndroidSystemTtsPlatformEvent> ReadAsync()
            {
                await available.WaitAsync().ConfigureAwait(false);
                lock (queueSync)
                {
                    if (values.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Android system TTS callback queue signaled without an event.");
                    }
                    return values.Dequeue();
                }
            }

            public void Dispose()
            {
                lock (queueSync)
                {
                    if (disposedQueue)
                    {
                        return;
                    }
                    disposedQueue = true;
                    values.Clear();
                }
                available.Dispose();
            }
        }

        private sealed class ProbeResult
        {
            public ProbeResult(
                int apiLevel,
                bool engineInitialized,
                int matchingVoiceCount,
                int matchingOfflineVoiceCount,
                int matchingNetworkVoiceCount,
                int maximumInputCharacters,
                string diagnostic)
            {
                ApiLevel = apiLevel;
                EngineInitialized = engineInitialized;
                MatchingVoiceCount = matchingVoiceCount;
                MatchingOfflineVoiceCount = matchingOfflineVoiceCount;
                MatchingNetworkVoiceCount = matchingNetworkVoiceCount;
                MaximumInputCharacters = maximumInputCharacters;
                Diagnostic = diagnostic;
            }

            public int ApiLevel { get; }
            public bool EngineInitialized { get; }
            public int MatchingVoiceCount { get; }
            public int MatchingOfflineVoiceCount { get; }
            public int MatchingNetworkVoiceCount { get; }
            public int MaximumInputCharacters { get; }
            public string Diagnostic { get; }
        }
#endif
    }
}
