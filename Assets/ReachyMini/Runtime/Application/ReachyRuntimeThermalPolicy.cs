#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    /// <summary>
    /// Applies conservative runtime frame pacing before any scene loads.
    ///
    /// The CI wrappers bound Unity/editor/toolchain CPU usage. This policy covers the
    /// complementary runtime path where PlayMode, generated presentations, Android
    /// acceptance APKs, or simple scenes can otherwise render as fast as possible and
    /// heat the runner or device even when the build toolchain is bounded.
    /// </summary>
    public static class ReachyRuntimeThermalPolicy
    {
        public const int DefaultTargetFrameRate = 30;
        public const int MinimumTargetFrameRate = 15;
        public const int MaximumTargetFrameRate = 120;

        public const string TargetFrameRateEnvironmentVariable =
            "WEACHY_TARGET_FRAME_RATE";
        public const string DisableEnvironmentVariable =
            "WEACHY_DISABLE_RUNTIME_THERMAL_POLICY";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyBeforeSceneLoad()
        {
            ApplyFromEnvironment();
        }

        public static void ApplyFromEnvironment()
        {
            if (IsDisabled())
            {
                return;
            }

            Apply(ResolveTargetFrameRate());
        }

        public static int ResolveTargetFrameRate()
        {
            string? configured = Environment.GetEnvironmentVariable(
                TargetFrameRateEnvironmentVariable);
            if (int.TryParse(configured, out int targetFrameRate))
            {
                return BoundTargetFrameRate(targetFrameRate);
            }

            return DefaultTargetFrameRate;
        }

        public static void Apply(int targetFrameRate)
        {
            int boundedTarget = BoundTargetFrameRate(targetFrameRate);
            Application.targetFrameRate = boundedTarget;

#if UNITY_ANDROID || UNITY_IOS
            // Mobile Unity players ignore vSyncCount for frame pacing; rely on
            // Application.targetFrameRate so Android acceptance APKs do not render
            // unlimited frames when the scene is simple.
            QualitySettings.vSyncCount = 0;
#else
            // Desktop/editor visual paths should also avoid unconstrained rendering.
            // When VSync is available, Unity uses it for pacing on desktop players.
            QualitySettings.vSyncCount = 1;
#endif
        }

        private static bool IsDisabled()
        {
            string? disabled = Environment.GetEnvironmentVariable(
                DisableEnvironmentVariable);
            return string.Equals(disabled, "1", StringComparison.Ordinal) ||
                string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(disabled, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int BoundTargetFrameRate(int targetFrameRate)
        {
            if (targetFrameRate < MinimumTargetFrameRate)
            {
                return MinimumTargetFrameRate;
            }

            if (targetFrameRate > MaximumTargetFrameRate)
            {
                return MaximumTargetFrameRate;
            }

            return targetFrameRate;
        }
    }
}
