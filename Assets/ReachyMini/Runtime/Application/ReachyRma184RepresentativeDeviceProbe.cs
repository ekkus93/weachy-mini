#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Performance;
using ReachyMini.Speech;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed class ReachyRma184RepresentativeDeviceProbe : MonoBehaviour
    {
        internal const string LaunchExtraName =
            "reachy_rma184_device_probe";
        internal const string ResultFileName =
            "rma184-device-probe.json";

        [Serializable]
        private sealed class ProbeReport
        {
            public int schema_version = 1;
            public string status = string.Empty;
            public string error = string.Empty;
            public string manufacturer = string.Empty;
            public string model = string.Empty;
            public string soc = string.Empty;
            public string processor = string.Empty;
            public int logical_processor_count;
            public int system_memory_mib;
            public string operating_system = string.Empty;
            public int android_api_level;
            public string graphics_api = string.Empty;
            public string graphics_device = string.Empty;
            public string camera_permission = string.Empty;
            public int camera_count;
            public int available_camera_count;
            public bool rear_camera_available;
            public bool front_camera_available;
            public string on_device_asr = string.Empty;
            public string offline_tts = string.Empty;
            public string support_status = string.Empty;
            public string support_diagnostic = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                !ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                return;
            }

            var host = new GameObject("ReachyRma184RepresentativeDeviceProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma184RepresentativeDeviceProbe>();
        }

        private async void Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ResultFileName);
            try
            {
                ProbeReport report = await CaptureAsync();
                WriteAtomically(resultPath, JsonUtility.ToJson(report, true));
                Debug.Log("RMA-184 representative-device probe completed.");
            }
            catch (Exception exception)
            {
                TryWriteFailure(resultPath, exception);
                Debug.LogError(
                    "RMA-184 representative-device probe failed (" +
                    exception.GetType().Name + ").");
            }
        }

        private static async Task<ProbeReport> CaptureAsync()
        {
            ReachyAndroidCameraDiscovery discovery =
                UnityEngine.Object.FindAnyObjectByType<ReachyAndroidCameraDiscovery>() ??
                new GameObject("ReachyRma184CameraDiscovery")
                    .AddComponent<ReachyAndroidCameraDiscovery>();
            discovery.RefreshPermissionAndCapabilities();
            ReachyCameraCapabilitySnapshot cameras = discovery.State.Current;

            bool rearCamera = false;
            bool frontCamera = false;
            for (int index = 0; index < cameras.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = cameras.Cameras[index];
                if (camera.Availability != ReachyCameraAvailabilityState.Available)
                {
                    continue;
                }
                rearCamera |= camera.Facing == ReachyDeviceCameraFacing.Rear;
                frontCamera |= camera.Facing == ReachyDeviceCameraFacing.Front;
            }

            SpeechProviderAvailability asr = await ProbeOnDeviceAsrAsync();
            SpeechProviderAvailability tts = await ProbeOfflineTtsAsync();
            int apiLevel = ReadAndroidApiLevel();
            long totalMemoryBytes = checked(
                (long)Math.Max(0, SystemInfo.systemMemorySize) * 1024L * 1024L);
            ReachyRepresentativeDeviceCapabilities capabilities =
                new ReachyRepresentativeDeviceCapabilities(
                    apiLevel,
                    totalMemoryBytes,
                    Math.Max(0, SystemInfo.processorCount),
                    SystemInfo.graphicsDeviceType.ToString(),
                    rearCamera,
                    frontCamera,
                    ToSpeechCapability(asr),
                    ToSpeechCapability(tts));
            ReachyDeviceSupportAssessment support =
                ReachyRepresentativeDeviceSupportPolicy.Evaluate(capabilities);

            return new ProbeReport
            {
                status = "passed",
                manufacturer = ReadBuildField("MANUFACTURER"),
                model = SystemInfo.deviceModel,
                soc = ReadSocModel(apiLevel),
                processor = SystemInfo.processorType,
                logical_processor_count = SystemInfo.processorCount,
                system_memory_mib = SystemInfo.systemMemorySize,
                operating_system = SystemInfo.operatingSystem,
                android_api_level = apiLevel,
                graphics_api = SystemInfo.graphicsDeviceType.ToString(),
                graphics_device = SystemInfo.graphicsDeviceName,
                camera_permission = cameras.Permission.ToString(),
                camera_count = cameras.Cameras.Count,
                available_camera_count = cameras.AvailableCameraCount,
                rear_camera_available = rearCamera,
                front_camera_available = frontCamera,
                on_device_asr = asr.State.ToString(),
                offline_tts = tts.State.ToString(),
                support_status = support.Status.ToString(),
                support_diagnostic = support.Diagnostic,
            };
        }

        private static async Task<SpeechProviderAvailability> ProbeOnDeviceAsrAsync()
        {
            IAsrProvider provider = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                "rma184-device-probe-asr",
                "en-US",
                TimeSpan.FromSeconds(30.0));
            try
            {
                return await provider.CheckAvailabilityAsync(
                    new AsrOptions("en-US", requestPartialResults: false),
                    CancellationToken.None);
            }
            finally
            {
                await provider.DisposeAsync();
            }
        }

        private static async Task<SpeechProviderAvailability> ProbeOfflineTtsAsync()
        {
            ITtsProvider provider = ReachyAndroidOfflineTtsProviderFactory.Create(
                "rma184-device-probe-tts",
                "en-US");
            try
            {
                return await provider.CheckAvailabilityAsync(CancellationToken.None);
            }
            finally
            {
                await provider.DisposeAsync();
            }
        }

        private static ReachyOfflineSpeechCapability ToSpeechCapability(
            SpeechProviderAvailability availability)
        {
            if (availability.IsAvailable)
            {
                return ReachyOfflineSpeechCapability.Available;
            }
            return availability.State == SpeechAvailabilityState.Unavailable
                ? ReachyOfflineSpeechCapability.Unavailable
                : ReachyOfflineSpeechCapability.Unknown;
        }

        private static int ReadAndroidApiLevel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
#else
            return 0;
#endif
        }

        private static string ReadSocModel(int apiLevel)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (apiLevel >= 31)
            {
                string soc = ReadBuildField("SOC_MODEL");
                if (!string.IsNullOrWhiteSpace(soc))
                {
                    return soc;
                }
            }
            string hardware = ReadBuildField("HARDWARE");
            return string.IsNullOrWhiteSpace(hardware)
                ? SystemInfo.processorType
                : hardware;
#else
            return SystemInfo.processorType;
#endif
        }

        private static string ReadBuildField(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                return build.GetStatic<string>(name) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
#else
            return string.Empty;
#endif
        }

        private static bool ReadBooleanLaunchExtra(string name, bool fallback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent");
                return intent.Call<bool>("getBooleanExtra", name, fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
#else
            return fallback;
#endif
        }

        private static void WriteAtomically(string path, string json)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "RMA-184 probe result path has no parent directory.");
            }
            Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            File.Move(temporary, fullPath);
        }

        private static void TryWriteFailure(string path, Exception exception)
        {
            try
            {
                WriteAtomically(
                    path,
                    JsonUtility.ToJson(
                        new ProbeReport
                        {
                            status = "failed",
                            error = exception.GetType().Name,
                        },
                        true));
            }
            catch (Exception writeException)
            {
                Debug.LogError(
                    "RMA-184 probe failure evidence could not be written (" +
                    writeException.GetType().Name + ").");
            }
        }
    }
}
