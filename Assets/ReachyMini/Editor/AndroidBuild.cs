using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string DevelopmentOutput =
            "Builds/Android/weachy-mini-development.apk";
        private const string DeviceFeasibilityOutput =
            "Builds/Android/weachy-mini-device-arm64-api26.apk";
        private const string ReleaseOutput =
            "Builds/Android/weachy-mini-release.aab";
        private const string CompileSdkPackageEnvironmentVariable =
            "WEACHY_ANDROID_COMPILE_SDK_PACKAGE";
        private const string PhysicalAcceptanceDefine =
            "WEACHY_PHYSICAL_ACCEPTANCE";
        private const string NativePluginDirectory =
            "Assets/Plugins/Android/libs/arm64-v8a";
        private const string RuntimeResourceDirectory =
            "Assets/Generated/ReachyMini/UnityPresentation/Resources/ReachyMiniRuntime";

        private const int ApplicationMinimumApiLevel = 31;
        private const int DeviceFeasibilityMinimumApiLevel = 26;
        private const int TargetApiLevel = 37;

        public static void BuildDevelopmentApk()
        {
            ConfigureAndroid(
                buildAppBundle: false,
                AndroidArchitecture.ARM64,
                ApplicationMinimumApiLevel,
                physicalAcceptance: false);
            Build(DevelopmentOutput, BuildOptions.Development);
        }

        public static void BuildDeviceFeasibilityApk()
        {
            ConfigureAndroid(
                buildAppBundle: false,
                AndroidArchitecture.ARM64,
                DeviceFeasibilityMinimumApiLevel,
                physicalAcceptance: true);
            Build(DeviceFeasibilityOutput, BuildOptions.Development);
        }

        public static void BuildReleaseAab()
        {
            ConfigureAndroid(
                buildAppBundle: true,
                AndroidArchitecture.ARM64,
                ApplicationMinimumApiLevel,
                physicalAcceptance: false);
            Build(ReleaseOutput, BuildOptions.None);
        }

        private static void ConfigureAndroid(
            bool buildAppBundle,
            AndroidArchitecture targetArchitecture,
            int minimumApiLevel,
            bool physicalAcceptance)
        {
            ConfigureAndroidSdk();

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android,
                    BuildTarget.Android))
            {
                throw new InvalidOperationException(
                    "Unity could not activate the Android build target.");
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
            ConfigureScriptingDefines(physicalAcceptance);
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
        }

        private static void ConfigureScriptingDefines(bool physicalAcceptance)
        {
            string existing = PlayerSettings.GetScriptingDefineSymbols(
                NamedBuildTarget.Android);
            HashSet<string> defines = new HashSet<string>(
                existing.Split(
                    new[] { ';' },
                    StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            if (physicalAcceptance)
            {
                defines.Add(PhysicalAcceptanceDefine);
            }
            else
            {
                defines.Remove(PhysicalAcceptanceDefine);
            }
            PlayerSettings.SetScriptingDefineSymbols(
                NamedBuildTarget.Android,
                defines.OrderBy(value => value, StringComparer.Ordinal).ToArray());
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

            string compileSdkPackage = Environment.GetEnvironmentVariable(
                CompileSdkPackageEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(compileSdkPackage))
            {
                throw new InvalidOperationException(
                    $"{CompileSdkPackageEnvironmentVariable} must identify the SDK platform " +
                    "package pinned by toolchain.lock.json.");
            }

            if (!compileSdkPackage.All(character =>
                    char.IsDigit(character) || character == '.') ||
                compileSdkPackage.StartsWith(".", StringComparison.Ordinal) ||
                compileSdkPackage.EndsWith(".", StringComparison.Ordinal) ||
                compileSdkPackage.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid Android SDK package version: {compileSdkPackage}");
            }

            string expectedApiPrefix =
                TargetApiLevel.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(
                    compileSdkPackage,
                    expectedApiPrefix,
                    StringComparison.Ordinal) &&
                !compileSdkPackage.StartsWith(
                    expectedApiPrefix + ".",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Android SDK package {compileSdkPackage} does not match target API " +
                    $"{TargetApiLevel}.");
            }

            string normalizedSdkRoot = Path.GetFullPath(sdkRoot);
            string requiredPlatformJar = Path.Combine(
                normalizedSdkRoot,
                "platforms",
                $"android-{compileSdkPackage}",
                "android.jar");
            if (!File.Exists(requiredPlatformJar))
            {
                throw new FileNotFoundException(
                    $"Android SDK platform package android-{compileSdkPackage} is missing.",
                    requiredPlatformJar);
            }

            AndroidExternalToolsSettings.sdkRootPath = normalizedSdkRoot;
            string configuredSdkRoot =
                Path.GetFullPath(AndroidExternalToolsSettings.sdkRootPath);
            if (!string.Equals(
                    configuredSdkRoot.TrimEnd(Path.DirectorySeparatorChar),
                    normalizedSdkRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Unity did not retain the requested Android SDK path. " +
                    $"Expected {normalizedSdkRoot}, found {configuredSdkRoot}.");
            }
        }

        private static void Build(string outputPath, BuildOptions options)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (!File.Exists(ReachyPresentationBuilder.ScenePath))
            {
                throw new FileNotFoundException(
                    "Generated Reachy presentation scene is missing. " +
                    "Run the presentation preparation command before building.",
                    ReachyPresentationBuilder.ScenePath);
            }
            if (scenes.Length != 1 ||
                !string.Equals(
                    scenes[0],
                    ReachyPresentationBuilder.ScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The generated Reachy presentation scene must be the sole enabled " +
                    "Unity build scene.");
            }
            ValidateProductionRuntimeAssets();

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

        private static void ValidateProductionRuntimeAssets()
        {
            string[] requiredPaths =
            {
                $"{NativePluginDirectory}/libmujoco.so",
                $"{NativePluginDirectory}/libreachy_sim.so",
                $"{RuntimeResourceDirectory}/reachy_mini_mjb.bytes",
                $"{RuntimeResourceDirectory}/runtime_manifest_json.bytes",
            };
            foreach (string path in requiredPaths)
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length <= 0)
                {
                    throw new FileNotFoundException(
                        "The production Unity Android runtime was not staged.",
                        path);
                }
            }
        }
    }
}
