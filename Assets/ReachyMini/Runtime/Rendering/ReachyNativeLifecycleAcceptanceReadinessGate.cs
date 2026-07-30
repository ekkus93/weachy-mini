#nullable enable

using System.Collections;
using UnityEngine;

namespace ReachyMini.Rendering
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReachyProductionAuthoritativeRuntime))]
    [RequireComponent(typeof(ReachyNativeLifecycleAcceptance))]
    public sealed class ReachyNativeLifecycleAcceptanceReadinessGate : MonoBehaviour
    {
        private const float ReadinessTimeoutSeconds = 30.0f;

        private ReachyNativeLifecycleAcceptance? acceptance;

        private void Awake()
        {
            acceptance = GetComponent<ReachyNativeLifecycleAcceptance>();
            if (acceptance != null)
            {
                acceptance.enabled = false;
            }
        }

        private IEnumerator Start()
        {
            if (acceptance == null)
            {
                enabled = false;
                yield break;
            }

            if (Application.platform != RuntimePlatform.Android)
            {
                acceptance.enabled = true;
                enabled = false;
                yield break;
            }

            ReachyProductionAuthoritativeRuntime runtime =
                GetComponent<ReachyProductionAuthoritativeRuntime>();
            float deadline = Time.realtimeSinceStartup + ReadinessTimeoutSeconds;
            while (runtime.Status != ReachyProductionRuntimeStatus.Faulted &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (runtime.Status == ReachyProductionRuntimeStatus.Running &&
                    runtime.TryGetLatestAuthoritativePair(out _, out _))
                {
                    break;
                }
                yield return null;
            }

            acceptance.enabled = true;
            enabled = false;
        }
    }
}
