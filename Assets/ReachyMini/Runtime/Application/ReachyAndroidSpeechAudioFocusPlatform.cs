#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidSpeechAudioFocusPlatform : ISpeechAudioFocusPlatform
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.speech.ReachySpeechAudioFocusBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly object sync = new object();
        private AndroidJavaObject? bridge;
        private bool disposed;
#endif

        public ReachyAndroidSpeechAudioFocusPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Android speech audio-focus coordination is available only in Android player builds.");
#endif
        }

        public event EventHandler<SpeechAudioPlatformInterruptionEventArgs>? Interrupted;

        public async ValueTask<SpeechAudioFocusRequestResult> RequestFocusAsync(
            string sessionId,
            SpeechAudioRole role,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException(
                    "Speech audio session identity is required.",
                    nameof(sessionId));
            }
            if (!Enum.IsDefined(typeof(SpeechAudioRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            var completion = new TaskCompletionSource<SpeechAudioFocusRequestResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new FocusCallback(this, sessionId, completion);
            using AndroidJavaObject activity = GetCurrentActivity();
            try
            {
                activeBridge.Call(
                    "request",
                    activity,
                    sessionId,
                    role == SpeechAudioRole.Listening
                        ? "listening"
                        : "speaking",
                    callback);
            }
            catch (AndroidJavaException exception)
            {
                throw new InvalidOperationException(
                    "Starting Android speech audio-focus acquisition failed with " +
                    exception.GetType().Name + ".",
                    exception);
            }

            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            activeBridge.Call(
                                "release",
                                sessionId,
                                new CancellationReleaseCallback(
                                    sessionId,
                                    completion,
                                    cancellationToken));
                        }
                        catch (AndroidJavaException exception)
                        {
                            completion.TrySetException(new InvalidOperationException(
                                "Cancelling Android speech audio focus could not release the exact session.",
                                exception));
                        }
                    });
            return await completion.Task.ConfigureAwait(false);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android speech audio-focus coordination requires an Android player.");
