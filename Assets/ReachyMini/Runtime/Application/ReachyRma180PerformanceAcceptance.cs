#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using ReachyMini.Performance;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed class ReachyRma180PerformanceAcceptance : MonoBehaviour
    {
        internal const string LaunchExtraName =
            "reachy_rma180_performance_acceptance";
        internal const string ProfileSecondsExtraName =
            "reachy_rma180_profile_seconds";
        internal const string ResultFileName =
            "rma180-performance-acceptance.json";

        internal const int DefaultProfileSeconds = 300;
        internal const int MinimumProfileSeconds = 10;
        internal const int MaximumProfileSeconds = 3600;
        private static readonly TimeSpan WarmupDuration =
            TimeSpan.FromSeconds(5.0);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android ||
                !ReadBooleanLaunchExtra(LaunchExtraName, false))
            {
                return;
            }

            var host = new GameObject("ReachyRma180PerformanceAcceptance");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyRma180PerformanceAcceptance>();
        }

        private async void Start()
        {
            string resultPath = Path.Combine(
                Application.persistentDataPath,
                ResultFileName);
            int originalTargetFrameRate = Application.targetFrameRate;
            int originalVSyncCount = QualitySettings.vSyncCount;
            int profileSeconds = ReadProfileSeconds();

            try
            {
                DeleteIfExists(resultPath);
                await Task.Delay(WarmupDuration);

                QualitySettings.vSyncCount = 0;
                ReachyPerformanceReport fps30 =
                    await CaptureProfileAsync(30, profileSeconds);
                ReachyPerformanceReport fps60 =
                    await CaptureProfileAsync(60, profileSeconds);

                WriteCombinedReport(
                    resultPath,
                    profileSeconds,
                    fps30,
                    fps60);
                Debug.Log(
                    "RMA-180 performance acceptance captured 30 FPS and 60 FPS profiles.");
            }
            catch (Exception exception)
            {
                TryWriteFailure(resultPath, profileSeconds, exception);
                Debug.LogError(
                    "RMA-180 performance acceptance failed (" +
                    exception.GetType().Name + ").");
            }
            finally
            {
                Application.targetFrameRate = originalTargetFrameRate;
                QualitySettings.vSyncCount = originalVSyncCount;
            }
        }

        private static async Task<ReachyPerformanceReport> CaptureProfileAsync(
            int targetFps,
            int profileSeconds)
        {
            Application.targetFrameRate = targetFps;
            await Task.Yield();

            using ReachyPerformanceSession session =
                ReachyPerformanceTelemetry.StartSession(
                    targetFps,
                    "rma180-fps" + targetFps);
            await Task.Delay(TimeSpan.FromSeconds(profileSeconds));

            ReachyPerformanceReport report = session.Complete();
            RequireBaselineSignals(report);
            return report;
        }

        private static void RequireBaselineSignals(ReachyPerformanceReport report)
        {
            ReachyPerformanceTimingSummary rendering =
                report.FindTiming(ReachyPerformanceWorkload.UnityRendering);
            if (rendering.SampleCount <= 0L)
            {
                throw new InvalidOperationException(
                    "RMA-180 captured no Unity rendering samples.");
            }

            ReachyPerformanceTimingSummary physics =
                report.FindTiming(ReachyPerformanceWorkload.NativePhysics);
            if (physics.SampleCount <= 0L)
            {
                throw new InvalidOperationException(
                    "RMA-180 captured no authoritative native physics samples.");
            }

            if (report.Resources.SampleCount <= 0L)
            {
                throw new InvalidOperationException(
                    "RMA-180 captured no memory, battery, or thermal resource samples.");
            }
        }

        private static void WriteCombinedReport(
            string path,
            int profileSeconds,
            ReachyPerformanceReport fps30,
            ReachyPerformanceReport fps60)
        {
            string json =
                "{\"schema_version\":1," +
                "\"status\":\"passed\"," +
                "\"profile_seconds\":" + profileSeconds + "," +
                "\"profiles\":[" +
                ReachyPerformanceReportJsonFormatter.Format(fps30) + "," +
                ReachyPerformanceReportJsonFormatter.Format(fps60) +
                "]}";
            WriteAtomically(path, json);
        }

        private static void TryWriteFailure(
            string path,
            int profileSeconds,
            Exception exception)
        {
            try
            {
                string json =
                    "{\"schema_version\":1," +
                    "\"status\":\"failed\"," +
                    "\"profile_seconds\":" + profileSeconds + "," +
                    "\"exception_type\":\"" +
                    exception.GetType().Name + "\"}";
                WriteAtomically(path, json);
            }
            catch (Exception writeException)
            {
                Debug.LogError(
                    "RMA-180 failure evidence could not be written (" +
                    writeException.GetType().Name + ").");
            }
        }

        private static void WriteAtomically(string path, string json)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "RMA-180 result path has no parent directory.");
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

        private static void DeleteIfExists(string path)
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

        private static int ReadProfileSeconds()
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
                int value = intent.Call<int>(
                    "getIntExtra",
                    ProfileSecondsExtraName,
                    DefaultProfileSeconds);
                return Math.Max(
                    MinimumProfileSeconds,
                    Math.Min(MaximumProfileSeconds, value));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "RMA-180 could not read the profile duration launch extra.",
                    exception);
            }
#else
            return DefaultProfileSeconds;
#endif
        }

        private static bool ReadBooleanLaunchExtra(
            string name,
            bool fallback)
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
    }
}
