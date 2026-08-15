#nullable enable

using System;
using ReachyMini.LocalModels;
using ReachyMini.Performance;
using UnityEngine;
using UnityEngine.Profiling;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    internal sealed class ReachyPerformanceRuntimeProbe : MonoBehaviour
    {
        internal const float ResourceSampleIntervalSeconds = 10.0f;

        private ReachyAndroidLocalLlmResourceSignalSource? androidResources;
        private bool previousSessionActive;
        private double nextResourceSampleTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("ReachyPerformanceRuntimeProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyPerformanceRuntimeProbe>();
        }

        private void Update()
        {
            bool active = ReachyPerformanceTelemetry.IsSessionActive;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!active)
            {
                previousSessionActive = false;
                return;
            }

            if (!previousSessionActive)
            {
                previousSessionActive = true;
                nextResourceSampleTime = now;
            }
            if (now < nextResourceSampleTime)
            {
                return;
            }

            CaptureResourceSample(now);
            nextResourceSampleTime = now + ResourceSampleIntervalSeconds;
        }

        private void LateUpdate()
        {
            if (!ReachyPerformanceTelemetry.IsSessionActive)
            {
                return;
            }

            double frameSeconds = Time.unscaledDeltaTime;
            if (frameSeconds <= 0.0 ||
                double.IsNaN(frameSeconds) ||
                double.IsInfinity(frameSeconds))
            {
                return;
            }
            ReachyPerformanceTelemetry.RecordDurationSeconds(
                ReachyPerformanceWorkload.UnityRendering,
                frameSeconds);
        }

        private void CaptureResourceSample(double monotonicSeconds)
        {
            long? allocatedBytes = null;
            long? availableBytes = null;
            double? batteryLevel = null;
            int? thermalSeverity = null;
            string thermalState = "unavailable";
            string unavailableReason = string.Empty;

            try
            {
                long allocated = Profiler.GetTotalAllocatedMemoryLong();
                if (allocated >= 0L)
                {
                    allocatedBytes = allocated;
                }
            }
            catch (Exception exception)
            {
                unavailableReason = AppendUnavailable(
                    unavailableReason,
                    "unity_memory:" + exception.GetType().Name);
            }

            float battery = SystemInfo.batteryLevel;
            if (!float.IsNaN(battery) && battery >= 0.0f && battery <= 1.0f)
            {
                batteryLevel = battery;
            }
            else
            {
                unavailableReason = AppendUnavailable(
                    unavailableReason,
                    "battery:unavailable");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                androidResources ??=
                    new ReachyAndroidLocalLlmResourceSignalSource();
                LocalLlmResourceSnapshot snapshot = androidResources.Capture(
                    LocalLlmPhysicsBudgetState.Unavailable);
                if (snapshot.TotalMemoryBytes > 0L)
                {
                    availableBytes = snapshot.AvailableMemoryBytes;
                }
                thermalSeverity = (int)snapshot.ThermalStatus;
                thermalState = snapshot.ThermalStatus.ToString();
            }
            catch (Exception exception)
            {
                unavailableReason = AppendUnavailable(
                    unavailableReason,
                    "android_resource:" + exception.GetType().Name);
            }
#else
            unavailableReason = AppendUnavailable(
                unavailableReason,
                "android_resource:not_android_player");
#endif

            ReachyPerformanceTelemetry.RecordResourceSample(
                new ReachyPerformanceResourceSample(
                    monotonicSeconds,
                    allocatedBytes,
                    availableBytes,
                    batteryLevel,
                    thermalSeverity,
                    thermalState,
                    unavailableReason));
        }

        private void OnDestroy()
        {
            androidResources?.Dispose();
            androidResources = null;
        }

        private static string AppendUnavailable(string current, string value)
        {
            string next = string.IsNullOrEmpty(current)
                ? value
                : current + ";" + value;
            return next.Length <= 512
                ? next
                : next.Substring(0, 512);
        }
    }
}
