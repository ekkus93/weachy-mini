#nullable enable

using System;
using System.IO;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma111AndroidBridgeSourceContracts
    {
        private const string BridgePath =
            "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/" +
            "src/main/java/com/ekkus93/weachy/camera/" +
            "ReachyMlKitTrackingBridge.java";
        private const string AcceptancePath =
            "Assets/ReachyMini/Runtime/Application/" +
            "ReachyRma111TrackingAcceptance.cs";
        private const string AcceptanceScriptPath =
            "scripts/run_rma111_lightweight_tracking_acceptance_android.sh";
        private const string WorkflowPath =
            ".github/workflows/local-unity-android-validation.yml";

        internal static void Run()
        {
            string repositoryRoot = FindRepositoryRoot();
            VerifyBridge(Read(repositoryRoot, BridgePath));
            VerifyPhysicalAcceptance(
                Read(repositoryRoot, AcceptancePath),
                Read(repositoryRoot, AcceptanceScriptPath));
            VerifyPinnedDeviceWorkflow(
                repositoryRoot,
                Read(repositoryRoot, WorkflowPath));
        }

        private static void VerifyBridge(string source)
        {
            int inputImage = RequireAfter(
                source,
                "image = InputImage.fromBitmap(bitmap, 0);",
                0,
                "input image creation");
            int faceStart = RequireAfter(
                source,
                "faceTask = faceDetector.process(image);",
                inputImage,
                "face task start");
            int faceListeners = RequireAfter(
                source,
                "faceTask\n                .addOnSuccessListener",
                faceStart,
                "face listener attachment");
            int personStart = RequireAfter(
                source,
                "personTask = personSegmenter.process(image);",
                faceListeners,
                "person task start");
            _ = RequireAfter(
                source,
                "personTask\n                .addOnSuccessListener",
                personStart,
                "person listener attachment");

            RequireText(
                source,
                "failImmediately(state, \"input_image_failed\", error);",
                "input-image failure cleanup");
            RequireText(
                source,
                "failImmediately(state, \"face_detection_start_failed\", error);",
                "face synchronous-start failure cleanup");
            RequireText(
                source,
                "failPart(state, \"person_segmentation_start_failed\", error);",
                "person synchronous-start failure drain");
            RequireText(
                source,
                "failPart(state, \"person_segmentation_result_failed\", error);",
                "person result failure drain");
            RequireText(
                source,
                "private void failImmediately(",
                "immediate failure cleanup helper");
            RequireText(source, "activeRequest = null;", "active request release");
            RequireText(source, "state.bitmap.recycle();", "bitmap release");
        }

        private static void VerifyPhysicalAcceptance(
            string acceptance,
            string script)
        {
            RequireText(
                acceptance,
                "Bundled ML Kit did not detect a person",
                "physical person detection requirement");
            RequireText(
                acceptance,
                "stable_person_id",
                "stable person report field");
            RequireText(
                acceptance,
                "first_person_id",
                "first person report ID");
            RequireText(
                acceptance,
                "second_person_id",
                "second person report ID");
            RequireText(
                script,
                "\"stable_person_id\"",
                "stable person shell gate");
            RequireText(
                script,
                "second_person_count",
                "second-frame person shell gate");
            RequireText(
                script,
                "first_person_id",
                "person ID shell gate");
        }

        private static void VerifyPinnedDeviceWorkflow(
            string repositoryRoot,
            string workflow)
        {
            RequireText(
                workflow,
                "- name: Pin one physical Android device",
                "single physical-device pin step");
            RequireText(
                workflow,
                "REACHY_ANDROID_SERIAL=%s",
                "pinned serial environment export");
            RequireText(
                workflow,
                "${#accepted[@]} != 1",
                "ambiguous-device fail-closed gate");

            string[] scripts =
            {
                "scripts/run_rma090_camera_discovery_acceptance_android.sh",
                "scripts/run_rma091_camera_acquisition_acceptance_android.sh",
                "scripts/run_rma092_camera_texture_acceptance_android.sh",
                "scripts/run_rma111_lightweight_tracking_acceptance_android.sh",
                "scripts/run_unity_native_lifecycle_acceptance_android.sh",
                "scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh",
            };
            foreach (string relativePath in scripts)
            {
                RequireText(
                    Read(repositoryRoot, relativePath),
                    "REACHY_ANDROID_SERIAL:-",
                    $"pinned serial consumption in {relativePath}");
            }
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
                string bridge = Path.Combine(
                    current.FullName,
                    BridgePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(projectVersion) && File.Exists(bridge))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new InvalidOperationException(
                "Could not locate the repository root for the RMA-111 source contracts.");
        }

        private static string Read(string root, string relativePath)
        {
            return File.ReadAllText(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static int RequireAfter(
            string source,
            string expected,
            int startIndex,
            string contract)
        {
            int index = source.IndexOf(
                expected,
                startIndex,
                StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
            return index;
        }

        private static void RequireText(
            string source,
            string expected,
            string contract)
        {
            if (!source.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
        }
    }
}
