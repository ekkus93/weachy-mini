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
        private const string AuthoritativeWrapperPath =
            "scripts/run_unity_authoritative_rendering_acceptance_android.sh";
        private const string AuthoritativeImplementationPath =
            "scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh";
        private const string WorkflowPath =
            ".github/workflows/local-unity-android-validation.yml";

        internal static void Run()
        {
            string repositoryRoot = FindRepositoryRoot();
            VerifyBridge(Read(repositoryRoot, BridgePath));
            VerifyPhysicalAcceptance(
                Read(repositoryRoot, AcceptancePath),
                Read(repositoryRoot, AcceptanceScriptPath));
            VerifyAuthoritativeInstallHarness(
                Read(repositoryRoot, AuthoritativeWrapperPath),
                Read(repositoryRoot, AuthoritativeImplementationPath));
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
                acceptance,
                "private static void InvalidateFaceRegion(",
                "detector-jitter-tolerant invalid face region");
            RequireText(
                acceptance,
                "Array.Fill(\n                    validity,\n                    (byte)0,",
                "multi-pixel invalid face region fill");
            RejectText(
                acceptance,
                "validity[centerY * width + centerX] = 0;",
                "brittle single-pixel physical invalidation");
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

        private static void VerifyAuthoritativeInstallHarness(
            string wrapper,
            string implementation)
        {
            RequireText(
                implementation,
                "\"${ADB[@]}\" uninstall \"${PACKAGE_NAME}\"",
                "clean authoritative package uninstall");
            RequireText(
                implementation,
                "package-path-after-uninstall-status.txt",
                "post-uninstall package probe status evidence");
            RequireText(
                implementation,
                "package_absence_status=$?",
                "fail-closed package absence verification");
            RequireText(
                implementation,
                "UNITY_AUTHORITATIVE_INSTALL_TIMEOUT_SECONDS",
                "bounded install timeout configuration");
            RequireText(
                implementation,
                "timeout --signal=TERM --kill-after=15s",
                "bounded streaming ADB installation");
            RequireText(
                implementation,
                "\"${INSTALL_TIMEOUT_SECONDS}s\"",
                "configured physical install deadline");
            RequireText(
                implementation,
                "\"${ADB[@]}\" install -g \"${APK_PATH}\"",
                "proven streaming install path");
            RequireText(
                implementation,
                "> \"${REPORT_DIR}/install.txt\" 2>&1",
                "direct complete install output capture");
            RequireText(
                implementation,
                "install_status=$?",
                "real bounded install exit status");
            RequireText(
                implementation,
                "cat \"${REPORT_DIR}/install.txt\"",
                "post-install console evidence publication");
            RequireText(
                implementation,
                "install_status == 124 || install_status == 137",
                "explicit TERM/KILL install timeout diagnosis");
            RequireText(
                implementation,
                "package_presence_status=$?",
                "fail-closed installed-package verification");
            RequireText(
                implementation,
                "capture_install_diagnostics",
                "installation failure evidence capture");
            RequireText(
                implementation,
                "apk-signature.txt",
                "APK signer evidence");
            RequireText(
                implementation,
                "apk-sha256.txt",
                "APK digest evidence");
            RejectExecutableText(
                implementation,
                "install --no-streaming",
                "hanging non-streaming installation");
            RejectExecutableText(
                implementation,
                "install -r",
                "state-contaminating replacement install");
            RejectExecutableText(
                implementation,
                "| tee \"${REPORT_DIR}/install.txt\"",
                "live install pipeline that can outlive timeout");

            int launch = RequireAfter(
                implementation,
                "\"${ADB[@]}\" shell am start -W",
                0,
                "authoritative launch command");
            int launchStatus = RequireAfter(
                implementation,
                "launch_status=${PIPESTATUS[0]}",
                launch,
                "authoritative launch status capture");
            _ = RequireAfter(
                implementation,
                "mv -f -- \"${launch_ready_tmp}\" \"${LAUNCH_READY_FILE}\"",
                launchStatus,
                "post-launch readiness publication");

            int readinessWait = RequireAfter(
                wrapper,
                "while [[ ! -s \"${LAUNCH_READY_FILE}\" ]]",
                0,
                "launch readiness wait");
            int processGuard = RequireAfter(
                wrapper,
                "kill -0 \"${implementation_pid}\"",
                readinessWait,
                "early implementation failure guard");
            _ = RequireAfter(
                wrapper,
                "if [[ -s \"${LAUNCH_READY_FILE}\" ]]; then",
                processGuard,
                "launch readiness race recheck");
            _ = RequireAfter(
                wrapper,
                "wait-focus",
                processGuard,
                "foreground wait after launch readiness");
            RequireText(
                wrapper,
                "LAUNCH_READY_TIMEOUT_SECONDS <= INSTALL_TIMEOUT_SECONDS + 20",
                "outer watchdog cannot preempt install evidence");
            RequireText(
                wrapper,
                "kill \"${implementation_pid}\"",
                "background implementation cleanup");
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
                AuthoritativeWrapperPath,
                AuthoritativeImplementationPath,
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

        private static void RejectExecutableText(
            string source,
            string rejected,
            string contract)
        {
            foreach (string line in source.Split('\n'))
            {
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.Contains(rejected, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Managed RMA-111 source contract failed: {contract}.");
                }
            }
        }

        private static void RejectText(
            string source,
            string rejected,
            string contract)
        {
            if (source.Contains(rejected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
        }
    }
}
