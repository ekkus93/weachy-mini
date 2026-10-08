#nullable enable

using System;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using UnityEngine;
using ReachyMini.Diagnostics;
using ReachyMini.RuntimeDiagnostics;

namespace ReachyMini.AppState
{
    public static class ReachyMainScreenBootstrap
    {
        public const string ShellObjectName = "ReachyApplicationShell";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            if (!TryInstall(out string fault))
            {
                ReachyRuntimeDiagnostics.Emit(
                    "ui",
                    ReachyDiagnosticEventIds.MainScreenBootstrapFailed,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Configuration,
                    new ReachyDiagnosticField(
                        "operation",
                        "install_after_scene_load",
                        ReachyDiagnosticDataClass.Identifier));
            }
        }

        public static bool TryInstall(out string fault)
        {
            ReachyPresentationRoot? root =
                UnityEngine.Object.FindAnyObjectByType<ReachyPresentationRoot>();
            Camera? camera = Camera.main;
            if (root == null)
            {
                fault = "The active scene contains no Reachy presentation root.";
                return false;
            }
            if (camera == null)
            {
                fault = "The active scene contains no tagged main camera.";
                return false;
            }
            return TryInstall(root, camera, out fault);
        }

        public static bool TryInstall(
            ReachyPresentationRoot root,
            Camera camera,
            out string fault)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            ReachyProductionAuthoritativeRuntime runtime =
                root.GetComponent<ReachyProductionAuthoritativeRuntime>();
            ReachyPresentationCamera cameraMetadata =
                camera.GetComponent<ReachyPresentationCamera>();
            if (runtime == null)
            {
                fault =
                    "The Reachy presentation root lacks the authoritative production runtime.";
                return false;
            }
            if (cameraMetadata == null ||
                !string.Equals(
                    cameraMetadata.Framing,
                    "fixed_front_three_quarter",
                    StringComparison.Ordinal) ||
                cameraMetadata.AcceptsUserNavigation)
            {
                fault =
                    "The tagged main camera is not the fixed non-navigable Reachy presentation camera.";
                return false;
            }

            ReachyMainScreen? existing =
                root.GetComponentInChildren<ReachyMainScreen>(true);
            if (existing != null)
            {
                if (existing.PresentationCamera != camera ||
                    existing.GetComponent<ReachyApplicationHostBehaviour>() == null ||
                    existing.GetComponent<ReachySettingsApplicationCompositionProvider>() == null)
                {
                    fault =
                        "The existing application shell is incomplete or is not bound " +
                        "to the validated fixed presentation camera.";
                    return false;
                }

                fault = string.Empty;
                return true;
            }

            GameObject shellObject = new GameObject(ShellObjectName);
            try
            {
                shellObject.transform.SetParent(root.transform, false);
                ReachyMainScreen screen =
                    shellObject.AddComponent<ReachyMainScreen>();
                screen.ConfigurePresentationCamera(camera);
                ReachyAndroidCameraDiscovery cameraDiscovery =
                    shellObject.AddComponent<ReachyAndroidCameraDiscovery>();
                ReachyCameraDiscoveryEvidence cameraEvidence =
                    shellObject.AddComponent<ReachyCameraDiscoveryEvidence>();
                cameraEvidence.Configure(cameraDiscovery);
                ReachySettingsApplicationCompositionProvider provider =
                    shellObject.AddComponent<
                        ReachySettingsApplicationCompositionProvider>();
                provider.Configure(runtime, camera, screen, cameraDiscovery);
                ReachyApplicationHostBehaviour host =
                    shellObject.AddComponent<ReachyApplicationHostBehaviour>();
                host.ConfigureCompositionProvider(provider);
                ReachyPriorityDegradationRuntimeDriver priorityDegradation =
                    shellObject.AddComponent<ReachyPriorityDegradationRuntimeDriver>();
                priorityDegradation.Configure(runtime);
                fault = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                UnityEngine.Object.DestroyImmediate(shellObject);
                ReachyRuntimeDiagnostics.Emit(
                    "ui",
                    ReachyDiagnosticEventIds.MainScreenBootstrapFailed,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Configuration,
                    new ReachyDiagnosticField(
                        "exception_type",
                        exception.GetType().Name,
                        ReachyDiagnosticDataClass.Identifier),
                    new ReachyDiagnosticField(
                        "operation",
                        "compose_shell",
                        ReachyDiagnosticDataClass.Identifier));
                fault =
                    "The application shell failed (" +
                    exception.GetType().Name + ").";
                return false;
            }
        }
    }
}
