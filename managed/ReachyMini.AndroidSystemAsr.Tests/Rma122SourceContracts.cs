#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

internal static class Rma122SourceContracts
{
    private const string JavaBridgePath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/" +
        "src/main/java/com/ekkus93/weachy/speech/ReachySystemAsrBridge.java";
    private const string AndroidManifestPath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/AndroidManifest.xml";
    private const string UnityBridgePath =
        "Assets/ReachyMini/Runtime/Application/ReachyAndroidSystemAsrPlatform.cs";

    public static Task JavaBridgeUsesSystemRecognizerOnly()
    {
        string source = Read(JavaBridgePath);
        Require(
            source,
            "SpeechRecognizer.isRecognitionAvailable(activity)",
            "system recognition availability probe");
        Require(
            source,
            "SpeechRecognizer.createSpeechRecognizer(activity)",
            "system SpeechRecognizer factory");
        Require(
            source,
            "hasMicrophonePermission(activity)",
            "microphone permission gate");
        Require(
            source,
            "recognizer.destroy();",
            "recognizer destruction");
        Require(
            source,
            "network_failure",
            "network failure classification");
        Require(
            source,
            "no alternate ASR provider was selected",
            "no-fallback failure disclosure");
        Reject(
            source,
            "createOnDeviceSpeechRecognizer",
            "explicit on-device recognizer factory");
        Reject(
            source,
            "isOnDeviceRecognitionAvailable",
            "explicit on-device availability probe");
        Reject(
            source,
            "EXTRA_PREFER_OFFLINE",
            "offline preference hint");
        Reject(
            source,
            "triggerModelDownload",
            "automatic language-model download");
        return Task.CompletedTask;
    }

    public static Task UnityBridgeMarshalsCallbacksWithoutFallback()
    {
        string source = Read(UnityBridgePath);
        Require(
            source,
            "ReachySystemAsrBridge",
            "separate system-recognizer Java bridge");
        Require(
            source,
            "RecognitionEventQueue",
            "bounded recognition callback queue");
        Require(
            source,
            "callback_request_identity_mismatch",
            "callback request identity failure");
        Require(
            source,
            "callback_queue_overflow",
            "visible callback queue overflow");
        Require(
            source,
            "activeBridge.Call(\"cancel\", requestId)",
            "explicit cancellation bridge");
        Require(
            source,
            "value.Call(\"close\")",
            "explicit teardown bridge");
        Reject(
            source,
            "ReachyAndroidOnDeviceAsrProviderFactory",
            "RMA-121 provider fallback");
        Reject(
            source,
            "OpenAI",
            "cloud ASR fallback");
        return Task.CompletedTask;
    }

    public static Task ManifestDeclaresSpeechRequirements()
    {
        string manifest = Read(AndroidManifestPath);
        Require(manifest, "android.permission.RECORD_AUDIO", "RECORD_AUDIO permission");
        Require(manifest, "android.hardware.microphone", "optional microphone feature declaration");
        Require(manifest, "android.speech.RecognitionService", "recognition-service package visibility");
        return Task.CompletedTask;
    }

    private static string Read(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(
            Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            string projectVersion = Path.Combine(
                current.FullName,
                "ProjectSettings",
                "ProjectVersion.txt");
            if (File.Exists(projectVersion))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root for RMA-122 source contracts.");
    }

    private static void Require(string source, string expected, string description)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-122 source contract is missing " + description + ".");
        }
    }

    private static void Reject(string source, string rejected, string description)
    {
        if (source.Contains(rejected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-122 source contract found prohibited " + description + ".");
        }
    }
}
