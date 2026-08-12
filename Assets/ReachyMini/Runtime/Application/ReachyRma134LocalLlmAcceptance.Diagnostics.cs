#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma134LocalLlmAcceptance
    {
        private static double ReadBatteryTemperatureCelsius()
        {
            using AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent");
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            string action = intentClass.GetStatic<string>("ACTION_BATTERY_CHANGED");
            using AndroidJavaObject filter = new AndroidJavaObject("android.content.IntentFilter", action);
            AndroidJavaObject? battery = activity.Call<AndroidJavaObject>("registerReceiver", (object?)null, filter);
            if (battery == null)
            {
                throw new InvalidOperationException(
                    "Android did not return ACTION_BATTERY_CHANGED data for RMA-134 acceptance.");
            }
            using (battery)
            {
                int temperatureTenths = battery.Call<int>("getIntExtra", "temperature", int.MinValue);
                if (temperatureTenths == int.MinValue)
                {
                    throw new InvalidOperationException(
                        "Android battery temperature is unavailable for RMA-134 acceptance.");
                }
                return temperatureTenths / 10.0;
            }
        }

        private static long ReadSelfRssBytes()
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long kibibytes))
                {
                    return checked(kibibytes * 1024L);
                }
                throw new InvalidOperationException("RMA-134 could not parse VmRSS from /proc/self/status.");
            }
            throw new InvalidOperationException("RMA-134 could not find VmRSS in /proc/self/status.");
        }

        private static bool ReadBooleanLaunchExtra(string name, bool defaultValue)
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, defaultValue);
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception)
            {
                return;
            }
            unhandledFailure = true;
            unhandledFailureMessage = Bound(condition + "\n" + stackTrace, 2048);
        }

        private static void InitializeCheckpointRun()
        {
            checkpointSequence = 0;
            checkpointStopwatch = Stopwatch.StartNew();
            string directory = UnityEngine.Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            foreach (string path in Directory.GetFiles(directory, CheckpointFilePrefix + "*.json"))
            {
                File.Delete(path);
            }
            foreach (string path in Directory.GetFiles(directory, CheckpointFilePrefix + "*.tmp"))
            {
                File.Delete(path);
            }
        }

        private static void WriteCheckpoint(string stage, string detail)
        {
            if (checkpointStopwatch == null)
            {
                checkpointStopwatch = Stopwatch.StartNew();
            }
            int sequence = Interlocked.Increment(ref checkpointSequence);
            Rma134AcceptanceCheckpoint checkpoint = new Rma134AcceptanceCheckpoint
            {
                schema_version = 1,
                sequence = sequence,
                stage = stage,
                elapsed_milliseconds = checkpointStopwatch.Elapsed.TotalMilliseconds,
                managed_thread_id = Thread.CurrentThread.ManagedThreadId,
                device_model = SystemInfo.deviceModel,
                detail = Bound(detail, 1024),
            };
            string directory = UnityEngine.Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            string finalPath = Path.Combine(
                directory,
                CheckpointFilePrefix + sequence.ToString("D3", CultureInfo.InvariantCulture) + ".json");
            string temporaryPath = finalPath + ".tmp";
            string json = JsonUtility.ToJson(checkpoint, true) + "\n";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(finalPath))
            {
                File.Delete(temporaryPath);
                throw new IOException("RMA-134 checkpoint destination already exists: " + finalPath);
            }
            File.Move(temporaryPath, finalPath);
            Debug.Log(
                "RMA-134 checkpoint " + sequence.ToString(CultureInfo.InvariantCulture) +
                " stage=" + stage + " elapsed_ms=" +
                checkpoint.elapsed_milliseconds.ToString("F3", CultureInfo.InvariantCulture));
        }

        private static void TryWriteCheckpoint(string stage, string detail)
        {
            try
            {
                WriteCheckpoint(stage, detail);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "RMA-134 could not publish checkpoint '" + stage + "': " + Bound(exception.Message, 1024));
            }
        }

        private static void WriteReport(string path, Rma134AcceptanceReport report)
        {
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters ? value : value.Substring(0, maximumCharacters);
        }
    }
}
