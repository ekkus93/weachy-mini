using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ReachyMini.Editor
{
    public static class AndroidBuild
    {
        private const string DevelopmentOutput = "Builds/Android/weachy-mini-development.apk";
        private const string EmulatorOutput = "Builds/Android/weachy-mini-emulator-x86_64.apk";
        private const string ReleaseOutput = "Builds/Android/weachy-mini-release.aab";

        public static void BuildDevelopmentApk()
        {
            ConfigureAndroid(buildAppBundle: false, AndroidArchitecture.ARM64);
            Build(DevelopmentOutput, BuildOptions.Development);
        }

        public static void BuildEmulatorApk()
        {
            ConfigureAndroid(buildAppBundle: false, AndroidArchitecture.X86_64);
            Build(EmulatorOutput, BuildOptions.Development);
        }

        public static void BuildReleaseAab()
        {
            ConfigureAndroid(buildAppBundle: true, AndroidArchitecture.ARM64);
            Build(ReleaseOutput, BuildOptions.None);
        }

        private static void ConfigureAndroid(
            bool buildAppBundle,
            AndroidArchitecture targetArchitecture)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android,
                    BuildTarget.Android))
            {
                throw new InvalidOperationException("Unity could not activate the Android build target.");
            }

            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = targetArchitecture;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel31;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)37;
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                "com.ekkus.weachymini");
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
        }

        private static void Build(string outputPath, BuildOptions options)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No enabled Unity scenes exist. Create the bootstrap scene before building.");
            }

            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException(
                    $"Android build output does not contain a directory: {outputPath}");
            }
            Directory.CreateDirectory(outputDirectory);

            BuildPlayerOptions buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = options,
            };
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android build failed with result {report.summary.result}.");
            }
        }
    }
}
