#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyAndroidMlKitTrackingBackend :
        IReachyTrackingDetectionBackend
    {
        public const string JavaClassName =
            "com.ekkus93.weachy.camera.ReachyMlKitTrackingBridge";

        private readonly object sync = new object();
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject? bridge;
#endif
        private string? activeRequestId;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool disposed;
#endif

        public ReachyAndroidMlKitTrackingBackend()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaObject(JavaClassName);
#else
            throw new PlatformNotSupportedException(
                "Bundled ML Kit lightweight tracking is available only in Android player builds.");
#endif
        }

        public string BackendId => "google-mlkit-bundled-face-selfie";

        public string BackendVersion =>
            "face-detection-16.1.7+segmentation-selfie-16.0.0-beta6";

        public bool SupportsFaces => true;

        public bool SupportsPeople => true;

        public async ValueTask<IReadOnlyList<ReachyTrackingDetection>> DetectAsync(
            ReachyTrackingFramePixels pixels,
            CancellationToken cancellationToken)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }
            cancellationToken.ThrowIfCancellationRequested();
            string requestId = Guid.NewGuid().ToString("N");
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activeBridge;
            lock (sync)
            {
                ThrowIfDisposed();
                if (activeRequestId != null)
                {
                    throw new InvalidOperationException(
                        "The ML Kit backend already has an in-flight request; requests are not queued.");
                }
                activeRequestId = requestId;
                activeBridge = bridge ??
                    throw new ObjectDisposedException(
                        nameof(ReachyAndroidMlKitTrackingBackend));
            }

            var completion = new TaskCompletionSource<BridgeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new Callback(requestId, completion);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            activeBridge.Call("cancel", requestId);
                        }
                        catch (AndroidJavaException exception)
                        {
                            completion.TrySetException(
                                new InvalidOperationException(
                                    $"ML Kit cancellation bridge failed: {exception.Message}",
                                    exception));
                            return;
                        }
                        completion.TrySetCanceled(cancellationToken);
                    });

            try
            {
                activeBridge.Call(
                    "detect",
                    requestId,
                    pixels.CopyRgbaTopLeft(),
                    pixels.Width,
                    pixels.Height,
                    callback);
                BridgeResult result = await completion.Task;
                if (!string.Equals(
                        result.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "ML Kit callback request identity did not match the active request.");
                }
                if (result.ErrorCode != null)
                {
                    throw new InvalidOperationException(
                        $"ML Kit tracking failed ({result.ErrorCode}): {result.ErrorMessage}");
                }
                return ParseDetections(requestId, result.Payload!);
            }
            catch (AndroidJavaException exception)
            {
                throw new InvalidOperationException(
                    $"ML Kit Java bridge failed: {exception.Message}",
                    exception);
            }
            finally
            {
                lock (sync)
                {
                    if (string.Equals(
                            activeRequestId,
                            requestId,
                            StringComparison.Ordinal))
                    {
                        activeRequestId = null;
                    }
                }
            }
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "Bundled ML Kit lightweight tracking is available only in Android player builds.");
#endif
        }

        public ValueTask DisposeAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject? value;
            string? requestId;
            lock (sync)
            {
                if (disposed)
                {
                    return default;
                }
                disposed = true;
                value = bridge;
                bridge = null;
                requestId = activeRequestId;
                activeRequestId = null;
            }
            if (value != null)
            {
                try
                {
                    if (requestId != null)
                    {
                        value.Call("cancel", requestId);
                    }
                    value.Call("close");
                }
                finally
                {
                    value.Dispose();
                }
            }
