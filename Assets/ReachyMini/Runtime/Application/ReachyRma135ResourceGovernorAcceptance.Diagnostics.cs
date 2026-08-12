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
    internal sealed partial class ReachyRma135ResourceGovernorAcceptance
    {
        private static bool ReadBooleanLaunchExtra(string name, bool defaultValue)
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<bool>("getBooleanExtra", name, defaultValue);
        }

        private static int ReadApiLevel()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }

        private static int TryReadApiLevel()
        {
            try
            {
                return ReadApiLevel();
            }
            catch
            {
                return 0;
            }
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
            var checkpoint = new Rma135AcceptanceCheckpoint
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
            File.WriteAllText(
                temporaryPath,
                JsonUtility.ToJson(checkpoint, true) + "\n",
                new UTF8Encoding(false));
            if (File.Exists(finalPath))
            {
                File.Delete(temporaryPath);
                throw new IOException(
                    "RMA-135 checkpoint destination already exists: " + finalPath);
            }
            File.Move(temporaryPath, finalPath);
            Debug.Log(
                "RMA-135 checkpoint " + sequence.ToString(CultureInfo.InvariantCulture) +
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
                    "RMA-135 could not publish checkpoint '" + stage + "': " +
                    Bound(exception.Message, 1024));
            }
        }

        private static void WriteReport(string path, Rma135AcceptanceReport report)
        {
            File.WriteAllText(
                path,
                JsonUtility.ToJson(report, true) + "\n",
                new UTF8Encoding(false));
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
        }
    }
}
