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

            ReachyAndroidCameraAcquisition? existing =
                discovery.GetComponent<ReachyAndroidCameraAcquisition>();
            if (existing != null)
            {
                existing.Configure(discovery);
                fault = string.Empty;
                return true;
            }

            try
            {
                ReachyAndroidCameraAcquisition acquisition =
                    discovery.gameObject.AddComponent<
                        ReachyAndroidCameraAcquisition>();
                acquisition.Configure(discovery);
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