#endif
            return default;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IReadOnlyList<ReachyTrackingDetection> ParseDetections(
            string requestId,
            string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException(
                    "ML Kit tracking returned an empty payload.");
            }
            DetectionEnvelope? envelope =
                JsonUtility.FromJson<DetectionEnvelope>(payload);
            if (envelope == null ||
                envelope.schema_version != 1 ||
                !string.Equals(
                    envelope.request_id,
                    requestId,
                    StringComparison.Ordinal) ||
                envelope.detections == null)
            {
                throw new InvalidOperationException(
                    "ML Kit tracking payload schema or request identity is invalid.");
            }
            if (envelope.detections.Length > 64)
            {
                throw new InvalidOperationException(
                    "ML Kit tracking payload exceeded the bounded detection count.");
            }

            var detections = new List<ReachyTrackingDetection>(
                envelope.detections.Length);
            for (int index = 0; index < envelope.detections.Length; ++index)
            {
                DetectionDto dto = envelope.detections[index] ??
                    throw new InvalidOperationException(
                        "ML Kit tracking payload contained a null detection.");
                ReachyTrackingDetectionClass detectionClass;
                if (string.Equals(dto.classification, "face", StringComparison.Ordinal))
                {
                    detectionClass = ReachyTrackingDetectionClass.Face;
                }
                else if (string.Equals(
                    dto.classification,
                    "person",
                    StringComparison.Ordinal))
                {
                    detectionClass = ReachyTrackingDetectionClass.Person;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"ML Kit returned unsupported classification '{dto.classification}'.");
                }
                detections.Add(new ReachyTrackingDetection(
                    detectionClass,
                    string.IsNullOrWhiteSpace(dto.provider_tracking_id)
                        ? null
                        : dto.provider_tracking_id,
                    dto.confidence,
                    new NormalizedVisionBounds(
                        dto.left,
                        dto.top,
                        dto.width,
                        dto.height)));
            }
            return new ReadOnlyCollection<ReachyTrackingDetection>(detections);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidMlKitTrackingBackend));
            }
        }

        private sealed class Callback : AndroidJavaProxy
        {
            private readonly string requestId;
            private readonly TaskCompletionSource<BridgeResult> completion;

            public Callback(
                string requestId,
                TaskCompletionSource<BridgeResult> completion)
                : base(JavaClassName + "$Callback")
            {
                this.requestId = requestId;
                this.completion = completion ??
                    throw new ArgumentNullException(nameof(completion));
            }

            public void onSuccess(string callbackRequestId, string payload)
            {
                if (!string.Equals(
                        requestId,
                        callbackRequestId,
                        StringComparison.Ordinal))
                {
                    completion.TrySetException(
                        new InvalidOperationException(
                            "ML Kit success callback returned a different request identifier."));
                    return;
                }
                completion.TrySetResult(
                    BridgeResult.Success(callbackRequestId, payload));
            }

            public void onFailure(
                string callbackRequestId,
                string code,
                string message)
            {
                if (!string.Equals(
                        requestId,
                        callbackRequestId,
                        StringComparison.Ordinal))
                {
                    completion.TrySetException(
                        new InvalidOperationException(
                            "ML Kit failure callback returned a different request identifier."));
                    return;
                }
                completion.TrySetResult(
                    BridgeResult.Failure(
                        callbackRequestId,
                        code,
                        message));
            }
        }

        private sealed class BridgeResult
        {
            private BridgeResult(
                string requestId,
                string? payload,
                string? errorCode,
                string? errorMessage)
            {
                RequestId = requestId;
                Payload = payload;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
            }

            public string RequestId { get; }

            public string? Payload { get; }

            public string? ErrorCode { get; }

            public string? ErrorMessage { get; }

            public static BridgeResult Success(
                string requestId,
                string payload)
            {
                return new BridgeResult(
                    requestId,
                    payload,
                    null,
                    null);
            }

            public static BridgeResult Failure(
                string requestId,
                string code,
                string message)
            {
                return new BridgeResult(
                    requestId,
                    null,
                    code,
                    message);
            }
        }

        [Serializable]
        private sealed class DetectionEnvelope
        {
            public int schema_version;
            public string request_id = string.Empty;
            public DetectionDto[] detections =
                Array.Empty<DetectionDto>();
        }

        [Serializable]
        private sealed class DetectionDto
        {
            public string classification = string.Empty;
            public string provider_tracking_id = string.Empty;
            public double confidence;
            public double left;
            public double top;
            public double width;
            public double height;
        }
#endif
    }
}
