#nullable enable

using System;
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
        private Func<string>? diagnosticsProvider;
        private Func<ReachySettingsResetOutcome>? resetSimulation;

        public ReachyMainScreenSnapshot? Snapshot => snapshot;

        public ReachySettingsSnapshot? SettingsSnapshot => settingsSnapshot;

        public ReachyCameraCapabilitySnapshot? CameraCapabilitySnapshot =>
            cameraCapabilitySnapshot;

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
                    "audio capture is not implemented until the speech phase");
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

        private void OnGUI()
        {
            ReachyMainScreenSnapshot? current = snapshot;
            if (current == null)
            {
                return;
            }

            EnsureStyles();
            Rect safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);
            }
            float scale = Mathf.Clamp(
                safeArea.width / ReferenceWidth,
                0.72f,
                1.35f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(safeArea.x, safeArea.y, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));
            float width = safeArea.width / scale;
            float height = safeArea.height / scale;

            DrawStatusCard(current);
            DrawBottomControls(current, width, height);
            if (current.SettingsVisible)
            {
                DrawSettingsPanel(width, height);
            }
            else if (current.DiagnosticsVisible)
            {
                DrawDiagnosticsPanel(width, height);
            }
            GUI.matrix = previousMatrix;
        }

        private void DrawStatusCard(ReachyMainScreenSnapshot current)
        {
            Rect card = new Rect(
                EdgePadding,
                EdgePadding,
                TopCardWidth,
                TopCardHeight);
            GUI.Box(card, GUIContent.none, panelStyle!);
            GUI.Label(
                new Rect(card.x + 22f, card.y + 16f, card.width - 44f, 34f),
                "REACHY MINI",
                titleStyle!);
            GUI.Label(
                new Rect(card.x + 22f, card.y + 52f, card.width - 44f, 38f),
                ReachyMainScreenStateStore.GetInteractionLabel(
                    current.InteractionState),
                stateStyle!);
            GUI.Label(
                new Rect(card.x + 22f, card.y + 91f, card.width - 44f, 48f),
                current.Detail,
                detailStyle!);
            GUI.Label(
                new Rect(card.x + 22f, card.y + 145f, card.width - 44f, 24f),
                $"CAMERA  {current.ActiveCamera}",
                indicatorStyle!);
            GUI.Label(
                new Rect(card.x + 22f, card.y + 169f, card.width - 44f, 24f),
                $"PROVIDER  {current.ActiveProvider} · " +
                ReachyMainScreenStateStore.GetProviderLocationLabel(
                    current.ProviderLocation),
                indicatorStyle!);
        }

        private void DrawBottomControls(
            ReachyMainScreenSnapshot current,
            float width,
            float height)
        {
            float barY = height - BottomBarHeight - EdgePadding;
            Rect bar = new Rect(
                EdgePadding,
                barY,
                width - EdgePadding * 2f,
                BottomBarHeight);
            GUI.Box(bar, GUIContent.none, panelStyle!);

            const float gap = 14f;
            float buttonWidth = (bar.width - 44f - gap * 3f) / 4f;
            float buttonY = bar.y + 25f;
            float buttonHeight = 68f;
            float buttonX = bar.x + 22f;
            string microphoneLabel = current.MicrophoneAvailable
                ? "MICROPHONE"
                : "MICROPHONE\nUNAVAILABLE";
            if (GUI.Button(
                    new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                    microphoneLabel,
                    buttonStyle!))
            {
                RequestMicrophone();
            }

            buttonX += buttonWidth + gap;
            string cameraLabel = current.CameraSelectionAvailable
                ? "DEVICE CAMERA"
                : "CAMERA\nACCESS";
            if (GUI.Button(
                    new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                    cameraLabel,
                    buttonStyle!))
            {
                RequestCameraSelection();
            }

            buttonX += buttonWidth + gap;
            if (GUI.Button(
                    new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                    current.SettingsVisible ? "CLOSE SETTINGS" : "SETTINGS",
                    buttonStyle!))
            {
                ToggleSettings();
            }

            buttonX += buttonWidth + gap;
            if (GUI.Button(
                    new Rect(buttonX, buttonY, buttonWidth, buttonHeight),
                    current.DiagnosticsVisible
                        ? "CLOSE DIAGNOSTICS"
                        : "DIAGNOSTICS",
                    buttonStyle!))
            {
                ToggleDiagnostics();
            }
        }

        private void DrawDiagnosticsPanel(float width, float height)
        {
            Rect panel = CenterPanel(width, height);
            GUI.Box(panel, GUIContent.none, panelStyle!);
            GUI.Label(
                new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, 40f),
                "Diagnostics",
                panelTitleStyle!);
            string diagnostics = diagnosticsProvider?.Invoke() ??
                "Application diagnostics are unavailable.";
            GUI.Label(
                new Rect(panel.x + 28f, panel.y + 78f, panel.width - 56f, 230f),
                diagnostics,
                panelBodyStyle!);
            if (GUI.Button(
                    new Rect(panel.x + 28f, panel.yMax - 78f, panel.width - 56f, 50f),
                    "CLOSE",
                    buttonStyle!))
            {
                ToggleDiagnostics();
            }
        }

        private static Rect CenterSettingsPanel(float width, float height)
        {
            float panelWidth = Mathf.Min(960f, width - EdgePadding * 2f);
            float panelHeight = Mathf.Min(650f, height - EdgePadding * 2f);
            return new Rect(
                (width - panelWidth) * 0.5f,
                Mathf.Max(EdgePadding, (height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);
        }

        private static Rect CenterPanel(float width, float height)
        {
            const float panelWidth = 650f;
            const float panelHeight = 390f;
            return new Rect(
                (width - panelWidth) * 0.5f,
                Mathf.Max(EdgePadding, (height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);
        }
    }
}
