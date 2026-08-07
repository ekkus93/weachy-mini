#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidOnDeviceAsrPlatform : IAndroidOnDeviceAsrPlatform
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.speech.ReachyOnDeviceAsrBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly object sync = new object();
        private AndroidJavaObject? bridge;
        private string? activeRecognitionRequestId;
        private bool disposed;
#endif

        public ReachyAndroidOnDeviceAsrPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Explicit Android on-device ASR is available only in Android player builds.");
#endif
        }

        public async ValueTask<AndroidOnDeviceAsrProbe> ProbeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "probe-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<ProbeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ProbeCallback(requestId, completion);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken));
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call("probe", activity, requestId, callback);
            ProbeResult result = await completion.Task.ConfigureAwait(false);
            return new AndroidOnDeviceAsrProbe(
                result.ApiLevel,
                result.HasMicrophonePermission,
                result.ExplicitOnDeviceRecognitionAvailable,
                result.RecognitionSupportCheckAvailable);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Explicit Android on-device ASR is available only in Android player builds.");
#endif
        }

        public async ValueTask<AndroidOnDeviceAsrSupportResult> CheckSupportAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            string requestId = "support-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<SupportResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new SupportCallback(requestId, completion);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            activeBridge.Call("cancel", requestId);
                            completion.TrySetCanceled(cancellationToken);
                        }
                        catch (AndroidJavaException exception)
                        {
                            completion.TrySetException(
                                new InvalidOperationException(
                                    "Cancelling Android on-device ASR support preflight failed with " +
                                        exception.GetType().Name + ".",
                                    exception));
                        }
                    });
            using AndroidJavaObject activity = GetCurrentActivity();
            activeBridge.Call(
                "checkSupport",
                activity,
                requestId,
                options.LanguageTag,
                callback);
            SupportResult result = await completion.Task.ConfigureAwait(false);
            return new AndroidOnDeviceAsrSupportResult(
                ParseSupportState(result.State),
                result.Diagnostic);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Explicit Android on-device ASR is available only in Android player builds.");
