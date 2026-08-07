#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

internal static class Rma121SourceContracts
{
    private const string JavaBridgePath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/" +
        "src/main/java/com/ekkus93/weachy/speech/" +
        "ReachyOnDeviceAsrBridge.java";
    private const string AndroidManifestPath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/AndroidManifest.xml";
    private const string UnityBridgePath =
        "Assets/ReachyMini/Runtime/Application/ReachyAndroidOnDeviceAsrPlatform.cs";

    public static Task JavaBridgeUsesOnlyExplicitOnDeviceRecognizer()
    {
        string source = Read(JavaBridgePath);
        Require(
            source,
            "SpeechRecognizer.createOnDeviceSpeechRecognizer(context)",
            "explicit on-device recognizer factory");
        Require(
            source,
            "SpeechRecognizer.isOnDeviceRecognitionAvailable(context)",
            "explicit on-device availability probe");
        Require(
            source,
            "recognizer.checkRecognitionSupport(intent, context.getMainExecutor(), callback);",
            "API 33 language support check");
        Require(
            source,
            "support.getOnlineLanguages()",
            "online-only language rejection");
        Require(
            source,
            "recognizer.destroy();",
            "recognizer destruction");
        Require(
            source,
            "hasMicrophonePermission(activity)",
            "microphone permission gate");
        Reject(
            source,
            "SpeechRecognizer.createSpeechRecognizer(",
            "system SpeechRecognizer factory");
        Reject(
            source,
            "intent.putExtra(\n                    RecognizerIntent.EXTRA_PREFER_OFFLINE",
            "EXTRA_PREFER_OFFLINE intent mutation");
        Reject(
            source,
            "triggerModelDownload(",
            "automatic language-model download");
        return Task.CompletedTask;
    }

    public static Task ManifestDeclaresAudioAndRecognitionServiceVisibility()
    {
        string manifest = Read(AndroidManifestPath);
        Require(
            manifest,
            "android.permission.RECORD_AUDIO",
            "RECORD_AUDIO permission");
        Require(
            manifest,
            "android.hardware.microphone",
            "optional microphone feature declaration");
        Require(
            manifest,
            "android.speech.RecognitionService",
            "speech recognition service package visibility");
        return Task.CompletedTask;
    }

    public static Task UnityBridgeMarshalsCallbacksWithoutFallback()
    {
        string source = Read(UnityBridgePath);
        Require(
            source,
            "TaskCreationOptions.RunContinuationsAsynchronously",
            "asynchronous callback continuation boundary");
        Require(
            source,
            "RecognitionEventQueue",
            "thread-safe recognition callback queue");
        Require(
            source,
            "callback_request_identity_mismatch",
            "callback request identity failure");
        Require(
            source,
            "callback_queue_overflow",
            "visible bounded callback overflow");
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
            "createSpeechRecognizer",
            "system recognizer fallback in Unity bridge");
        Reject(
            source,
            "OpenAI",
            "cloud ASR fallback in Unity bridge");
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
        DirectoryInfo? current =
            new DirectoryInfo(Directory.GetCurrentDirectory());
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
            "Could not locate the repository root for RMA-121 source contracts.");
    }

    private static void Require(
        string source,
        string expected,
        string description)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-121 source contract is missing " + description + ".");
        }
    }

    private static void Reject(
        string source,
        string rejected,
        string description)
    {
        if (source.Contains(rejected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-121 source contract found prohibited " + description + ".");
        }
    }
}
