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

        private const int AndroidScreenOrientationUnspecified = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureAndroidOrientationBeforeSceneLoad()
        {
            ConfigureAndroidAutoRotation();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            ConfigureAndroidAutoRotation();

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

        private static void ConfigureAndroidAutoRotation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = true;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            try
            {
                using var unityPlayer = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>(
                        "currentActivity");
                if (activity == null)
                {
                    throw new InvalidOperationException(
                        "UnityPlayer.currentActivity is unavailable.");
                }

                activity.Call(
                    "runOnUiThread",
                    new AndroidJavaRunnable(
                        () =>
                        {
                            try
                            {
                                using var currentUnityPlayer =
                                    new AndroidJavaClass(
                                        "com.unity3d.player.UnityPlayer");
                                using AndroidJavaObject currentActivity =
                                    currentUnityPlayer.GetStatic<
                                        AndroidJavaObject>(
                                        "currentActivity");
                                if (currentActivity == null)
                                {
                                    throw new InvalidOperationException(
                                        "UnityPlayer.currentActivity is unavailable.");
                                }
                                currentActivity.Call(
                                    "setRequestedOrientation",
                                    AndroidScreenOrientationUnspecified);
                            }
                            catch (Exception exception)
                            {
                                Debug.LogError(
                                    "Android auto-rotation request failed on the UI thread: " +
                                    exception.Message);
                            }
                        }));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Android auto-rotation setup failed: " +
                    exception.Message);
            }
#endif
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

            ReachyCameraAcquisitionAcceptanceRetry? retry =
                discovery.GetComponent<
                    ReachyCameraAcquisitionAcceptanceRetry>();
            if (retry == null)
            {
                retry = discovery.gameObject.AddComponent<
                    ReachyCameraAcquisitionAcceptanceRetry>();
            }
            retry.Configure(acquisition, discovery);
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
