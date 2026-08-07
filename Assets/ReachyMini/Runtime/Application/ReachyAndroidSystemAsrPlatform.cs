#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidSystemAsrPlatform : IAndroidSystemAsrPlatform
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.speech.ReachySystemAsrBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly object sync = new object();
        private AndroidJavaObject? bridge;
        private string? activeRecognitionRequestId;
        private bool disposed;
#endif

        public ReachyAndroidSystemAsrPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Android system ASR is available only in Android player builds.");
#endif
        }

        public async ValueTask<AndroidSystemAsrProbe> ProbeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "system-probe-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<ProbeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ProbeCallback(requestId, completion);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken));
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("probe", activity, requestId, callback);
            ProbeResult result = await completion.Task.ConfigureAwait(false);
            return new AndroidSystemAsrProbe(
                result.ApiLevel,
                result.HasMicrophonePermission,
                result.SystemRecognitionAvailable);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android system ASR is available only in Android player builds.");
#endif
        }

        public async IAsyncEnumerable<AndroidSystemAsrPlatformEvent> RecognizeAsync(
            string requestId,
            AsrOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException(
                    "Android system ASR request identity is required.",
                    nameof(requestId));
            }
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge;
            lock (sync)
            {
                ThrowIfDisposed();
                if (activeRecognitionRequestId != null)
                {
                    yield return Failure(
                        requestId,
                        AndroidSystemAsrFailureKind.Busy,
                        "recognizer_busy",
                        "The Android system SpeechRecognizer already has an active utterance; requests are not queued.");
                    yield break;
                }
                activeRecognitionRequestId = requestId;
                activeBridge = bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidSystemAsrPlatform));
            }

            using var queue = new RecognitionEventQueue(requestId);
            var callback = new RecognitionCallback(requestId, queue);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            activeBridge.Call("cancel", requestId);
                            queue.Publish(new AndroidSystemAsrPlatformEvent(
                                requestId,
                                AndroidSystemAsrPlatformEventKind.Cancelled));
                        }
                        catch (AndroidJavaException exception)
                        {
                            queue.PublishTerminalFailure(
                                AndroidSystemAsrFailureKind.ServiceFailure,
                                "java_bridge_cancel_failed",
                                "Cancelling Android system ASR failed with " +
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
                        options.LanguageTag,
                        options.RequestPartialResults,
                        callback);
                }
                catch (AndroidJavaException exception)
                {
                    queue.PublishTerminalFailure(
                        AndroidSystemAsrFailureKind.ServiceFailure,
                        "java_bridge_start_failed",
                        "Starting Android system ASR failed with " +
                            exception.GetType().Name + ".");
                }

                bool terminal = false;
                while (!terminal)
                {
                    AndroidSystemAsrPlatformEvent value =
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
                        "RMA122_ASR_CLEANUP cancel bridge failed: " +
                        exception.GetType().Name);
                }
                lock (sync)
                {
                    if (string.Equals(
                        activeRecognitionRequestId,
                        requestId,
                        StringComparison.Ordinal))
                    {
                        activeRecognitionRequestId = null;
                    }
                }
            }
