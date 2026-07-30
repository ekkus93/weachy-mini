#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ReachyMini.Core;
using ReachyMini.Interop;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Rendering
{
    [DisallowMultipleComponent]
    public sealed class ReachyNativeLifecycleAcceptance : MonoBehaviour
    {
        public const string ResultFileName =
            "weachy-native-lifecycle-acceptance.json";
        public const string LaunchExtraName =
            "weachy_lifecycle_acceptance";

        private const string ModelResourcePath =
            "ReachyMiniRuntime/reachy_mini_mjb";
        private const int RequiredPauseResumeCycles = 2;
        private const double MaximumResumeSimulationAdvanceSeconds = 0.75;
        private const float StartupTimeoutSeconds = 30.0f;
        private const float LifecycleTimeoutSeconds = 60.0f;

        private readonly List<LifecycleCycleReport> cycles =
            new List<LifecycleCycleReport>();

        private string displayMessage = "Native lifecycle acceptance is starting.";
        private bool acceptanceEnabled;
        private bool complete;
        private int pauseCallbackCount;
        private int resumeCallbackCount;
        private long lastPauseUtcTicks;
        private long lastResumeUtcTicks;

        public bool IsAcceptanceEnabled => acceptanceEnabled;

        public bool IsComplete => complete;

        public string ResultPath => Path.Combine(
            Application.persistentDataPath,
            ResultFileName);

        private IEnumerator Start()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                enabled = false;
                yield break;
            }

            bool requested = TryReadAcceptanceRequest(out string requestError);
            if (!string.IsNullOrEmpty(requestError))
            {
                acceptanceEnabled = true;
                Fail(requestError);
                yield break;
            }
            if (!requested)
            {
                enabled = false;
                yield break;
            }

            acceptanceEnabled = true;
            PublishProgress(
                "component_started",
                cycle: 0,
                "The RMA-022 native lifecycle component started.");
            yield return RunAcceptance();
        }

        private IEnumerator RunAcceptance()
        {
            ReachyProductionAuthoritativeRuntime runtime =
                GetComponent<ReachyProductionAuthoritativeRuntime>();
            ReachyAuthoritativeRenderer renderer =
                GetComponent<ReachyAuthoritativeRenderer>();
            if (runtime == null || renderer == null)
            {
                Fail("The lifecycle scene is missing the production runtime or renderer.");
                yield break;
            }

            PublishProgress(
                "waiting_for_runtime",
                cycle: 0,
                "Waiting for native creation, the worker, and authoritative publication.");
            float startupDeadline = Time.realtimeSinceStartup + StartupTimeoutSeconds;
            while (runtime.Status != ReachyProductionRuntimeStatus.Running)
            {
                if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    Fail($"Production runtime startup failed: {runtime.Fault}");
                    yield break;
                }
                if (Time.realtimeSinceStartup >= startupDeadline)
                {
                    Fail($"Timed out waiting for native startup; runtime={runtime.Status}.");
                    yield break;
                }
                yield return null;
            }

            if (!TryReadLatestState(runtime, out ReachyAuthoritativePoseSnapshot initialState))
            {
                Fail("The production runtime did not publish an initial authoritative state pair.");
                yield break;
            }

            uint nativeAbiVersion;
            string nativeVersion;
            try
            {
                nativeAbiVersion = NativeReachySim.AbiVersion();
                nativeVersion = Marshal.PtrToStringAnsi(NativeReachySim.VersionString()) ??
                    string.Empty;
            }
            catch (Exception exception)
            {
                Fail($"The IL2CPP native version call failed: {exception.Message}");
                yield break;
            }
            if (nativeAbiVersion != ProjectMetadata.NativeAbiVersion ||
                string.IsNullOrWhiteSpace(nativeVersion))
            {
                Fail(
                    $"Native version mismatch: managed ABI={ProjectMetadata.NativeAbiVersion}, " +
                    $"native ABI={nativeAbiVersion}, version='{nativeVersion}'.");
                yield break;
            }
            PublishProgress(
                "native_version_resolved",
                cycle: 0,
                $"Resolved native ABI {nativeAbiVersion} and version {nativeVersion}.");

            ReachySimCreateResult malformedCreate = ReachySimSession.Create(
                new byte[] { 0x4d, 0x4a, 0x42, 0x00, 0x52, 0x4d, 0x41, 0x2d, 0x30, 0x32, 0x32 });
            if (malformedCreate.IsSuccess || malformedCreate.Session != null ||
                malformedCreate.Error.Code == ReachySimErrorCode.Ok ||
                string.IsNullOrWhiteSpace(malformedCreate.Error.Message))
            {
                malformedCreate.Session?.Dispose();
                Fail("A deliberately malformed model did not produce a structured native initialization failure.");
                yield break;
            }
            displayMessage =
                $"Controlled native initialization failure: {malformedCreate.Error.Code}.";
            Debug.LogWarning(
                $"RMA-022 controlled native initialization failure: " +
                $"{malformedCreate.Error.Code}: {malformedCreate.Error.Message}",
                this);
            PublishProgress(
                "controlled_initialization_failure_observed",
                cycle: 0,
                $"Observed {malformedCreate.Error.Code}: {malformedCreate.Error.Message}");

            TextAsset? modelAsset = Resources.Load<TextAsset>(ModelResourcePath);
            if (modelAsset == null || modelAsset.bytes.Length == 0)
            {
                Fail("The staged production MJB is unavailable for the native lifecycle probe.");
                yield break;
            }

            bool probeStepped = false;
            bool probeDestroyed = false;
            bool operationAfterCloseRejected = false;
            ReachySimCreateResult probeCreate = ReachySimSession.Create(modelAsset.bytes);
            if (!probeCreate.IsSuccess || probeCreate.Session == null)
            {
                Fail(
                    $"The valid lifecycle probe could not create a native session: " +
                    $"{probeCreate.Error.Code}: {probeCreate.Error.Message}");
                yield break;
            }
            ReachySimSession probeSession = probeCreate.Session;
            try
            {
                ReachySimOperationResult stepResult = probeSession.Step(2U);
                if (!stepResult.IsSuccess)
                {
                    Fail(
                        $"The valid lifecycle probe could not step: " +
                        $"{stepResult.Error.Code}: {stepResult.Error.Message}");
                    yield break;
                }
                probeStepped = true;

                ReachySimOperationResult closeResult = probeSession.Close();
                if (!closeResult.IsSuccess)
                {
                    Fail(
                        $"The valid lifecycle probe could not destroy its handle: " +
                        $"{closeResult.Error.Code}: {closeResult.Error.Message}");
                    yield break;
                }
                probeDestroyed = true;
                try
                {
                    probeSession.Step(1U);
                }
                catch (ObjectDisposedException)
                {
                    operationAfterCloseRejected = true;
                }
                if (!operationAfterCloseRejected)
                {
                    Fail("The managed wrapper allowed an operation after native handle destruction.");
                    yield break;
                }
            }
            finally
            {
                probeSession.Dispose();
            }
            PublishProgress(
                "valid_probe_destroyed",
                cycle: 0,
                "A valid native session loaded, stepped, closed, and rejected reuse.");

            ulong initialSequence = initialState.Sequence;
            double initialSimulationTime = initialState.SimulationTime;
            for (int cycle = 1; cycle <= RequiredPauseResumeCycles; ++cycle)
            {
                yield return RunPauseResumeCycle(runtime, cycle);
                if (complete)
                {
                    yield break;
                }
            }

            if (runtime.Status != ReachyProductionRuntimeStatus.Running ||
                !TryReadLatestState(runtime, out ReachyAuthoritativePoseSnapshot finalState))
            {
                Fail("The production runtime was not healthy after repeated resume.");
                yield break;
            }

            PublishProgress(
                "destroying_production_runtime",
                cycle: 0,
                "Destroying the production component to exercise its application-shutdown path.");
            UnityEngine.Object.Destroy(runtime);
            yield return null;
            yield return null;

            bool productionRuntimeDestroyed = runtime == null;
            bool rendererDisabled = !renderer.enabled;
            if (!productionRuntimeDestroyed || !rendererDisabled)
            {
                Fail(
                    $"Production shutdown did not complete visibly: " +
                    $"runtime_destroyed={productionRuntimeDestroyed}, " +
                    $"renderer_disabled={rendererDisabled}.");
                yield break;
            }

            LifecycleReport report = new LifecycleReport
            {
                status = "ok",
                native_abi_version = nativeAbiVersion,
                native_version = nativeVersion,
                model_hash = finalState.ModelHash.ToString(),
                initial_sequence = initialSequence.ToString(),
                final_sequence = finalState.Sequence.ToString(),
                initial_simulation_time = initialSimulationTime,
                final_simulation_time = finalState.SimulationTime,
                controlled_initialization_failure_observed = true,
                controlled_initialization_failure_code =
                    malformedCreate.Error.Code.ToString(),
                controlled_initialization_failure_message =
                    malformedCreate.Error.Message,
                valid_probe_stepped = probeStepped,
                probe_session_destroyed = probeDestroyed,
                operation_after_close_rejected = operationAfterCloseRejected,
                pause_callback_count = pauseCallbackCount,
                resume_callback_count = resumeCallbackCount,
                pause_resume_cycle_count = cycles.Count,
                cycles = cycles.ToArray(),
                production_runtime_destroyed = productionRuntimeDestroyed,
                renderer_disabled_after_shutdown = rendererDisabled,
                hidden_native_fallback = false,
            };

            complete = true;
            displayMessage = "RMA-022 native lifecycle acceptance passed.";
            string reportJson = JsonUtility.ToJson(report);
            PublishResult(reportJson);
            Debug.Log("WEACHY_RMA022_LIFECYCLE_ACCEPTANCE " + reportJson, this);
        }

        private IEnumerator RunPauseResumeCycle(
            ReachyProductionAuthoritativeRuntime runtime,
            int cycle)
        {
            if (!TryReadLatestState(runtime, out ReachyAuthoritativePoseSnapshot beforePause))
            {
                Fail($"Lifecycle cycle {cycle} could not capture its pre-pause state.");
                yield break;
            }

            int expectedPauseCount = pauseCallbackCount + 1;
            int expectedResumeCount = resumeCallbackCount + 1;
            lastPauseUtcTicks = 0L;
            lastResumeUtcTicks = 0L;
            displayMessage = $"Waiting for lifecycle pause/resume cycle {cycle}.";
            PublishProgress(
                "awaiting_pause",
                cycle,
                $"Background the application for lifecycle cycle {cycle}.");

            float deadline = Time.realtimeSinceStartup + LifecycleTimeoutSeconds;
            while (pauseCallbackCount < expectedPauseCount ||
                   resumeCallbackCount < expectedResumeCount)
            {
                if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    Fail(
                        $"Production runtime faulted during lifecycle cycle {cycle}: " +
                        runtime.Fault);
                    yield break;
                }
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail(
                        $"Timed out waiting for lifecycle cycle {cycle}; " +
                        $"pause_callbacks={pauseCallbackCount}, " +
                        $"resume_callbacks={resumeCallbackCount}.");
                    yield break;
                }
                yield return null;
            }

            while (runtime.Status != ReachyProductionRuntimeStatus.Running)
            {
                if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    Fail(
                        $"Production runtime faulted while resuming cycle {cycle}: " +
                        runtime.Fault);
                    yield break;
                }
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail(
                        $"Production runtime did not return to Running after cycle {cycle}; " +
                        $"status={runtime.Status}.");
                    yield break;
                }
                yield return null;
            }

            yield return null;
            if (!TryReadLatestState(runtime, out ReachyAuthoritativePoseSnapshot afterResume))
            {
                Fail($"Lifecycle cycle {cycle} could not capture its post-resume state.");
                yield break;
            }

            double suspendedWallSeconds = lastResumeUtcTicks > lastPauseUtcTicks &&
                lastPauseUtcTicks > 0L
                ? TimeSpan.FromTicks(lastResumeUtcTicks - lastPauseUtcTicks).TotalSeconds
                : 0.0;
            double simulationAdvanceSeconds =
                afterResume.SimulationTime - beforePause.SimulationTime;
            ulong sequenceAdvance = afterResume.Sequence >= beforePause.Sequence
                ? afterResume.Sequence - beforePause.Sequence
                : ulong.MaxValue;
            LifecycleCycleReport report = new LifecycleCycleReport
            {
                cycle = cycle,
                pause_callback_observed = pauseCallbackCount >= expectedPauseCount,
                resume_callback_observed = resumeCallbackCount >= expectedResumeCount,
                suspended_wall_seconds = suspendedWallSeconds,
                simulation_time_before_pause = beforePause.SimulationTime,
                simulation_time_after_resume = afterResume.SimulationTime,
                simulation_time_advance = simulationAdvanceSeconds,
                sequence_before_pause = beforePause.Sequence.ToString(),
                sequence_after_resume = afterResume.Sequence.ToString(),
                sequence_advance = sequenceAdvance.ToString(),
                runtime_status_after_resume = runtime.Status.ToString(),
            };
            cycles.Add(report);

            if (suspendedWallSeconds < 1.0 ||
                simulationAdvanceSeconds < 0.0 ||
                simulationAdvanceSeconds > MaximumResumeSimulationAdvanceSeconds ||
                simulationAdvanceSeconds >= suspendedWallSeconds ||
                runtime.Status != ReachyProductionRuntimeStatus.Running)
            {
                Fail(
                    $"Lifecycle cycle {cycle} violated pause invariants: " +
                    JsonUtility.ToJson(report));
                yield break;
            }

            PublishProgress(
                "cycle_complete",
                cycle,
                $"Cycle {cycle} resumed without suspended-wall-time catch-up.");
        }

        private void OnApplicationPause(bool paused)
        {
            if (!acceptanceEnabled || complete)
            {
                return;
            }

            if (paused)
            {
                ++pauseCallbackCount;
                lastPauseUtcTicks = DateTime.UtcNow.Ticks;
                PublishProgress(
                    "pause_callback",
                    pauseCallbackCount,
                    $"Observed application pause callback {pauseCallbackCount}.");
            }
            else if (pauseCallbackCount > resumeCallbackCount)
            {
                ++resumeCallbackCount;
                lastResumeUtcTicks = DateTime.UtcNow.Ticks;
                PublishProgress(
                    "resume_callback",
                    resumeCallbackCount,
                    $"Observed application resume callback {resumeCallbackCount}.");
            }
        }

        private static bool TryReadLatestState(
            ReachyProductionAuthoritativeRuntime runtime,
            out ReachyAuthoritativePoseSnapshot state)
        {
            return runtime.TryGetLatestAuthoritativePair(out _, out state);
        }

        private static bool TryReadAcceptanceRequest(out string error)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject intent =
                    activity.Call<AndroidJavaObject>("getIntent"))
                {
                    error = string.Empty;
                    return intent.Call<bool>(
                        "getBooleanExtra",
                        LaunchExtraName,
                        false);
                }
            }
            catch (Exception exception)
            {
                error =
                    "Could not inspect the Android RMA-022 launch intent: " +
                    exception.Message;
                return false;
            }
