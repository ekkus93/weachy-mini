using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ReachyMini.Editor
{
    public static class AndroidBuild
    {
        private const string DevelopmentOutput = "Builds/Android/weachy-mini-development.apk";
        private const string DeviceFeasibilityOutput =
            "Builds/Android/weachy-mini-device-arm64-api26.apk";
        private const string ReleaseOutput = "Builds/Android/weachy-mini-release.aab";

        private const int ApplicationMinimumApiLevel = 31;
        private const int DeviceFeasibilityMinimumApiLevel = 26;
        private const int TargetApiLevel = 37;

        public static void BuildDevelopmentApk()
        {
            ConfigureAndroid(
                buildAppBundle: false,
                AndroidArchitecture.ARM64,
                ApplicationMinimumApiLevel);
            Build(DevelopmentOutput, BuildOptions.Development);
        }

        public static void BuildDeviceFeasibilityApk()
        {
            ConfigureAndroid(
                buildAppBundle: false,
                AndroidArchitecture.ARM64,
                DeviceFeasibilityMinimumApiLevel);
            Build(DeviceFeasibilityOutput, BuildOptions.Development);
        }

        public static void BuildReleaseAab()
        {
            ConfigureAndroid(
                buildAppBundle: true,
                AndroidArchitecture.ARM64,
                ApplicationMinimumApiLevel);
            Build(ReleaseOutput, BuildOptions.None);
        }

        private static void ConfigureAndroid(
            bool buildAppBundle,
            AndroidArchitecture targetArchitecture,
            int minimumApiLevel)
        {
            ConfigureAndroidSdk();

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
            PlayerSettings.Android.minSdkVersion =
                (AndroidSdkVersions)minimumApiLevel;
            PlayerSettings.Android.targetSdkVersion =
                (AndroidSdkVersions)TargetApiLevel;
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                "com.ekkus.weachymini");
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
        }

        private static void ConfigureAndroidSdk()
        {
            string sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (string.IsNullOrWhiteSpace(sdkRoot))
            {
                sdkRoot = Environment.GetEnvironmentVariable("ANDROID_HOME");
            }

            if (string.IsNullOrWhiteSpace(sdkRoot))
            {
                throw new InvalidOperationException(
                    "ANDROID_SDK_ROOT or ANDROID_HOME must identify the provisioned Android SDK.");
            }

            string normalizedSdkRoot = Path.GetFullPath(sdkRoot);
            string requiredPlatform = Path.Combine(
                normalizedSdkRoot,
                "platforms",
                $"android-{TargetApiLevel}");
            if (!Directory.Exists(requiredPlatform))
            {
                throw new DirectoryNotFoundException(
                    $"Android SDK platform {TargetApiLevel} is missing: {requiredPlatform}");
            }

            AndroidExternalToolsSettings.sdkRootPath = normalizedSdkRoot;
            string configuredSdkRoot = Path.GetFullPath(AndroidExternalToolsSettings.sdkRootPath);
            if (!string.Equals(
                    configuredSdkRoot.TrimEnd(Path.DirectorySeparatorChar),
                    normalizedSdkRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unity did not retain the requested Android SDK path. " +
                    $"Expected {normalizedSdkRoot}, found {configuredSdkRoot}.");
            }
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

            string outputDirectory = Path.GetDirectoryName(outputPath);
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