#else
            await Task.Yield();
            yield return Failure(
                requestId,
                AndroidSystemAsrFailureKind.ServiceFailure,
                "android_player_required",
                "Android system ASR requires an Android player.");
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
                    return default;
                }
                disposed = true;
                value = bridge;
                bridge = null;
                activeRecognitionRequestId = null;
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

        private static AndroidSystemAsrPlatformEvent Failure(
            string requestId,
            AndroidSystemAsrFailureKind kind,
            string code,
            string diagnostic)
        {
            return new AndroidSystemAsrPlatformEvent(
                requestId,
                AndroidSystemAsrPlatformEventKind.Failed,
                failure: new AndroidSystemAsrPlatformFailure(
                    kind,
                    code,
                    diagnostic));
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject GetBridge()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                return bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidSystemAsrPlatform));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReachyAndroidSystemAsrPlatform));
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static AndroidSystemAsrFailureKind ParseFailureKind(string value)
        {
            return value switch
            {
                "permission_denied" => AndroidSystemAsrFailureKind.PermissionDenied,
                "audio_failure" => AndroidSystemAsrFailureKind.AudioFailure,
                "speech_timeout" => AndroidSystemAsrFailureKind.SpeechTimeout,
                "network_failure" => AndroidSystemAsrFailureKind.NetworkFailure,
                "network_timeout" => AndroidSystemAsrFailureKind.NetworkTimeout,
                "client_failure" => AndroidSystemAsrFailureKind.ClientFailure,
                "service_failure" => AndroidSystemAsrFailureKind.ServiceFailure,
                "recognizer_busy" => AndroidSystemAsrFailureKind.Busy,
                "too_many_requests" => AndroidSystemAsrFailureKind.TooManyRequests,
                "service_disconnected" => AndroidSystemAsrFailureKind.ServiceDisconnected,
                "language_not_supported" => AndroidSystemAsrFailureKind.LanguageNotSupported,
                "language_unavailable" => AndroidSystemAsrFailureKind.LanguageUnavailable,
                _ => AndroidSystemAsrFailureKind.Unknown,
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
                bool hasPermission,
                bool recognitionAvailable)
            {
                if (!string.Equals(
                    requestId,
                    callbackRequestId,
                    StringComparison.Ordinal))
                {
                    completion.TrySetException(
                        IdentityMismatch("probe"));
                    return;
                }
                completion.TrySetResult(new ProbeResult(
                    apiLevel,
                    hasPermission,
                    recognitionAvailable));
            }
        }

        private sealed class RecognitionCallback : AndroidJavaProxy
        {
            private readonly string requestId;
            private readonly RecognitionEventQueue queue;

            public RecognitionCallback(
                string requestId,
                RecognitionEventQueue queue)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.queue = queue;
            }

            public void onStarted(string callbackRequestId) =>
                Publish(callbackRequestId,
                    new AndroidSystemAsrPlatformEvent(
                        requestId,
                        AndroidSystemAsrPlatformEventKind.Started));

            public void onPartialResult(
                string callbackRequestId,
                string transcript) =>
                Publish(callbackRequestId,
                    new AndroidSystemAsrPlatformEvent(
                        requestId,
                        AndroidSystemAsrPlatformEventKind.PartialResult,
                        transcript));

            public void onFinalResult(
                string callbackRequestId,
                string transcript) =>
                Publish(callbackRequestId,
                    new AndroidSystemAsrPlatformEvent(
                        requestId,
                        AndroidSystemAsrPlatformEventKind.FinalResult,
                        transcript));

            public void onNoMatch(string callbackRequestId) =>
                Publish(callbackRequestId,
                    new AndroidSystemAsrPlatformEvent(
                        requestId,
                        AndroidSystemAsrPlatformEventKind.NoMatch));

            public void onCancelled(string callbackRequestId) =>
                Publish(callbackRequestId,
                    new AndroidSystemAsrPlatformEvent(
                        requestId,
                        AndroidSystemAsrPlatformEventKind.Cancelled));

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
                AndroidSystemAsrPlatformEvent value)
            {
                if (ValidateIdentity(callbackRequestId))
                {
                    queue.Publish(value);
                }
            }

            private bool ValidateIdentity(string callbackRequestId)
            {
                if (string.Equals(
                    requestId,
                    callbackRequestId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                queue.PublishTerminalFailure(
                    AndroidSystemAsrFailureKind.Unknown,
                    "callback_request_identity_mismatch",
                    "The Android system ASR Java callback returned a different request identifier.");
                return false;
            }
        }

        private sealed class ProbeResult
        {

            public ProbeResult(int apiLevel, bool permission, bool recognitionAvailable)
            {
                ApiLevel = apiLevel;
                HasMicrophonePermission = permission;
                SystemRecognitionAvailable = recognitionAvailable;
            }

            public int ApiLevel { get; }
            public bool HasMicrophonePermission { get; }
            public bool SystemRecognitionAvailable { get; }
        }

        private sealed class RecognitionEventQueue : IDisposable
        {
            private const int MaximumQueuedEvents = 128;
            private readonly object queueSync = new object();
            private readonly Queue<AndroidSystemAsrPlatformEvent> events =
                new Queue<AndroidSystemAsrPlatformEvent>();
            private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            private readonly string requestId;
            private bool terminal;
            private bool queueDisposed;

            public RecognitionEventQueue(string requestId)
            {
                this.requestId = requestId;
            }

            public void Publish(AndroidSystemAsrPlatformEvent value)
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
                            AndroidSystemAsrFailureKind.ServiceFailure,
                            "callback_queue_overflow",
                            "Android system ASR callback queue overflowed; no transcript event was silently dropped."));
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
                AndroidSystemAsrFailureKind kind,
                string code,
                string diagnostic) =>
                Publish(Failure(requestId, kind, code, diagnostic));

            public async ValueTask<AndroidSystemAsrPlatformEvent> ReadAsync()
            {
                await signal.WaitAsync().ConfigureAwait(false);
                lock (queueSync)
                {
                    if (events.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Android system ASR callback queue ended without a readable event.");
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
                "Android system ASR " + callbackKind +
                " callback returned a different request identifier.");
#endif
    }

    public static class ReachyAndroidSystemAsrProviderFactory
    {
        public static IAsrProvider Create(
            string instanceId,
            string languageTag,
            TimeSpan maximumUtteranceDuration)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSystemAsrProvider(
                new ReachyAndroidSystemAsrPlatform(),
                instanceId,
                languageTag,
                maximumUtteranceDuration);
#else
            throw new PlatformNotSupportedException(
                "Android system ASR is available only in Android player builds.");
#endif
        }
    }
}