#else
            error = string.Empty;
            return false;
#endif
        }

        private void PublishProgress(string stage, int cycle, string message)
        {
            LifecycleProgress progress = new LifecycleProgress
            {
                status = "in_progress",
                stage = stage,
                cycle = cycle,
                message = message,
            };
            PublishResult(JsonUtility.ToJson(progress));
        }

        private void PublishResult(string json)
        {
            try
            {
                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string resultPath = Path.Combine(directory, ResultFileName);
                string temporaryPath = resultPath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                File.Move(temporaryPath, resultPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not publish RMA-022 lifecycle evidence: " +
                    exception.Message,
                    this);
            }
        }

        private void Fail(string message)
        {
            complete = true;
            displayMessage = "RMA-022 lifecycle acceptance failed: " + message;
            LifecycleFailure failure = new LifecycleFailure
            {
                status = "failed",
                message = message,
            };
            string failureJson = JsonUtility.ToJson(failure);
            PublishResult(failureJson);
            Debug.LogError(
                "WEACHY_RMA022_LIFECYCLE_ACCEPTANCE_FAILURE " + failureJson,
                this);
        }

        private void OnGUI()
        {
            if (!acceptanceEnabled)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                wordWrap = true,
            };
            style.normal.textColor = complete ? Color.white : Color.yellow;
            GUI.Box(
                new Rect(20f, 160f, Screen.width - 40f, 150f),
                displayMessage,
                style);
        }

        [Serializable]
        private sealed class LifecycleProgress
        {
            public string status = string.Empty;
            public string stage = string.Empty;
            public int cycle;
            public string message = string.Empty;
        }

        [Serializable]
        private sealed class LifecycleCycleReport
        {
            public int cycle;
            public bool pause_callback_observed;
            public bool resume_callback_observed;
            public double suspended_wall_seconds;
            public double simulation_time_before_pause;
            public double simulation_time_after_resume;
            public double simulation_time_advance;
            public string sequence_before_pause = string.Empty;
            public string sequence_after_resume = string.Empty;
            public string sequence_advance = string.Empty;
            public string runtime_status_after_resume = string.Empty;
        }

        [Serializable]
        private sealed class LifecycleReport
        {
            public string status = string.Empty;
            public uint native_abi_version;
            public string native_version = string.Empty;
            public string model_hash = string.Empty;
            public string initial_sequence = string.Empty;
            public string final_sequence = string.Empty;
            public double initial_simulation_time;
            public double final_simulation_time;
            public bool controlled_initialization_failure_observed;
            public string controlled_initialization_failure_code = string.Empty;
            public string controlled_initialization_failure_message = string.Empty;
            public bool valid_probe_stepped;
            public bool probe_session_destroyed;
            public bool operation_after_close_rejected;
            public int pause_callback_count;
            public int resume_callback_count;
            public int pause_resume_cycle_count;
            public LifecycleCycleReport[] cycles =
                Array.Empty<LifecycleCycleReport>();
            public bool production_runtime_destroyed;
            public bool renderer_disabled_after_shutdown;
            public bool hidden_native_fallback = true;
        }

        [Serializable]
        private sealed class LifecycleFailure
        {
            public string status = string.Empty;
            public string message = string.Empty;
        }
    }
}
