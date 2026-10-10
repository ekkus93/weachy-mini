#nullable enable

using System;
using ReachyMini.LocalModels;
using ReachyMini.Performance;
using ReachyMini.Rendering;
using UnityEngine;

namespace ReachyMini.AppState
{
    /// <summary>
    /// Shipping RMA-181 runtime bridge. It samples the same Android
    /// memory/thermal signals and authoritative physics timing used by the
    /// local-LLM governor, then applies one ordered degradation decision to
    /// the real render, provider, and perception services.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ReachyPriorityDegradationRuntimeDriver : MonoBehaviour
    {
        private const double EvaluationIntervalSeconds = 0.5;

        private ReachyProductionAuthoritativeRuntime? productionRuntime;
        private ReachyApplicationHostBehaviour? hostBehaviour;
        private ReachyAndroidLocalLlmResourceSignalSource? resourceSignals;
        private ReachySimulationLocalLlmPhysicsBudgetSource? physicsBudget;
        private ReachyUnityPriorityDegradationTarget? unityTarget;
        private ReachyPriorityDegradationRuntime? priorityRuntime;
        private double nextEvaluationTime;
        private bool configured;
        private bool configurationFaultReported;
        private ReachyPriorityDegradationLevel? lastReportedLevel;
        private ReachyPriorityDegradationReason lastReportedReasons;

        internal ReachyPriorityDegradationDecision? CurrentDecision { get; private set; }

        // Exact sampled inputs associated with CurrentDecision, exposed only so
        // physical qualification can verify the shipping policy. Callers cannot
        // replace or mutate the production sampling path through this property.
        internal ReachyPriorityDegradationSignals? CurrentSignals =>
            priorityRuntime?.LastSignals;

        internal void Configure(
            ReachyProductionAuthoritativeRuntime runtime)
        {
            if (configured)
            {
                throw new InvalidOperationException(
                    "The priority-degradation runtime cannot be configured more than once.");
            }

            productionRuntime = runtime ??
                throw new ArgumentNullException(nameof(runtime));
            configured = true;
        }

        private void Update()
        {
            if (!configured ||
                Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextEvaluationTime)
            {
                return;
            }
            nextEvaluationTime = now + EvaluationIntervalSeconds;

            if (priorityRuntime == null && !TryInitializeRuntime())
            {
                return;
            }

            try
            {
                ReachyPriorityDegradationDecision decision =
                    priorityRuntime!.CaptureAndApply();
                CurrentDecision = decision;
                bool levelChanged = lastReportedLevel != decision.Level;
                bool criticalReasonChanged =
                    decision.Level == ReachyPriorityDegradationLevel.Critical &&
                    lastReportedReasons != decision.Reasons;
                if (levelChanged || criticalReasonChanged)
                {
                    // Log only transitions, not every 500 ms sample. Preserve
                    // the exact source sample and the fail-closed policy.
                    ReachyPriorityDegradationSignals? sampled =
                        priorityRuntime.LastSignals;
                    if (sampled != null)
                    {
                        Debug.LogWarning(
                            "RMA-181 sampled resource transition: " +
                            decision.Diagnostic +
                            " total_mem_bytes=" + sampled.TotalMemoryBytes +
                            "; available_mem_bytes=" + sampled.AvailableMemoryBytes +
                            "; low_mem_threshold_bytes=" + sampled.LowMemoryThresholdBytes +
                            "; android_low_memory=" + sampled.SystemReportsLowMemory +
                            "; thermal=" + sampled.ThermalStatus +
                            "; physics_budget=" + sampled.PhysicsBudgetState + ".");
                    }
                }
                lastReportedLevel = decision.Level;
                lastReportedReasons = decision.Reasons;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "RMA-181 priority-degradation sampling failed closed: " +
                    exception.Message);
            }
        }

        private bool TryInitializeRuntime()
        {
            ReachyProductionAuthoritativeRuntime runtime =
                productionRuntime ??
                throw new InvalidOperationException(
                    "The priority-degradation runtime lacks the authoritative simulation.");

            hostBehaviour ??=
                FindAnyObjectByType<ReachyApplicationHostBehaviour>();
            ReachyApplicationHost? host = hostBehaviour?.Host;
            if (host == null)
            {
                return false;
            }

            ReachyApplicationState applicationState = host.Health.State;
            if (applicationState != ReachyApplicationState.Ready &&
                applicationState != ReachyApplicationState.Degraded)
            {
                return false;
            }

            IReachyProviderService provider =
                host.GetRequiredService<IReachyProviderService>(
                    ReachyServiceKind.Provider);
            IReachyPerceptionService perception =
                host.GetRequiredService<IReachyPerceptionService>(
                    ReachyServiceKind.Perception);

            if (provider is not IReachyPriorityDegradationTarget providerTarget ||
                perception is not IReachyPriorityDegradationTarget perceptionTarget)
            {
                if (!configurationFaultReported)
                {
                    configurationFaultReported = true;
                    Debug.LogError(
                        "RMA-181 shipping services do not expose the required " +
                        "priority-degradation targets; policy application is unavailable.");
                }
                return false;
            }

            resourceSignals =
                new ReachyAndroidLocalLlmResourceSignalSource();
            physicsBudget =
                new ReachySimulationLocalLlmPhysicsBudgetSource(runtime);
            unityTarget =
                new ReachyUnityPriorityDegradationTarget();

            int currentTargetFrameRate =
                ReachyRuntimeThermalPolicy.CaptureCurrentPacing().TargetFrameRate;
            int nominalRenderFps =
                currentTargetFrameRate >= 45 ? 60 : 30;
            var coordinator =
                new ReachyPriorityDegradationCoordinator(
                    new ReachyPriorityDegradationPolicy(nominalRenderFps),
                    new IReachyPriorityDegradationTarget[]
                    {
                        unityTarget,
                        providerTarget,
                        perceptionTarget,
                    });
            priorityRuntime =
                new ReachyPriorityDegradationRuntime(
                    resourceSignals,
                    physicsBudget,
                    coordinator);
            return true;
        }

        private void OnDestroy()
        {
            unityTarget?.RestoreBaseline();
            resourceSignals?.Dispose();
            priorityRuntime = null;
            physicsBudget = null;
            resourceSignals = null;
            unityTarget = null;
        }
    }
}