#endif
        }

        public async ValueTask ReleaseFocusAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException(
                    "Speech audio session identity is required.",
                    nameof(sessionId));
            }
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge = GetBridge();
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ReleaseCallback(sessionId, completion);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken));
            try
            {
                activeBridge.Call("release", sessionId, callback);
            }
            catch (AndroidJavaException exception)
            {
                throw new InvalidOperationException(
                    "Releasing Android speech audio focus failed with " +
                    exception.GetType().Name + ".",
                    exception);
            }

            _ = await completion.Task.ConfigureAwait(false);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Android speech audio-focus coordination requires an Android player.");
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

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject GetBridge()
        {
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(ReachyAndroidSpeechAudioFocusPlatform));
                }
                return bridge ?? throw new ObjectDisposedException(
                    nameof(ReachyAndroidSpeechAudioFocusPlatform));
            }
        }

        private void PublishInterruption(
            string sessionId,
            string code,
            string diagnostic)
        {
            Interrupted?.Invoke(
                this,
                new SpeechAudioPlatformInterruptionEventArgs(
                    sessionId,
                    new SpeechAudioInterruption(
                        ParseInterruptionKind(code),
                        code,
                        diagnostic)));
        }

        private static SpeechAudioInterruptionKind ParseInterruptionKind(
            string code)
        {
            return code switch
            {
                "audio_focus_loss_permanent" =>
                    SpeechAudioInterruptionKind.PermanentFocusLoss,
                "audio_focus_loss_transient" =>
                    SpeechAudioInterruptionKind.TransientFocusLoss,
                "audio_focus_duck_rejected" =>
                    SpeechAudioInterruptionKind.DuckRequested,
                "audio_route_added" or "audio_route_removed" =>
                    SpeechAudioInterruptionKind.AudioRouteChanged,
                "audio_becoming_noisy" =>
                    SpeechAudioInterruptionKind.BecomingNoisy,
                "phone_or_communication_audio_mode" =>
                    SpeechAudioInterruptionKind.PhoneOrCommunicationMode,
                "microphone_muted" =>
                    SpeechAudioInterruptionKind.MicrophoneMuted,
                _ => SpeechAudioInterruptionKind.PlatformFailure,
            };
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private sealed class FocusCallback : AndroidJavaProxy
        {
            private readonly ReachyAndroidSpeechAudioFocusPlatform owner;
            private readonly string sessionId;
            private readonly TaskCompletionSource<SpeechAudioFocusRequestResult>
                completion;

            public FocusCallback(
                ReachyAndroidSpeechAudioFocusPlatform owner,
                string sessionId,
                TaskCompletionSource<SpeechAudioFocusRequestResult> completion)
                : base(JavaClassName + "$Callback")
            {
                this.owner = owner;
                this.sessionId = sessionId;
                this.completion = completion;
            }

            public void onFocusGranted(string callbackSessionId)
            {
                if (!ValidateIdentity(callbackSessionId))
                {
                    return;
                }
                completion.TrySetResult(SpeechAudioFocusRequestResult.Granted(
                    "Android granted immediate speech audio focus."));
            }

            public void onFocusDenied(
                string callbackSessionId,
                string code,
                string diagnostic)
            {
                if (!ValidateIdentity(callbackSessionId))
                {
                    return;
                }
                completion.TrySetResult(
                    SpeechAudioFocusRequestResult.Denied(code, diagnostic));
            }

            public void onReleased(string callbackSessionId)
            {
                ValidateIdentity(callbackSessionId);
            }

            public void onReleaseFailed(
                string callbackSessionId,
                string code,
                string diagnostic)
            {
                ValidateIdentity(callbackSessionId);
            }

            public void onInterrupted(
                string callbackSessionId,
                string code,
                string diagnostic)
            {
                if (!ValidateIdentity(callbackSessionId))
                {
                    return;
                }
                owner.PublishInterruption(sessionId, code, diagnostic);
                completion.TrySetResult(
                    SpeechAudioFocusRequestResult.Denied(code, diagnostic));
            }

            private bool ValidateIdentity(string callbackSessionId)
            {
                if (string.Equals(
                    sessionId,
                    callbackSessionId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                completion.TrySetException(new InvalidOperationException(
                    "Android speech audio-focus callback returned a different session identifier."));
                return false;
            }
        }

        private sealed class ReleaseCallback : AndroidJavaProxy
        {
            private readonly string sessionId;
            private readonly TaskCompletionSource<object?> completion;

            public ReleaseCallback(
                string sessionId,
                TaskCompletionSource<object?> completion)
                : base(JavaClassName + "$Callback")
            {
                this.sessionId = sessionId;
                this.completion = completion;
            }

            public void onFocusGranted(string callbackSessionId) =>
                ValidateIdentity(callbackSessionId);

            public void onFocusDenied(
                string callbackSessionId,
                string code,
                string diagnostic) =>
                ValidateIdentity(callbackSessionId);

            public void onReleased(string callbackSessionId)
            {
                if (ValidateIdentity(callbackSessionId))
                {
                    completion.TrySetResult(null);
                }
            }

            public void onReleaseFailed(
                string callbackSessionId,
                string code,
                string diagnostic)
            {
                if (ValidateIdentity(callbackSessionId))
                {
                    completion.TrySetException(new InvalidOperationException(
                        "Android speech audio-focus release failed: " +
                        code + ": " + diagnostic));
                }
            }

            public void onInterrupted(
                string callbackSessionId,
                string code,
                string diagnostic) =>
                ValidateIdentity(callbackSessionId);

            private bool ValidateIdentity(string callbackSessionId)
            {
                if (string.Equals(
                    sessionId,
                    callbackSessionId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                completion.TrySetException(new InvalidOperationException(
                    "Android speech audio-focus release callback returned a different session identifier."));
                return false;
            }
        }

        private sealed class CancellationReleaseCallback : AndroidJavaProxy
        {
            private readonly string sessionId;
            private readonly TaskCompletionSource<SpeechAudioFocusRequestResult>
                completion;
            private readonly CancellationToken cancellationToken;

            public CancellationReleaseCallback(
                string sessionId,
                TaskCompletionSource<SpeechAudioFocusRequestResult> completion,
                CancellationToken cancellationToken)
                : base(JavaClassName + "$Callback")
            {
                this.sessionId = sessionId;
                this.completion = completion;
                this.cancellationToken = cancellationToken;
            }

            public void onFocusGranted(string callbackSessionId) =>
                Validate(callbackSessionId);

            public void onFocusDenied(
                string callbackSessionId,
                string code,
                string diagnostic) =>
                Validate(callbackSessionId);

            public void onReleased(string callbackSessionId)
            {
                if (Validate(callbackSessionId))
                {
                    completion.TrySetCanceled(cancellationToken);
                }
            }

            public void onReleaseFailed(
                string callbackSessionId,
                string code,
                string diagnostic)
            {
                if (Validate(callbackSessionId))
                {
                    completion.TrySetException(new InvalidOperationException(
                        "Cancelling Android speech audio focus failed to release the exact session: " +
                        code + ": " + diagnostic));
                }
            }

            public void onInterrupted(
                string callbackSessionId,
                string code,
                string diagnostic) =>
                Validate(callbackSessionId);

            private bool Validate(string callbackSessionId)
            {
                if (string.Equals(
                    sessionId,
                    callbackSessionId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                completion.TrySetException(new InvalidOperationException(
                    "Android speech audio-focus cancellation release returned a different session identifier."));
                return false;
            }
        }
#endif
    }
}
