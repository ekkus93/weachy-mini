#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ReachyMini.Presentation;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Rendering
{
    [DisallowMultipleComponent]
    public sealed class ReachyAuthoritativePhysicalAcceptance : MonoBehaviour
    {
        public const string ResultFileName =
            "weachy-authoritative-acceptance.json";

        private const string SuccessMarker = "WEACHY_AUTHORITATIVE_ACCEPTANCE ";
        private const string FailureMarker =
            "WEACHY_AUTHORITATIVE_ACCEPTANCE_FAILURE ";
        private const double PhaseDurationSeconds = 0.55;
        private const float PositionMotionThresholdMetres = 1.0e-5f;
        private const float RotationMotionThresholdDegrees = 0.05f;
        private static readonly double[] PoseA =
        {
            0.18, 0.25, -0.20, 0.18, -0.15, 0.22, -0.17, 0.30, -0.25,
        };
        private static readonly double[] PoseB =
        {
            -0.12, -0.20, 0.18, -0.15, 0.20, -0.18, 0.16, -0.20, 0.28,
        };

        private string displayMessage =
            "Authoritative rendering acceptance is starting.";
        private bool complete;

        public bool IsComplete => complete;

        public string ResultPath => Path.Combine(
            Application.persistentDataPath,
            ResultFileName);

        private IEnumerator Start()
        {
#if !WEACHY_PHYSICAL_ACCEPTANCE
            yield break;
#else
            PublishProgress(
                "component_started",
                "The physical authoritative-rendering component started.");
            yield return RunAcceptance();
#endif
        }

        private IEnumerator RunAcceptance()
        {
            ReachyProductionAuthoritativeRuntime runtime =
                GetComponent<ReachyProductionAuthoritativeRuntime>();
            ReachyPresentationRoot root = GetComponent<ReachyPresentationRoot>();
            ReachyAuthoritativeRenderer renderer =
                GetComponent<ReachyAuthoritativeRenderer>();
            if (runtime == null || root == null || renderer == null)
            {
                Fail(
                    "The generated acceptance scene is missing a required " +
                    "runtime component.");
                yield break;
            }

            PublishProgress(
                "waiting_for_runtime",
                "Waiting for the production simulator and renderer to bind.");
            float startupDeadline = Time.realtimeSinceStartup + 30.0f;
            while (runtime.Status != ReachyProductionRuntimeStatus.Running ||
                   renderer.Status !=
                       ReachyAuthoritativeRendererStatus.Rendering)
            {
                if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    Fail(
                        $"Production runtime faulted during startup: " +
                        runtime.Fault);
                    yield break;
                }
                if (Time.realtimeSinceStartup >= startupDeadline)
                {
                    Fail(
                        $"Timed out waiting for production rendering; " +
                        $"runtime={runtime.Status}, renderer={renderer.Status}.");
                    yield break;
                }
                yield return null;
            }

            PublishProgress(
                "runtime_bound",
                $"Production rendering is bound with model hash " +
                $"{runtime.ModelHash}.");
            if (!runtime.TryGetLatestAuthoritativePair(
                    out _,
                    out ReachyAuthoritativePoseSnapshot baselineState))
            {
                Fail(
                    "The production pose source did not publish an initial " +
                    "snapshot pair.");
                yield break;
            }

            ReachyPresentationBody[] bodies = root.GetCanonicalBodies();
            Dictionary<string, BodyWorldPose> baseline = CaptureWorldPoses(bodies);
            ulong initialSequence = baselineState.Sequence;
            uint initialContinuity = baselineState.DiscontinuityId;

            displayMessage =
                "Driving representative pose A from authoritative MuJoCo commands.";
            PublishProgress(
                "pose_a_submitted",
                "Submitting representative pose A to the production worker.");
            ReachySimulationCommandEnqueueResult poseAResult =
                runtime.SubmitPositionTargets(PoseA);
            if (poseAResult != ReachySimulationCommandEnqueueResult.Accepted)
            {
                Fail($"Pose A command was rejected: {poseAResult}.");
                yield break;
            }
            yield return WaitForSimulationAdvance(
                runtime,
                baselineState.SimulationTime + PhaseDurationSeconds,
                15.0f);
            if (!TryGetHealthyState(
                    runtime,
                    renderer,
                    out ReachyAuthoritativePoseSnapshot poseAState,
                    out string poseAError))
            {
                Fail(poseAError);
                yield break;
            }
            Dictionary<string, BodyWorldPose> poseA = CaptureWorldPoses(bodies);
            PublishProgress(
                "pose_a_observed",
                $"Observed authoritative pose A at sequence " +
                $"{poseAState.Sequence}.");

            displayMessage =
                "Driving representative pose B from authoritative MuJoCo commands.";
            PublishProgress(
                "pose_b_submitted",
                "Submitting representative pose B to the production worker.");
            ReachySimulationCommandEnqueueResult poseBResult =
                runtime.SubmitPositionTargets(PoseB);
            if (poseBResult != ReachySimulationCommandEnqueueResult.Accepted)
            {
                Fail($"Pose B command was rejected: {poseBResult}.");
                yield break;
            }
            yield return WaitForSimulationAdvance(
                runtime,
                poseAState.SimulationTime + PhaseDurationSeconds,
                15.0f);
            if (!TryGetHealthyState(
                    runtime,
                    renderer,
                    out ReachyAuthoritativePoseSnapshot poseBState,
                    out string poseBError))
            {
                Fail(poseBError);
                yield break;
            }
            PublishProgress(
                "pose_b_observed",
                $"Observed authoritative pose B at sequence " +
                $"{poseBState.Sequence}.");

            int movedBodyCount = CountMovedBodies(baseline, poseA);
            bool bodyYawMoved =
                BodyMoved(baseline, poseA, "body_down_3dprint");
            bool headMoved = BodyMoved(baseline, poseA, "xl_330");
            bool rightAntennaMoved =
                BodyMoved(baseline, poseA, "dc15_a01_horn_dummy_7");
            bool leftAntennaMoved =
                BodyMoved(baseline, poseA, "dc15_a01_horn_dummy_8");
            int movedStewartLinks = CountMovedStewartLinks(baseline, poseA);

            displayMessage =
                "Resetting the production simulation and verifying " +
                "discontinuity snapping.";
            PublishProgress(
                "reset_submitted",
                "Submitting a neutral reset to the production worker.");
            ReachySimulationControlResult resetResult = runtime.ResetNeutral();
            if (!resetResult.IsSuccess)
            {
                Fail(
                    $"Neutral reset failed: {resetResult.Error.Code}: " +
                    resetResult.Error.Message);
                yield break;
            }

            uint resetContinuity = initialContinuity;
            float resetDeadline = Time.realtimeSinceStartup + 15.0f;
            while (resetContinuity == initialContinuity)
            {
                if (!TryGetHealthyState(
                        runtime,
                        renderer,
                        out ReachyAuthoritativePoseSnapshot resetState,
                        out string resetError))
                {
                    Fail(resetError);
                    yield break;
                }
                resetContinuity = resetState.DiscontinuityId;
                if (Time.realtimeSinceStartup >= resetDeadline)
                {
                    Fail(
                        "Neutral reset did not publish a new continuity epoch.");
                    yield break;
                }
                yield return null;
            }

            bool structureValid = renderer.ValidateAuthoritativeStructure();
            AcceptanceReport report = new AcceptanceReport
            {
                status = "ok",
                model_hash = runtime.ModelHash.ToString(),
                body_count = bodies.Length,
                initial_sequence = initialSequence.ToString(),
                pose_a_sequence = poseAState.Sequence.ToString(),
                pose_b_sequence = poseBState.Sequence.ToString(),
                initial_continuity_id = initialContinuity,
                reset_continuity_id = resetContinuity,
                moved_body_count = movedBodyCount,
                moved_stewart_link_count = movedStewartLinks,
                body_yaw_moved = bodyYawMoved,
                head_moved = headMoved,
                right_antenna_moved = rightAntennaMoved,
                left_antenna_moved = leftAntennaMoved,
                renderer_structure_valid = structureValid,
                renderer_status = renderer.Status.ToString(),
                runtime_status = runtime.Status.ToString(),
                sleep_reset_status =
                    "unsupported_pinned_model_has_no_named_sleep_keyframe",
                hidden_kinematic_fallback = false,
            };

            if (runtime.ModelHash == 0UL || bodies.Length != 18 ||
                poseAState.Sequence <= initialSequence ||
                poseBState.Sequence <= poseAState.Sequence ||
                resetContinuity == initialContinuity ||
                movedBodyCount < 10 || movedStewartLinks < 6 ||
                !bodyYawMoved || !headMoved ||
                !rightAntennaMoved || !leftAntennaMoved ||
                !structureValid ||
                renderer.Status !=
                    ReachyAuthoritativeRendererStatus.Rendering ||
                runtime.Status != ReachyProductionRuntimeStatus.Running)
            {
                report.status = "failed_acceptance_condition";
                Fail(JsonUtility.ToJson(report));
                yield break;
            }

            complete = true;
            displayMessage =
                "Authoritative MuJoCo-to-Unity rendering acceptance passed " +
                "on this device.";
            string reportJson = JsonUtility.ToJson(report);
            PublishResult(reportJson);
            Debug.Log(SuccessMarker + reportJson, this);
        }

        private static IEnumerator WaitForSimulationAdvance(
            ReachyProductionAuthoritativeRuntime runtime,
            double targetSimulationTime,
            float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (true)
            {
                if (runtime.TryGetLatestAuthoritativePair(
                        out _,
                        out ReachyAuthoritativePoseSnapshot state) &&
                    state.SimulationTime >= targetSimulationTime)
                {
                    yield break;
                }
                if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                {
                    yield break;
                }
                if (Time.realtimeSinceStartup >= deadline)
                {
                    yield break;
                }
                yield return null;
            }
        }

        private static bool TryGetHealthyState(
            ReachyProductionAuthoritativeRuntime runtime,
            ReachyAuthoritativeRenderer renderer,
            out ReachyAuthoritativePoseSnapshot state,
            out string error)
        {
            if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
            {
                state = null!;
                error = $"Production runtime faulted: {runtime.Fault}";
                return false;
            }
            if (renderer.Status == ReachyAuthoritativeRendererStatus.Faulted)
            {
                state = null!;
                error = $"Authoritative renderer faulted: {renderer.Fault}";
                return false;
            }
            if (!runtime.TryGetLatestAuthoritativePair(out _, out state))
            {
                error =
                    "The authoritative pose source stopped publishing " +
                    "snapshot pairs.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static Dictionary<string, BodyWorldPose> CaptureWorldPoses(
            ReachyPresentationBody[] bodies)
        {
            Dictionary<string, BodyWorldPose> result =
                new Dictionary<string, BodyWorldPose>(StringComparer.Ordinal);
            for (int index = 0; index < bodies.Length; ++index)
            {
                ReachyPresentationBody body = bodies[index];
                string name = string.IsNullOrEmpty(body.BodyName)
                    ? $"__body_{body.BodyIndex}"
                    : body.BodyName;
                result.Add(
                    name,
                    new BodyWorldPose(
                        body.transform.position,
                        body.transform.rotation));
            }
            return result;
        }

        private static int CountMovedBodies(
            IReadOnlyDictionary<string, BodyWorldPose> baseline,
            IReadOnlyDictionary<string, BodyWorldPose> moved)
        {
            int count = 0;
            foreach (KeyValuePair<string, BodyWorldPose> entry in baseline)
            {
                if (moved.TryGetValue(
                        entry.Key,
                        out BodyWorldPose current) &&
                    HasMoved(entry.Value, current))
                {
                    ++count;
                }
            }
            return count;
        }

        private static int CountMovedStewartLinks(
            IReadOnlyDictionary<string, BodyWorldPose> baseline,
            IReadOnlyDictionary<string, BodyWorldPose> moved)
        {
            int count = 0;
            foreach (KeyValuePair<string, BodyWorldPose> entry in baseline)
            {
                if (entry.Key.StartsWith(
                        "stewart_link_rod",
                        StringComparison.Ordinal) &&
                    moved.TryGetValue(
                        entry.Key,
                        out BodyWorldPose current) &&
                    HasMoved(entry.Value, current))
                {
                    ++count;
                }
            }
            return count;
        }

        private static bool BodyMoved(
            IReadOnlyDictionary<string, BodyWorldPose> baseline,
            IReadOnlyDictionary<string, BodyWorldPose> moved,
            string bodyName)
        {
            return baseline.TryGetValue(
                    bodyName,
                    out BodyWorldPose before) &&
                moved.TryGetValue(bodyName, out BodyWorldPose after) &&
                HasMoved(before, after);
        }

        private static bool HasMoved(
            BodyWorldPose before,
            BodyWorldPose after)
        {
            return Vector3.Distance(before.Position, after.Position) >
                    PositionMotionThresholdMetres ||
                Quaternion.Angle(before.Rotation, after.Rotation) >
                    RotationMotionThresholdDegrees;
        }

        private void PublishProgress(string stage, string message)
        {
            AcceptanceProgress progress = new AcceptanceProgress
            {
                status = "in_progress",
                stage = stage,
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
                    $"Could not publish authoritative acceptance evidence: " +
                    exception.Message,
                    this);
            }
        }

        private void Fail(string message)
        {
            complete = true;
            displayMessage =
                $"Authoritative rendering acceptance failed: {message}";
            AcceptanceFailure failure = new AcceptanceFailure
            {
                status = "failed",
                message = message,
            };
            string failureJson = JsonUtility.ToJson(failure);
            PublishResult(failureJson);
            Debug.LogError(FailureMarker + failureJson, this);
        }

        private void OnGUI()
        {
#if WEACHY_PHYSICAL_ACCEPTANCE
            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                wordWrap = true,
            };
            style.normal.textColor = complete ? Color.white : Color.yellow;
            GUI.Box(
                new Rect(20f, 20f, Screen.width - 40f, 120f),
                displayMessage,
                style);
#endif
        }

        private readonly struct BodyWorldPose
        {
            public BodyWorldPose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }

            public Quaternion Rotation { get; }
        }

        [Serializable]
        private sealed class AcceptanceProgress
        {
            public string status = string.Empty;
            public string stage = string.Empty;
            public string message = string.Empty;
        }

        [Serializable]
        private sealed class AcceptanceReport
        {
            public string status = string.Empty;
            public string model_hash = string.Empty;
            public int body_count = 0;
            public string initial_sequence = string.Empty;
            public string pose_a_sequence = string.Empty;
            public string pose_b_sequence = string.Empty;
            public uint initial_continuity_id = 0U;
            public uint reset_continuity_id = 0U;
            public int moved_body_count = 0;
            public int moved_stewart_link_count = 0;
            public bool body_yaw_moved = false;
            public bool head_moved = false;
            public bool right_antenna_moved = false;
            public bool left_antenna_moved = false;
            public bool renderer_structure_valid = false;
            public string renderer_status = string.Empty;
            public string runtime_status = string.Empty;
            public string sleep_reset_status = string.Empty;
            public bool hidden_kinematic_fallback = true;
        }

        [Serializable]
        private sealed class AcceptanceFailure
        {
            public string status = string.Empty;
            public string message = string.Empty;
        }
    }
}
