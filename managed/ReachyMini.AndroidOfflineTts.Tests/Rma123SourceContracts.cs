#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

internal static class Rma123SourceContracts
{
    private const string JavaBridgePath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/" +
        "src/main/java/com/ekkus93/weachy/speech/ReachyOfflineTtsBridge.java";
    private const string UnityBridgePath =
        "Assets/ReachyMini/Runtime/Application/ReachyAndroidOfflineTtsPlatform.cs";
    private const string UnityManifestPath =
        "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/AndroidManifest.xml";
    private const string HostedManifestPath =
        "android-plugin/src/main/AndroidManifest.xml";

    public static Task JavaBridgeEnforcesOfflineVoiceOnly()
    {
        string source = Read(JavaBridgePath);
        Require(source, "new TextToSpeech(", "asynchronous TextToSpeech initialization");
        Require(source, "status ->", "TextToSpeech OnInit listener");
        Require(source, "voice.isNetworkConnectionRequired()", "per-voice network requirement inspection");
        Require(source, "TextToSpeech.Engine.KEY_FEATURE_NOT_INSTALLED", "missing voice-data inspection");
        Require(source, "tts.setVoice(voice)", "exact voice selection");
        Require(source, "Voice selected = tts.getVoice()", "post-selection exact-voice verification");
        Require(source, "TextToSpeech.QUEUE_ADD", "non-replacing synthesis queue mode");
        Require(source, "UtteranceProgressListener", "utterance lifecycle listener");
        Require(source, "public void onStart", "start callback");
        Require(source, "public void onDone", "done callback");
        Require(source, "public void onStop", "stop callback");
        Require(source, "public void onError", "error callback");
        Require(source, "pendingInitialization.removeIf", "cancellation of pre-initialization work");
        Require(source, "tts.shutdown();", "TextToSpeech shutdown");
        Require(source, "no alternate TTS provider was selected", "no-fallback failure disclosure");
        Reject(source, "setLanguage(", "locale closest-match selection");
        Reject(source, "QUEUE_FLUSH", "implicit replacement of queued speech");
        Reject(source, "startActivity(", "automatic voice-data installation UI launch");
        Reject(source, "OpenAI", "cloud TTS fallback");
        Reject(source, "http://", "network endpoint");
        Reject(source, "https://", "network endpoint");
        return Task.CompletedTask;
    }

    public static Task UnityBridgeMarshalsWithoutFallback()
    {
        string source = Read(UnityBridgePath);
        Require(source, "ReachyOfflineTtsBridge", "dedicated offline TTS Java bridge");
        Require(source, "SpeechEventQueue", "bounded utterance callback queue");
        Require(source, "callback_request_identity_mismatch", "callback request identity failure");
        Require(source, "callback_queue_overflow", "visible callback queue overflow");
        Require(source, "activeBridge.Call(\"cancel\", requestId)", "explicit cancellation bridge");
        Require(source, "value.Call(\"close\")", "explicit teardown bridge");
        Reject(source, "ReachyAndroidSystemAsrProviderFactory", "ASR provider substitution");
        Reject(source, "OpenAI", "cloud TTS fallback");
        Reject(source, "HttpClient", "network transport");
        return Task.CompletedTask;
    }

    public static Task ManifestsDeclareTtsServiceVisibility()
    {
        string unityManifest = Read(UnityManifestPath);
        string hostedManifest = Read(HostedManifestPath);
        Require(
            unityManifest,
            "android.intent.action.TTS_SERVICE",
            "Unity TTS service package visibility");
        Require(
            hostedManifest,
            "android.intent.action.TTS_SERVICE",
            "hosted Android TTS service package visibility");
        return Task.CompletedTask;
    }

    private static string Read(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
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
            "Could not locate the repository root for RMA-123 source contracts.");
    }

    private static void Require(string source, string expected, string description)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-123 source contract is missing " + description + ".");
        }
    }

    private static void Reject(string source, string rejected, string description)
    {
        if (source.Contains(rejected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RMA-123 source contract found prohibited " + description + ".");
        }
    }
}