#endif
        }

        public async IAsyncEnumerable<AndroidOnDeviceAsrPlatformEvent> RecognizeAsync(
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
                    "Android on-device ASR request identity is required.",
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
                        AndroidOnDeviceAsrFailureKind.Busy,
                        "recognizer_busy",
                        "The explicit on-device recognizer already has an active utterance; requests are not queued.");
                    yield break;
                }
                activeRecognitionRequestId = requestId;
                activeBridge = bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidOnDeviceAsrPlatform));
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
                            queue.Publish(new AndroidOnDeviceAsrPlatformEvent(
                                requestId,
                                AndroidOnDeviceAsrPlatformEventKind.Cancelled));
                        }
                        catch (AndroidJavaException exception)
                        {
                            queue.PublishTerminalFailure(
                                AndroidOnDeviceAsrFailureKind.ServiceFailure,
                                "java_bridge_cancel_failed",
                                "Cancelling explicit Android on-device ASR failed with " +
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
                        AndroidOnDeviceAsrFailureKind.ServiceFailure,
                        "java_bridge_start_failed",
                        "Starting explicit Android on-device ASR failed with " +
                            exception.GetType().Name + ".");
                }

                bool terminal = false;
                while (!terminal)
                {
                    AndroidOnDeviceAsrPlatformEvent value =
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
                        "RMA121_ASR_CLEANUP cancel bridge failed: " +
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
                AndroidOnDeviceAsrFailureKind.ServiceFailure,
                "android_player_required",
                "Explicit Android on-device ASR requires an Android player.");
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

        private static AndroidOnDeviceAsrPlatformEvent Failure(
            string requestId,
            AndroidOnDeviceAsrFailureKind kind,
            string code,
            string diagnostic)
        {
            return new AndroidOnDeviceAsrPlatformEvent(
                requestId,
                AndroidOnDeviceAsrPlatformEventKind.Failed,
                failure: new AndroidOnDeviceAsrPlatformFailure(
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
                    nameof(ReachyAndroidOnDeviceAsrPlatform));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidOnDeviceAsrPlatform));
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static AndroidOnDeviceAsrSupportState ParseSupportState(
            string value)
        {
            return value switch
            {
                "installed" => AndroidOnDeviceAsrSupportState.Installed,
                "preflight_unavailable" =>
                    AndroidOnDeviceAsrSupportState.PreflightUnavailable,
                "model_download_required" =>
                    AndroidOnDeviceAsrSupportState.ModelDownloadRequired,
                "model_download_pending" =>
                    AndroidOnDeviceAsrSupportState.ModelDownloadPending,
                "unsupported_language" =>
                    AndroidOnDeviceAsrSupportState.UnsupportedLanguage,
                "faulted" => AndroidOnDeviceAsrSupportState.Faulted,
                _ => AndroidOnDeviceAsrSupportState.Faulted,
            };
        }

        private static AndroidOnDeviceAsrFailureKind ParseFailureKind(
            string value)
        {
            return value switch
            {
                "permission_denied" => AndroidOnDeviceAsrFailureKind.PermissionDenied,
                "audio_failure" => AndroidOnDeviceAsrFailureKind.AudioFailure,
                "speech_timeout" => AndroidOnDeviceAsrFailureKind.Timeout,
                "client_failure" => AndroidOnDeviceAsrFailureKind.ClientFailure,
                "service_failure" => AndroidOnDeviceAsrFailureKind.ServiceFailure,
                "recognizer_busy" => AndroidOnDeviceAsrFailureKind.Busy,
                "too_many_requests" => AndroidOnDeviceAsrFailureKind.TooManyRequests,
                "service_disconnected" =>
                    AndroidOnDeviceAsrFailureKind.ServiceDisconnected,
                "language_not_supported" =>
                    AndroidOnDeviceAsrFailureKind.LanguageNotSupported,
                "language_model_unavailable" =>
                    AndroidOnDeviceAsrFailureKind.LanguageModelUnavailable,
                "unexpected_network_error" =>
                    AndroidOnDeviceAsrFailureKind.UnexpectedNetworkFailure,
                _ => AndroidOnDeviceAsrFailureKind.Unknown,
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
                bool onDeviceAvailable,
                bool supportCheckAvailable)
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
                    onDeviceAvailable,
                    supportCheckAvailable));
            }
        }

        private sealed class SupportCallback : AndroidJavaProxy
        {
            private readonly string requestId;
            private readonly TaskCompletionSource<SupportResult> completion;

            public SupportCallback(
                string requestId,
                TaskCompletionSource<SupportResult> completion)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.completion = completion;
            }

            public void onSupportResult(
                string callbackRequestId,
                string state,
                string diagnostic)
            {
                if (!string.Equals(
                    requestId,
                    callbackRequestId,
                    StringComparison.Ordinal))
                {
                    completion.TrySetException(
                        IdentityMismatch("support"));
                    return;
                }
                completion.TrySetResult(new SupportResult(state, diagnostic));
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
                    new AndroidOnDeviceAsrPlatformEvent(
                        requestId,
                        AndroidOnDeviceAsrPlatformEventKind.Started));

            public void onPartialResult(
                string callbackRequestId,
                string transcript) =>
                Publish(callbackRequestId,
                    new AndroidOnDeviceAsrPlatformEvent(
                        requestId,
                        AndroidOnDeviceAsrPlatformEventKind.PartialResult,
                        transcript));

            public void onFinalResult(
                string callbackRequestId,
                string transcript) =>
                Publish(callbackRequestId,
                    new AndroidOnDeviceAsrPlatformEvent(
                        requestId,
                        AndroidOnDeviceAsrPlatformEventKind.FinalResult,
                        transcript));

            public void onNoMatch(string callbackRequestId) =>
                Publish(callbackRequestId,
                    new AndroidOnDeviceAsrPlatformEvent(
                        requestId,
                        AndroidOnDeviceAsrPlatformEventKind.NoMatch));

            public void onCancelled(string callbackRequestId) =>
                Publish(callbackRequestId,
                    new AndroidOnDeviceAsrPlatformEvent(
                        requestId,
                        AndroidOnDeviceAsrPlatformEventKind.Cancelled));

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
                AndroidOnDeviceAsrPlatformEvent value)
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
                    AndroidOnDeviceAsrFailureKind.Unknown,
                    "callback_request_identity_mismatch",
                    "The Android on-device ASR Java callback returned a different request identifier.");
                return false;
            }
        }

        private sealed class ProbeResult
        {
            public ProbeResult(
                int apiLevel,
                bool permission,
                bool onDevice,
                bool supportCheck)
            {
                ApiLevel = apiLevel;
                HasMicrophonePermission = permission;
                ExplicitOnDeviceRecognitionAvailable = onDevice;
                RecognitionSupportCheckAvailable = supportCheck;
            }

            public int ApiLevel { get; }
            public bool HasMicrophonePermission { get; }
            public bool ExplicitOnDeviceRecognitionAvailable { get; }
            public bool RecognitionSupportCheckAvailable { get; }
        }

        private sealed class SupportResult
        {
            public SupportResult(string state, string diagnostic)
            {
                State = state;
                Diagnostic = diagnostic;
            }
            public string State { get; }
            public string Diagnostic { get; }
        }

        private sealed class RecognitionEventQueue : IDisposable
        {
            private const int MaximumQueuedEvents = 128;
            private readonly object queueSync = new object();
            private readonly Queue<AndroidOnDeviceAsrPlatformEvent> events =
                new Queue<AndroidOnDeviceAsrPlatformEvent>();
            private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            private readonly string requestId;
            private bool terminal;
            private bool queueDisposed;

            public RecognitionEventQueue(string requestId)
            {
                this.requestId = requestId;
            }

            public void Publish(AndroidOnDeviceAsrPlatformEvent value)
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
                            AndroidOnDeviceAsrFailureKind.ServiceFailure,
                            "callback_queue_overflow",
                            "Android on-device ASR callback queue overflowed; no transcript event was silently dropped."));
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
                AndroidOnDeviceAsrFailureKind kind,
                string code,
                string diagnostic) =>
                Publish(Failure(requestId, kind, code, diagnostic));

            public async ValueTask<AndroidOnDeviceAsrPlatformEvent> ReadAsync()
            {
                await signal.WaitAsync().ConfigureAwait(false);
                lock (queueSync)
                {
                    if (events.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Android on-device ASR callback queue ended without a readable event.");
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

        private static InvalidOperationException IdentityMismatch(
            string callbackKind) =>
            new InvalidOperationException(
                "Android on-device ASR " + callbackKind +
                " callback returned a different request identifier.");
#endif
    }

    public static class ReachyAndroidOnDeviceAsrProviderFactory
    {
        public static IAsrProvider Create(
            string instanceId,
            string languageTag,
            TimeSpan maximumUtteranceDuration)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidOnDeviceAsrProvider(
                new ReachyAndroidOnDeviceAsrPlatform(),
                instanceId,
                languageTag,
                maximumUtteranceDuration);
#else
            throw new PlatformNotSupportedException(
                "Explicit Android on-device ASR is available only in Android player builds.");
#endif
        }
    }
}
