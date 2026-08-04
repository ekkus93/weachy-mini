#nullable enable

using System;
using System.Collections;
using UnityEngine;

namespace ReachyMini.AppState
{
    public static class ReachyCameraAcquisitionBootstrap
    {
        public const string InstallerObjectName =
            "ReachyCameraAcquisitionInstaller";
        public const string AcquisitionObjectName =
            "ReachyAndroidCameraAcquisition";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            if (UnityEngine.Object.FindAnyObjectByType<
                    ReachyCameraAcquisitionInstaller>() != null)
            {
                return;
            }

            GameObject installerObject = new GameObject(InstallerObjectName);
            installerObject.AddComponent<ReachyCameraAcquisitionInstaller>();
        }

        public static bool TryInstall(out string fault)
        {
            ReachyAndroidCameraDiscovery? discovery =
                UnityEngine.Object.FindAnyObjectByType<
                    ReachyAndroidCameraDiscovery>();
            if (discovery == null)
            {
                fault =
                    "The application shell contains no Android camera discovery component.";
                return false;
            }

            try
            {
                ReachyAndroidCameraAcquisition acquisition =
                    GetOrCreateAcquisition(discovery);
                InstallAcceptanceEvidenceIfRequested(
                    discovery,
                    acquisition);
                fault = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                fault = string.IsNullOrWhiteSpace(exception.Message)
                    ? "CameraX acquisition installation failed without diagnostics."
                    : exception.Message;
                return false;
            }
        }

        private static ReachyAndroidCameraAcquisition GetOrCreateAcquisition(
            ReachyAndroidCameraDiscovery discovery)
        {
            ReachyAndroidCameraAcquisition? existing =
                discovery.GetComponentInChildren<
                    ReachyAndroidCameraAcquisition>(true);
            if (existing != null)
            {
                existing.Configure(discovery);
                return existing;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            GameObject acquisitionObject =
                new GameObject(AcquisitionObjectName);
            acquisitionObject.SetActive(false);
            acquisitionObject.transform.SetParent(
                discovery.transform,
                false);
            try
            {
                ReachyAndroidCameraAcquisition acquisition =
                    acquisitionObject.AddComponent<
                        ReachyAndroidCameraAcquisition>();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    new ReachyAndroidUiThreadCameraAcquisitionPlatform());
                acquisitionObject.SetActive(true);
                return acquisition;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(acquisitionObject);
                throw;
            }
#else
            ReachyAndroidCameraAcquisition acquisition =
                discovery.gameObject.AddComponent<
                    ReachyAndroidCameraAcquisition>();
            acquisition.Configure(discovery);
            return acquisition;
#endif
        }

        private static void InstallAcceptanceEvidenceIfRequested(
            ReachyAndroidCameraDiscovery discovery,
            ReachyAndroidCameraAcquisition acquisition)
        {
            if (!ReachyCameraAcquisitionEvidence
                    .IsAcceptanceRequestedFromLaunchIntent())
            {
                return;
            }

            ReachyCameraAcquisitionEvidence? evidence =
                discovery.GetComponent<ReachyCameraAcquisitionEvidence>();
            if (evidence == null)
            {
                evidence = discovery.gameObject.AddComponent<
                    ReachyCameraAcquisitionEvidence>();
            }
            evidence.Configure(acquisition, discovery);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyCameraAcquisitionInstaller : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null;
            if (!ReachyCameraAcquisitionBootstrap.TryInstall(out string fault))
            {
                Debug.LogError(
                    $"RMA-091 CameraX acquisition bootstrap failed: {fault}");
            }
            Destroy(gameObject);
        }
    }
}
