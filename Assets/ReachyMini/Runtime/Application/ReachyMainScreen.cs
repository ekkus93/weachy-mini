#nullable enable

using System;
using ReachyMini.Diagnostics;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed partial class ReachyMainScreen : MonoBehaviour
    {
        private const float ReferenceWidth = 1080f;
        private const float EdgePadding = 28f;
        private const float TopCardWidth = 430f;
        private const float TopCardHeight = 190f;
        private const float BottomBarHeight = 118f;
        private const float SettingsNavigationWidth = 180f;
        private static readonly ReachySettingsSection[] SettingsSections =
        {
            ReachySettingsSection.Providers,
            ReachySettingsSection.Camera,
            ReachySettingsSection.Speech,
            ReachySettingsSection.LocalModel,
            ReachySettingsSection.Simulation,
            ReachySettingsSection.Privacy,
            ReachySettingsSection.Licenses,
        };
        private static readonly ReachyProviderKind[] ProviderKinds =
        {
            ReachyProviderKind.Asr,
            ReachyProviderKind.Tts,
            ReachyProviderKind.Llm,
            ReachyProviderKind.Vlm,
        };

        [SerializeField]
        private Camera? presentationCamera;

        private ReachyMainScreenStateStore? stateStore;
        private ReachyMainScreenSnapshot? snapshot;
        private ReachySettingsStateStore? settingsStore;
        private ReachySettingsSnapshot? settingsSnapshot;
        private ReachyCameraCapabilityStateStore? cameraCapabilityStore;
        private ReachyCameraCapabilitySnapshot? cameraCapabilitySnapshot;
        private Action? requestCameraAccess;
        private Func<ReachyDiagnosticsScreenSnapshot>? diagnosticsProvider;
        private Func<ReachyDiagnosticBundleExportOutcome>? diagnosticBundleExporter;
        private string diagnosticBundleExportStatus =
            "No diagnostic bundle has been exported. Sensitive content is excluded by policy.";
        private Vector2 diagnosticsScrollPosition;
        private Func<ReachySettingsResetOutcome>? resetSimulation;

        public ReachyMainScreenSnapshot? Snapshot => snapshot;

        public ReachySettingsSnapshot? SettingsSnapshot => settingsSnapshot;

        public ReachyCameraCapabilitySnapshot? CameraCapabilitySnapshot =>
            cameraCapabilitySnapshot;

        public ReachyDiagnosticsScreenSnapshot? DiagnosticsSnapshot =>
            diagnosticsProvider?.Invoke();

        public string DiagnosticBundleExportStatus => diagnosticBundleExportStatus;

        public Camera? PresentationCamera => presentationCamera;

        public void ConfigurePresentationCamera(Camera camera)
        {
            if (stateStore != null)
            {
                throw new InvalidOperationException(
                    "The presentation camera cannot change after the main screen is bound.");
            }
            presentationCamera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public void ConfigureDiagnosticBundleExport(
            Func<ReachyDiagnosticBundleExportOutcome> exportOperation)
        {
            if (diagnosticBundleExporter != null)
            {
                throw new InvalidOperationException(
                    "The diagnostic bundle export operation cannot be bound more than once.");
            }
            diagnosticBundleExporter = exportOperation ??
                throw new ArgumentNullException(nameof(exportOperation));
        }

        public ReachyDiagnosticBundleExportOutcome ExportDiagnosticBundle()
        {
            Func<ReachyDiagnosticBundleExportOutcome>? exporter =
                diagnosticBundleExporter;
            if (exporter == null)
            {
                var unavailable = new ReachyDiagnosticBundleExportOutcome(
                    false,
                    "Diagnostic bundle export is unavailable because no exporter is bound.");
                diagnosticBundleExportStatus = unavailable.Detail;
                return unavailable;
            }

            ReachyDiagnosticBundleExportOutcome outcome = exporter();
            diagnosticBundleExportStatus = outcome.Succeeded
                ? outcome.Detail + " Path: " + outcome.FullPath
                : outcome.Detail;
            return outcome;
        }

        public void Bind(
            ReachyMainScreenStateStore store,
            Func<string> currentDiagnostics)
        {
            Bind(
                store,
                new ReachySettingsStateStore(),
                currentDiagnostics,
                () => new ReachySettingsResetOutcome(
                    false,
                    "The legacy composition does not expose simulation reset."));
        }

        public void Bind(
            ReachyMainScreenStateStore store,
            ReachySettingsStateStore durableSettings,
            Func<string> currentDiagnostics,
            Func<ReachySettingsResetOutcome> resetSimulationOperation)
        {
            ReachyCameraCapabilityStateStore unavailableCamera =
                CreateUnsupportedCameraCapabilities();
            Bind(
                store,
                durableSettings,
                currentDiagnostics,
                resetSimulationOperation,
                unavailableCamera,
                () => { });
        }

        public void Bind(
            ReachyMainScreenStateStore store,
            ReachySettingsStateStore durableSettings,
            Func<string> currentDiagnostics,
            Func<ReachySettingsResetOutcome> resetSimulationOperation,
            ReachyCameraCapabilityStateStore cameraCapabilities,
            Action requestCameraAccessOperation)
        {
            if (stateStore != null)
            {
                throw new InvalidOperationException(
                    "The main screen cannot be bound more than once.");
            }
            if (presentationCamera == null)
            {
                throw new InvalidOperationException(
                    "The main screen requires the fixed presentation camera.");
            }

            stateStore = store ?? throw new ArgumentNullException(nameof(store));
            settingsStore = durableSettings ??
                throw new ArgumentNullException(nameof(durableSettings));
            cameraCapabilityStore = cameraCapabilities ??
                throw new ArgumentNullException(nameof(cameraCapabilities));
            requestCameraAccess = requestCameraAccessOperation ??
                throw new ArgumentNullException(nameof(requestCameraAccessOperation));
            Func<string> legacyDiagnostics = currentDiagnostics ??
                throw new ArgumentNullException(nameof(currentDiagnostics));
            diagnosticsProvider = () =>
                ReachyDiagnosticsScreenSnapshot.FromLegacyText(
                    legacyDiagnostics());
            resetSimulation = resetSimulationOperation ??
                throw new ArgumentNullException(nameof(resetSimulationOperation));
            snapshot = stateStore.Current;
            settingsSnapshot = settingsStore.Current;
            cameraCapabilitySnapshot = cameraCapabilityStore.Current;
            stateStore.Changed += OnStateChanged;
            settingsStore.Changed += OnSettingsChanged;
            cameraCapabilityStore.Changed += OnCameraCapabilitiesChanged;
        }

        public void Bind(
            ReachyMainScreenStateStore store,
            ReachySettingsStateStore durableSettings,
            Func<ReachyDiagnosticsScreenSnapshot> currentDiagnostics,
            Func<ReachySettingsResetOutcome> resetSimulationOperation,
            ReachyCameraCapabilityStateStore cameraCapabilities,
            Action requestCameraAccessOperation)
        {
            if (stateStore != null)
            {
                throw new InvalidOperationException(
                    "The main screen cannot be bound more than once.");
            }
            if (presentationCamera == null)
            {
                throw new InvalidOperationException(
                    "The main screen requires the fixed presentation camera.");
            }

            stateStore = store ?? throw new ArgumentNullException(nameof(store));
            settingsStore = durableSettings ??
                throw new ArgumentNullException(nameof(durableSettings));
            cameraCapabilityStore = cameraCapabilities ??
                throw new ArgumentNullException(nameof(cameraCapabilities));
            requestCameraAccess = requestCameraAccessOperation ??
                throw new ArgumentNullException(nameof(requestCameraAccessOperation));
            diagnosticsProvider = currentDiagnostics ??
                throw new ArgumentNullException(nameof(currentDiagnostics));
            resetSimulation = resetSimulationOperation ??
                throw new ArgumentNullException(nameof(resetSimulationOperation));
            snapshot = stateStore.Current;
            settingsSnapshot = settingsStore.Current;
            cameraCapabilitySnapshot = cameraCapabilityStore.Current;
            stateStore.Changed += OnStateChanged;
            settingsStore.Changed += OnSettingsChanged;
            cameraCapabilityStore.Changed += OnCameraCapabilitiesChanged;
        }

        public void RequestMicrophone()
        {
            ReachyMainScreenStateStore store = RequireStore();
            if (!store.Current.MicrophoneAvailable)
            {
                store.ReportUnavailableAction(
                    "Microphone",
                    "no usable on-device speech provider was found, or microphone " +
                    "permission has not been granted yet");
                return;
            }
            store.SetInteraction(
                ReachyInteractionState.Listening,
                "Listening for speech.");
        }

        public void RequestCameraSelection()
        {
            ReachyMainScreenStateStore store = RequireStore();
            ReachyCameraCapabilitySnapshot camera =
                RequireCameraCapabilities();
            if (store.Current.CameraSelectionAvailable)
            {
                RequireSettings().SelectSection(ReachySettingsSection.Camera);
                store.ShowSettings(
                    "Android camera capabilities are available. Frame acquisition remains disabled until RMA-091.");
                return;
            }
            if (camera.Permission == ReachyCameraPermissionState.Unsupported ||
                camera.Permission == ReachyCameraPermissionState.Faulted)
            {
                store.ReportUnavailableAction(
                    "Camera selection",
                    camera.Message);
                return;
            }

            (requestCameraAccess ?? throw new InvalidOperationException(
                "The camera access operation is not bound."))();
            camera = RequireCameraCapabilities();
            if (camera.Permission == ReachyCameraPermissionState.Unsupported ||
                camera.Permission == ReachyCameraPermissionState.Faulted)
            {
                store.ReportUnavailableAction(
                    "Camera selection",
                    camera.Message);
                return;
            }
            store.SetInteraction(
                ReachyInteractionState.Unavailable,
                camera.Message);
        }

        public void RequestCameraAccess()
        {
            RequestCameraSelection();
        }

        public void ToggleSettings()
        {
            ReachyMainScreenStateStore store = RequireStore();
            if (store.Current.SettingsVisible)
            {
                store.HidePanels("Settings closed.");
            }
            else
            {
                store.ShowSettings(
                    "Settings opened. Unavailable capabilities remain labeled and actionable.");
            }
        }

        public void ToggleDiagnostics()
        {
            ReachyMainScreenStateStore store = RequireStore();
            if (store.Current.DiagnosticsVisible)
            {
                store.HidePanels("Diagnostics closed.");
            }
            else
            {
                store.ShowDiagnostics("Application diagnostics opened.");
            }
        }

        public void SelectSettingsSection(ReachySettingsSection section)
        {
            RequireSettings().SelectSection(section);
        }

        public void CycleProvider(ReachyProviderKind kind)
        {
            RequireSettings().CycleProvider(kind);
        }

        public void CyclePreferredCamera()
        {
            RequireSettings().CyclePreferredCameraFacing();
        }

        public void RequestCameraPreview()
        {
            RequireSettings().ReportUnavailableAction(
                "Camera preview",
                "RMA-090 discovers capabilities only; CameraX preview and ImageAnalysis begin in RMA-091");
        }

        public void RequestCameraCalibration()
        {
            RequireSettings().ReportUnavailableAction(
                "Camera calibration",
                "device-camera calibration begins after CameraX capability discovery");
        }

        public void RequestReprojectionDiagnostics()
        {
            RequireSettings().ReportUnavailableAction(
                "Reprojection diagnostics",
                "calibrated device-camera frames are not available yet");
        }

        public void CycleSpeechLanguage()
        {
            RequireSettings().CycleSpeechLanguage();
        }

        public void CycleSpeechVoice()
        {
            RequireSettings().CycleSpeechVoice();
        }

        public void RequestLocalModelInstall()
        {
            RequireSettings().ReportUnavailableAction(
                "Local-model install",
                "the model package installer is not implemented yet");
        }

        public void RequestLocalModelImport()
        {
            RequireSettings().ReportUnavailableAction(
                "Local-model import",
                "the Android document import bridge is not implemented yet");
        }

        public void RequestLocalModelSelect()
        {
            RequireSettings().ReportUnavailableAction(
                "Local-model selection",
                "no compatible local model is installed");
        }

        public void RequestLocalModelDelete()
        {
            RequireSettings().ReportUnavailableAction(
                "Local-model delete",
                "there is no installed local model to delete");
        }

        public void CycleLocalModelMemoryBudget()
        {
            RequireSettings().CycleLocalModelMemoryBudget();
        }

        public void CycleLocalModelContextLength()
        {
            RequireSettings().CycleLocalModelContextLength();
        }

        public void CycleSimulationFidelity()
        {
            RequireSettings().CycleSimulationFidelity();
        }

        public void RequestSimulationReset()
        {
            ReachySettingsResetOutcome outcome =
                (resetSimulation ?? throw new InvalidOperationException(
                    "The simulation reset operation is not bound."))();
            RequireSettings().ReportSimulationReset(
                outcome.Succeeded,
                outcome.Detail);
        }

        public void ToggleHistory()
        {
            RequireSettings().ToggleHistory();
        }

        public void CycleRetentionDays()
        {
            RequireSettings().CycleRetentionDays();
        }

        private void OnDestroy()
        {
            if (stateStore != null)
            {
                stateStore.Changed -= OnStateChanged;
            }
            if (settingsStore != null)
            {
                settingsStore.Changed -= OnSettingsChanged;
            }
            if (cameraCapabilityStore != null)
            {
                cameraCapabilityStore.Changed -= OnCameraCapabilitiesChanged;
            }
            stateStore = null;
            settingsStore = null;
            cameraCapabilityStore = null;
            requestCameraAccess = null;
            diagnosticsProvider = null;
            resetSimulation = null;
        }

        private void OnStateChanged(
            object? sender,
            ReachyMainScreenChangedEventArgs eventArgs)
        {
            snapshot = eventArgs.Snapshot;
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            settingsSnapshot = eventArgs.Snapshot;
        }

        private void OnCameraCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            cameraCapabilitySnapshot = eventArgs.Snapshot;
        }

        private ReachyMainScreenStateStore RequireStore()
        {
            return stateStore ?? throw new InvalidOperationException(
                "The main screen is not bound to application state.");
        }

        private ReachySettingsStateStore RequireSettings()
        {
            return settingsStore ?? throw new InvalidOperationException(
                "The main screen is not bound to settings state.");
        }

        private ReachyCameraCapabilitySnapshot RequireCameraCapabilities()
        {
            return cameraCapabilitySnapshot ?? throw new InvalidOperationException(
                "The main screen is not bound to camera capability state.");
        }

        private static ReachyCameraCapabilityStateStore
            CreateUnsupportedCameraCapabilities()
        {
            var store = new ReachyCameraCapabilityStateStore();
            store.MarkUnsupported(
                "Android camera discovery is not bound in the legacy application shell.");
            return store;
        }
    }
}
