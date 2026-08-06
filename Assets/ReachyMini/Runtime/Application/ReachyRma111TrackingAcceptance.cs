#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyRma111TrackingAcceptanceBootstrap
    {
        public const string AcceptanceLaunchExtra =
            "reachy_rma111_acceptance";
        public const string ResultFileName =
            "rma111-lightweight-tracking-state.json";
        public const string ObjectName =
            "ReachyRma111TrackingAcceptance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallIfRequested()
        {
            if (!IsAcceptanceRequestedFromLaunchIntent() ||
                Object.FindAnyObjectByType<
                    ReachyRma111TrackingAcceptance>() != null)
            {
                return;
            }
            var gameObject = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<ReachyRma111TrackingAcceptance>();
        }

        public static bool IsAcceptanceRequestedFromLaunchIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>(
                        "currentActivity");
                using AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>(
                    "getBooleanExtra",
                    AcceptanceLaunchExtra,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect the RMA-111 acceptance launch extra: " +
                    exception.Message);
                return false;
            }
#else
            return false;
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyRma111TrackingAcceptance : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ReachyRma111TrackingAcceptanceBootstrap.ResultFileName);
            DeleteIfPresent(resultPath);
            Task<Report> task = RunAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Report report;
            if (task.IsFaulted)
            {
                Exception exception =
                    task.Exception?.GetBaseException() ??
                    new InvalidOperationException(
                        "RMA-111 acceptance failed without an exception.");
                report = Report.Failure(exception.Message);
                Debug.LogError(
                    "RMA-111 lightweight tracking acceptance failed: " +
                    exception.Message,
                    this);
            }
            else if (task.IsCanceled)
            {
                report = Report.Failure(
                    "RMA-111 lightweight tracking acceptance was cancelled.");
            }
            else
            {
                report = task.Result;
            }

            WriteAtomically(
                resultPath,
                JsonUtility.ToJson(report, prettyPrint: true));
            Destroy(gameObject);
        }

        private static async Task<Report> RunAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            FixturePixels fixture = DecodeFixture();
            var backend = new ReachyAndroidMlKitTrackingBackend();
            await using var tracker =
                new ReachyOnDeviceLightweightTracker(
                    "rma111-physical-acceptance",
                    backend);
            var selection = new VisionProviderSelection(
                tracker.Descriptor);

            TrackingResult first = await TrackFixtureAsync(
                tracker,
                selection,
                fixture,
                sourceSequence: 1UL,
                timestampNanoseconds: 1_000_000_000L,
                validity: CreateValidity(fixture.Width, fixture.Height),
                requestId: "rma111-first");
            RequireSuccess(first, "first bundled ML Kit inference");
            TrackedObject firstFace = first.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "face",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect a face in the pinned public-domain fixture.");

            TrackingResult second = await TrackFixtureAsync(
                tracker,
                selection,
                fixture,
                sourceSequence: 2UL,
                timestampNanoseconds: 1_050_000_000L,
                validity: CreateValidity(fixture.Width, fixture.Height),
                requestId: "rma111-second");
            RequireSuccess(second, "second bundled ML Kit inference");
            TrackedObject secondFace = second.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "face",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect the fixture face on the second frame.");
            if (!string.Equals(
                    firstFace.LocalId,
                    secondFace.LocalId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The managed stable face identifier changed across equivalent frames.");
            }

            byte[] invalidCenter = CreateValidity(
                fixture.Width,
                fixture.Height);
            InvalidateCenter(
                invalidCenter,
                fixture.Width,
                fixture.Height,
                secondFace.Bounds);
            TrackingResult masked = await TrackFixtureAsync(
                tracker,
                selection,
                fixture,
                sourceSequence: 3UL,
                timestampNanoseconds: 1_100_000_000L,
                validity: invalidCenter,
                requestId: "rma111-invalid-center");
            RequireSuccess(masked, "invalid-center inference");
            bool invalidFaceSuppressed = !masked.Objects.Any(item =>
                string.Equals(
                    item.Classification,
                    "face",
                    StringComparison.Ordinal));
            if (!invalidFaceSuppressed)
            {
                throw new InvalidOperationException(
                    "A face centered in an invalid transformed pixel was reported.");
            }

            return Report.Success(
                backend.BackendId,
                backend.BackendVersion,
                ReachyRma111Fixture.Sha256,
                first.Objects.Count(item =>
                    item.Classification == "face"),
                first.Objects.Count(item =>
                    item.Classification == "person"),
                firstFace.LocalId,
                secondFace.LocalId,
                invalidFaceSuppressed);
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "RMA-111 physical acceptance requires an Android player build.");
#endif
        }

        private static async Task<TrackingResult> TrackFixtureAsync(
            IVisualTracker tracker,
            VisionProviderSelection selection,
            FixturePixels fixture,
            ulong sourceSequence,
            long timestampNanoseconds,
            byte[] validity,
            string requestId)
        {
            var identity = new ReachyVisionFrameIdentity(
                "rma111-public-domain-fixture",
                sourceSessionId: 1UL,
                sourceSequence,
                timestampNanoseconds,
                authoritativeSequence: sourceSequence,
                continuityId: 1U);
            var coverage = new ReachyVisionCoverage(
                VisionCoverageState.Normal,
                validPixelCount: CountValid(validity),
                totalPixelCount: validity.Length,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning: false,
                "RMA-111 fixture validity mask.");
            var resources = new FixtureResources(
                identity,
                fixture.Width,
                fixture.Height,
                fixture.RgbaTopLeft,
                validity);
            await using var frame = new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                identity,
                coverage,
                resources);
            var context = new VisionRequestContext(
                requestId,
                selection.Current,
                TimeSpan.FromSeconds(15.0));
            var request = new TrackingRequest(frame, context);
            return await VisionProviderExecutor.TrackAsync(
                tracker,
                request,
                selection,
                CancellationToken.None);
        }

        private static FixturePixels DecodeFixture()
        {
            byte[] jpeg = Convert.FromBase64String(
                ReachyRma111Fixture.JpegBase64);
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false);
            try
            {
                if (!ImageConversion.LoadImage(
                        texture,
                        jpeg,
                        markNonReadable: false))
                {
                    throw new InvalidOperationException(
                        "The pinned RMA-111 JPEG fixture could not be decoded.");
                }
                Color32[] bottomLeft = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                var topLeft = new byte[checked(width * height * 4)];
                for (int y = 0; y < height; ++y)
                {
                    int sourceY = height - 1 - y;
                    for (int x = 0; x < width; ++x)
                    {
                        Color32 pixel = bottomLeft[sourceY * width + x];
                        int offset = (y * width + x) * 4;
                        topLeft[offset] = pixel.r;
                        topLeft[offset + 1] = pixel.g;
                        topLeft[offset + 2] = pixel.b;
                        topLeft[offset + 3] = pixel.a;
                    }
                }
                return new FixturePixels(width, height, topLeft);
            }
            finally
            {
                DestroyUnityObject(texture);
            }
        }

        private static byte[] CreateValidity(int width, int height)
        {
            var validity = new byte[checked(width * height)];
            Array.Fill(validity, (byte)255);
            return validity;
        }

        private static void InvalidateCenter(
            byte[] validity,
            int width,
            int height,
            NormalizedVisionBounds bounds)
        {
            int centerX = Math.Min(
                width - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterX * width)));
            int centerY = Math.Min(
                height - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterY * height)));
            validity[centerY * width + centerX] = 0;
        }

        private static long CountValid(byte[] validity)
        {
            long count = 0L;
            for (int index = 0; index < validity.Length; ++index)
            {
                if (validity[index] >= 128)
                {
                    count++;
                }
            }
            return count;
        }

        private static void RequireSuccess(
            TrackingResult result,
            string stage)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{stage} failed with {result.Status}: {result.Diagnostic}");
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            string temporary = path + ".tmp";
            Directory.CreateDirectory(
                Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "RMA-111 result path has no directory."));
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            string temporary = path + ".tmp";
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        private sealed class FixturePixels
        {
            public FixturePixels(
                int width,
                int height,
                byte[] rgbaTopLeft)
            {
                Width = width;
                Height = height;
                RgbaTopLeft = rgbaTopLeft;
            }

            public int Width { get; }

            public int Height { get; }

            public byte[] RgbaTopLeft { get; }
        }


        private static void DestroyUnityObject(UnityEngine.Object value)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        private sealed class FixtureResources :
            IReachyVisionFrameResources,
            IReachyTrackingPixelSource
        {
            private readonly ReachyTrackingFramePixels pixels;
            private bool disposed;

            public FixtureResources(
                ReachyVisionFrameIdentity identity,
                int width,
                int height,
                byte[] rgba,
                byte[] validity)
            {
                pixels = new ReachyTrackingFramePixels(
                    identity,
                    width,
                    height,
                    rgba,
                    validity);
            }

            public string OwnerId => "rma111-fixture";

            public ulong Generation => pixels.Identity.SourceSequence;

            public int Width => pixels.Width;

            public int Height => pixels.Height;

            public bool IsDisposed => disposed;

            public bool HasResource(VisionResourceKind kind)
            {
                return !disposed &&
                    (kind == VisionResourceKind.Color ||
                     kind == VisionResourceKind.ValidityMask);
            }

            public VisionPixelEncoding GetEncoding(VisionResourceKind kind)
            {
                return kind == VisionResourceKind.Color
                    ? VisionPixelEncoding.Rgba8
                    : kind == VisionResourceKind.ValidityMask
                        ? VisionPixelEncoding.ValidityMask8
                        : throw new ArgumentOutOfRangeException(nameof(kind));
            }

            public bool TryGetResource<TResource>(
                VisionResourceKind kind,
                out TResource? resource)
                where TResource : class
            {
                if (!disposed &&
                    kind == VisionResourceKind.Color &&
                    this is TResource pixelSource)
                {
                    resource = pixelSource;
                    return true;
                }
                resource = null;
                return false;
            }

            public ValueTask<ReachyTrackingFramePixels> StageAsync(
                ReachyVisionFrame frame,
                int maximumDimension,
                CancellationToken cancellationToken)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(FixtureResources));
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (maximumDimension < Math.Max(Width, Height))
                {
                    throw new InvalidOperationException(
                        "The pinned RMA-111 fixture must fit inside the tracker staging bound.");
                }
                return new ValueTask<ReachyTrackingFramePixels>(pixels);
            }

            public ValueTask DisposeAsync()
            {
                disposed = true;
                return default;
            }
        }

        [Serializable]
        private sealed class Report
        {
            public string status = string.Empty;
            public bool acceptance_enabled;
            public string backend_id = string.Empty;
            public string backend_version = string.Empty;
            public string fixture_sha256 = string.Empty;
            public int first_face_count;
            public int first_person_count;
            public string first_face_id = string.Empty;
            public string second_face_id = string.Empty;
            public bool stable_face_id;
            public bool invalid_center_suppressed;
            public bool object_tracking_enabled;
            public bool motion_tracking_enabled;
            public int vlm_invocation_count;
            public bool network_model_download_used;
            public string error = string.Empty;

            public static Report Success(
                string backendId,
                string backendVersion,
                string fixtureSha256,
                int faceCount,
                int personCount,
                string firstFaceId,
                string secondFaceId,
                bool invalidCenterSuppressed)
            {
                return new Report
                {
                    status = "passed",
                    acceptance_enabled = true,
                    backend_id = backendId,
                    backend_version = backendVersion,
                    fixture_sha256 = fixtureSha256,
                    first_face_count = faceCount,
                    first_person_count = personCount,
                    first_face_id = firstFaceId,
                    second_face_id = secondFaceId,
                    stable_face_id = string.Equals(
                        firstFaceId,
                        secondFaceId,
                        StringComparison.Ordinal),
                    invalid_center_suppressed =
                        invalidCenterSuppressed,
                    object_tracking_enabled = false,
                    motion_tracking_enabled = false,
                    vlm_invocation_count = 0,
                    network_model_download_used = false,
                };
            }

            public static Report Failure(string message)
            {
                return new Report
                {
                    status = "failed",
                    acceptance_enabled = true,
                    error = string.IsNullOrWhiteSpace(message)
                        ? "unknown failure"
                        : message,
                };
            }
        }
    }
}
