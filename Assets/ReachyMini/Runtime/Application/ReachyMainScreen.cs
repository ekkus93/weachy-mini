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
        private GUIStyle? titleStyle;
        private GUIStyle? stateStyle;
        private GUIStyle? detailStyle;
        private GUIStyle? indicatorStyle;
        private GUIStyle? buttonStyle;
        private GUIStyle? smallButtonStyle;
        private GUIStyle? panelStyle;
        private GUIStyle? panelTitleStyle;
        private GUIStyle? panelBodyStyle;
        private GUIStyle? sectionStyle;
        private GUIStyle? warningStyle;

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

        private string BuildPreferredCameraCapabilityLabel(
            ReachyCameraFacing preferredFacing)
        {
            ReachyCameraCapabilitySnapshot cameraState =
                RequireCameraCapabilities();
            ReachyDeviceCameraFacing desired = preferredFacing switch
            {
                ReachyCameraFacing.Front => ReachyDeviceCameraFacing.Front,
                ReachyCameraFacing.Rear => ReachyDeviceCameraFacing.Rear,
                _ => ReachyDeviceCameraFacing.Unknown,
            };
            for (int index = 0; index < cameraState.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = cameraState.Cameras[index];
                if (desired != ReachyDeviceCameraFacing.Unknown &&
                    camera.Facing != desired)
                {
                    continue;
                }
                string resolution = camera.AnalysisResolutions.Count == 0
                    ? "no YUV analysis sizes"
                    : camera.AnalysisResolutions[0].ToString();
                string intrinsics = camera.Intrinsics.Available
                    ? "platform intrinsics"
                    : "calibration fallback required";
                return
                    $"ID {camera.CameraId} · {camera.Facing} · " +
                    $"orientation {camera.SensorOrientationDegrees}° · " +
                    $"{camera.Availability} · max {resolution} · {intrinsics}";
            }
            return "No discovered camera matches the stored preference.";
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

        private void DrawSettingsPanel(float width, float height)
        {
            ReachySettingsSnapshot? current = settingsSnapshot;
            if (current == null)
            {
                return;
            }

            Rect panel = CenterSettingsPanel(width, height);
            GUI.Box(panel, GUIContent.none, panelStyle!);
            GUI.Label(
                new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 38f),
                "Settings",
                panelTitleStyle!);

            Rect navigation = new Rect(
                panel.x + 22f,
                panel.y + 68f,
                SettingsNavigationWidth,
                panel.height - 138f);
            DrawSettingsNavigation(navigation, current.ActiveSection);

            Rect content = new Rect(
                navigation.xMax + 20f,
                navigation.y,
                panel.xMax - navigation.xMax - 42f,
                navigation.height);
            DrawSettingsContent(content, current);

            GUI.Label(
                new Rect(
                    panel.x + 24f,
                    panel.yMax - 62f,
                    panel.width - 160f,
                    44f),
                current.StatusMessage,
                warningStyle!);
            if (GUI.Button(
                    new Rect(panel.xMax - 124f, panel.yMax - 62f, 100f, 42f),
                    "CLOSE",
                    smallButtonStyle!))
            {
                ToggleSettings();
            }
        }

        private void DrawSettingsNavigation(
            Rect area,
            ReachySettingsSection active)
        {
            float y = area.y;
            for (int index = 0; index < SettingsSections.Length; ++index)
            {
                ReachySettingsSection section = SettingsSections[index];
                string label = ReachySettingsStateStore.GetSectionLabel(section);
                if (section == active)
                {
                    label = "• " + label;
                }
                if (GUI.Button(
                        new Rect(area.x, y, area.width, 48f),
                        label,
                        smallButtonStyle!))
                {
                    SelectSettingsSection(section);
                }
                y += 54f;
            }
        }

        private void DrawSettingsContent(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 32f),
                ReachySettingsStateStore.GetSectionLabel(current.ActiveSection),
                sectionStyle!);
            Rect body = new Rect(
                area.x,
                area.y + 42f,
                area.width,
                area.height - 42f);
            switch (current.ActiveSection)
            {
                case ReachySettingsSection.Providers:
                    DrawProviderSettings(body, current);
                    break;
                case ReachySettingsSection.Camera:
                    DrawCameraSettings(body, current);
                    break;
                case ReachySettingsSection.Speech:
                    DrawSpeechSettings(body, current);
                    break;
                case ReachySettingsSection.LocalModel:
                    DrawLocalModelSettings(body, current);
                    break;
                case ReachySettingsSection.Simulation:
                    DrawSimulationSettings(body, current);
                    break;
                case ReachySettingsSection.Privacy:
                    DrawPrivacySettings(body, current);
                    break;
                case ReachySettingsSection.Licenses:
                    DrawLicenseSettings(body);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(current.ActiveSection),
                        current.ActiveSection,
                        null);
            }
        }

        private void DrawProviderSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 54f),
                "ASR, TTS, LLM, and VLM are selected independently. " +
                "A stored preference does not mean the runtime is installed.",
                panelBodyStyle!);
            float y = area.y + 62f;
            foreach (ReachyProviderKind kind in ProviderKinds)
            {
                ReachyProviderSelection provider = current.GetProvider(kind);
                string label =
                    $"{ReachySettingsStateStore.GetProviderKindLabel(kind)}  " +
                    $"{provider.DisplayName}\n" +
                    $"{ReachySettingsStateStore.GetExecutionLabel(provider.Execution)} · " +
                    $"{ReachySettingsStateStore.GetConnectivityLabel(provider.Connectivity)} · " +
                    (provider.Available ? "Available" : "Unavailable");
                if (GUI.Button(
                        new Rect(area.x, y, area.width, 72f),
                        label,
                        smallButtonStyle!))
                {
                    CycleProvider(kind);
                }
                y += 80f;
            }
        }

        private void DrawCameraSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            ReachyCameraCapabilitySnapshot camera =
                RequireCameraCapabilities();
            GUI.Label(
                new Rect(area.x, area.y, area.width, 48f),
                "The robot presentation camera remains fixed. RMA-090 requests " +
                "Android permission only from these camera controls and discovers capabilities without opening a camera device.",
                panelBodyStyle!);
            float y = area.y + 54f;
            string accessLabel = camera.Permission switch
            {
                ReachyCameraPermissionState.PermanentlyDenied =>
                    "OPEN ANDROID APP SETTINGS",
                ReachyCameraPermissionState.Granted =>
                    "REFRESH CAMERA CAPABILITIES",
                ReachyCameraPermissionState.Requesting =>
                    "PERMISSION REQUEST IN PROGRESS",
                ReachyCameraPermissionState.Unsupported =>
                    "CAMERA DISCOVERY UNSUPPORTED",
                _ => "REQUEST CAMERA ACCESS",
            };
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 48f),
                    accessLabel,
                    smallButtonStyle!))
            {
                RequestCameraAccess();
            }
            y += 56f;
            GUI.Label(
                new Rect(area.x, y, area.width, 42f),
                $"PERMISSION  {camera.Permission} · FRONT {camera.FrontCameraCount} · " +
                $"REAR {camera.RearCameraCount} · AVAILABLE {camera.AvailableCameraCount}",
                warningStyle!);
            y += 48f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 48f),
                    "PREFERRED DEVICE CAMERA  " +
                    ReachySettingsStateStore.GetCameraFacingLabel(
                        current.PreferredCameraFacing),
                    smallButtonStyle!))
            {
                CyclePreferredCamera();
            }
            y += 56f;
            GUI.Label(
                new Rect(area.x, y, area.width, 48f),
                BuildPreferredCameraCapabilityLabel(
                    current.PreferredCameraFacing),
                panelBodyStyle!);
            y += 54f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    "PREVIEW / IMAGE ANALYSIS — RMA-091",
                    smallButtonStyle!))
            {
                RequestCameraPreview();
            }
            y += 52f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    $"CALIBRATION  {current.CameraCalibrationProfile}",
                    smallButtonStyle!))
            {
                RequestCameraCalibration();
            }
            y += 52f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    "REPROJECTION DIAGNOSTICS — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestReprojectionDiagnostics();
            }
            GUI.Label(
                new Rect(area.x, y + 48f, area.width, 62f),
                camera.Message + "\n" + current.ReprojectionStatus,
                warningStyle!);
        }

        private void DrawSpeechSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 54f),
                "Speech preferences are durable. Availability and network " +
                "requirements are derived from the selected ASR/TTS and voice configuration.",
                panelBodyStyle!);
            float y = area.y + 66f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 60f),
                    $"LANGUAGE  {current.SpeechLanguage}",
                    smallButtonStyle!))
            {
                CycleSpeechLanguage();
            }
            y += 70f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 60f),
                    $"VOICE  {current.SpeechVoice}",
                    smallButtonStyle!))
            {
                CycleSpeechVoice();
            }
            GUI.Label(
                new Rect(area.x, y + 76f, area.width, 100f),
                current.SpeechNetworkStatus,
                warningStyle!);
        }

        private void DrawLocalModelSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 58f),
                $"INSTALLED  {current.LocalModelCount}\nACTIVE  {current.ActiveLocalModel}",
                panelBodyStyle!);
            float half = (area.width - 12f) * 0.5f;
            float y = area.y + 72f;
            if (GUI.Button(
                    new Rect(area.x, y, half, 52f),
                    "INSTALL — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestLocalModelInstall();
            }
            if (GUI.Button(
                    new Rect(area.x + half + 12f, y, half, 52f),
                    "IMPORT — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestLocalModelImport();
            }
            y += 62f;
            if (GUI.Button(
                    new Rect(area.x, y, half, 52f),
                    "SELECT — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestLocalModelSelect();
            }
            if (GUI.Button(
                    new Rect(area.x + half + 12f, y, half, 52f),
                    "DELETE — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestLocalModelDelete();
            }
            y += 72f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 56f),
                    $"MEMORY BUDGET  {current.LocalModelMemoryBudgetMb} MB",
                    smallButtonStyle!))
            {
                CycleLocalModelMemoryBudget();
            }
            y += 66f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 56f),
                    $"CONTEXT  {current.LocalModelContextTokens} TOKENS",
                    smallButtonStyle!))
            {
                CycleLocalModelContextLength();
            }
        }

        private void DrawSimulationSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            if (GUI.Button(
                    new Rect(area.x, area.y, area.width, 58f),
                    "FIDELITY  " +
                    ReachySettingsStateStore.GetSimulationFidelityLabel(
                        current.SimulationFidelity),
                    smallButtonStyle!))
            {
                CycleSimulationFidelity();
            }
            GUI.Label(
                new Rect(area.x, area.y + 70f, area.width, 30f),
                $"CALIBRATION PROFILE  {current.SimulationCalibrationProfile}",
                panelBodyStyle!);
            if (GUI.Button(
                    new Rect(area.x, area.y + 112f, area.width, 54f),
                    "RESET TO NEUTRAL",
                    smallButtonStyle!))
            {
                RequestSimulationReset();
            }
            GUI.Label(
                new Rect(area.x, area.y + 182f, area.width, 160f),
                current.SimulationDiagnostics,
                warningStyle!);
        }

        private void DrawPrivacySettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 130f),
                current.PrivacyCloudSummary,
                warningStyle!);
            float y = area.y + 142f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 58f),
                    current.HistoryEnabled
                        ? "HISTORY  ENABLED"
                        : "HISTORY  DISABLED",
                    smallButtonStyle!))
            {
                ToggleHistory();
            }
            y += 68f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 58f),
                    current.RetentionDays == 0
                        ? "RETENTION  SESSION ONLY"
                        : $"RETENTION  {current.RetentionDays} DAYS",
                    smallButtonStyle!))
            {
                CycleRetentionDays();
            }
        }

        private void DrawLicenseSettings(Rect area)
        {
            ReachyLicenseNotice[] notices =
                ReachySettingsStateStore.GetLicenseNotices();
            float y = area.y;
            for (int index = 0; index < notices.Length; ++index)
            {
                ReachyLicenseNotice notice = notices[index];
                GUI.Label(
                    new Rect(area.x, y, area.width, 72f),
                    $"{notice.Component}\n" +
                    $"{notice.Attribution}\n" +
                    $"{notice.LicenseReference}",
                    panelBodyStyle!);
                y += 80f;
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

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            GUIStyle baseLabel = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
            };
            titleStyle = new GUIStyle(baseLabel)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.65f, 0.75f, 0.86f, 1f) },
            };
            stateStyle = new GUIStyle(baseLabel)
            {
                fontSize = 29,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            detailStyle = new GUIStyle(baseLabel)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.86f, 0.89f, 0.94f, 1f) },
            };
            indicatorStyle = new GUIStyle(baseLabel)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.54f, 0.82f, 0.72f, 1f) },
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8),
            };
            smallButtonStyle = new GUIStyle(buttonStyle)
            {
                fontSize = 13,
            };
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal =
                {
                    background = Texture2D.whiteTexture,
                    textColor = Color.white,
                },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(0, 0, 0, 0),
            };
            panelStyle.normal.background = CreatePanelTexture();
            panelTitleStyle = new GUIStyle(baseLabel)
            {
                fontSize = 25,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            panelBodyStyle = new GUIStyle(baseLabel)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.9f, 0.92f, 0.96f, 1f) },
            };
            sectionStyle = new GUIStyle(baseLabel)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            warningStyle = new GUIStyle(baseLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(1f, 0.8f, 0.48f, 1f) },
            };
        }

        private static Texture2D CreatePanelTexture()
        {
            Texture2D texture = new Texture2D(
                1,
                1,
                TextureFormat.RGBA32,
                false)
            {
                name = "ReachyMainScreenPanel",
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(
                0,
                0,
                new Color(0.035f, 0.045f, 0.062f, 0.96f));
            texture.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: true);
            return texture;
        }
    }
}
