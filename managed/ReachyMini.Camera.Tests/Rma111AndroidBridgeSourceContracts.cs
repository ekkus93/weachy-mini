#nullable enable

using System;
using System.IO;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma111AndroidBridgeSourceContracts
    {
        private const string RelativeBridgePath =
            "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/" +
            "src/main/java/com/ekkus93/weachy/camera/" +
            "ReachyMlKitTrackingBridge.java";

        internal static void Run()
        {
            string repositoryRoot = FindRepositoryRoot();
            string sourcePath = Path.Combine(
                repositoryRoot,
                RelativeBridgePath.Replace('/', Path.DirectorySeparatorChar));
            string source = File.ReadAllText(sourcePath);

            int faceStart = RequireAfter(
                source,
                "faceTask = faceDetector.process(image);",
                0,
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
                "failImmediately(state, \"face_detection_start_failed\", error);",
                "face synchronous-start failure cleanup");
            RequireText(
                source,
                "failPart(state, \"person_segmentation_start_failed\", error);",
                "person synchronous-start failure drain");
            RequireText(
                source,
                "private void failImmediately(",
                "immediate failure cleanup helper");
            RequireText(
                source,
                "activeRequest = null;",
                "active request release");
            RequireText(
                source,
                "state.bitmap.recycle();",
                "bitmap release");
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
                    RelativeBridgePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                if (File.Exists(projectVersion) && File.Exists(bridge))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new InvalidOperationException(
                "Could not locate the repository root for the RMA-111 Android bridge source contract.");
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
