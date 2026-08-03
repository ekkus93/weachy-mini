#!/usr/bin/env python3
"""Integrate the RMA-090 camera discovery boundary into existing application files."""

from __future__ import annotations

from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    target = Path(path)
    text = target.read_text(encoding="utf-8")
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"Expected one integration anchor in {path}, found {count}: {old[:80]!r}"
        )
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


def patch_camera_bridge() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraDiscovery.cs"
    replace_once(
        path,
        "            int requestCount = ReadHistory(RequestCountKey);\n",
        "            int requestCount = state.Current.PermissionRequestCount;\n",
    )
    replace_once(
        path,
        """            Debug.Log(
                "RMA090_CAMERA_CAPABILITIES " +
                eventArgs.Snapshot.Summary);
""",
        """            ReachyCameraCapabilitySnapshot snapshot = eventArgs.Snapshot;
            Debug.Log(
                "RMA090_CAMERA_CAPABILITIES " +
                snapshot.Summary);
            for (int index = 0; index < snapshot.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = snapshot.Cameras[index];
                string largestResolution = camera.AnalysisResolutions.Count == 0
                    ? "none"
                    : camera.AnalysisResolutions[0].ToString();
                Debug.Log(
                    $"RMA090_CAMERA id={camera.CameraId} facing={camera.Facing} " +
                    $"orientation={camera.SensorOrientationDegrees} " +
                    $"availability={camera.Availability} " +
                    $"resolutions={camera.AnalysisResolutions.Count} " +
                    $"top={largestResolution} intrinsics={camera.Intrinsics.Source}");
            }
""",
    )


def patch_java_bridge() -> None:
    path = (
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyCameraDiscoveryBridge.java"
    )
    replace_once(
        path,
        """        if (level == CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_EXTERNAL) {
            return "external";
        }
""",
        "",
    )


def patch_bootstrap() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyMainScreenBootstrap.cs"
    replace_once(
        path,
        """                ReachySettingsApplicationCompositionProvider provider =
                    shellObject.AddComponent<
                        ReachySettingsApplicationCompositionProvider>();
                provider.Configure(runtime, camera, screen);
""",
        """                ReachyAndroidCameraDiscovery cameraDiscovery =
                    shellObject.AddComponent<ReachyAndroidCameraDiscovery>();
                ReachySettingsApplicationCompositionProvider provider =
                    shellObject.AddComponent<
                        ReachySettingsApplicationCompositionProvider>();
                provider.Configure(runtime, camera, screen, cameraDiscovery);
""",
    )


def patch_composition() -> None:
    path = (
        "Assets/ReachyMini/Runtime/Application/"
        "ReachySettingsApplicationCompositionProvider.cs"
    )
    replace_once(
        path,
        """        [SerializeField]
        private ReachyMainScreen? mainScreen;

        private bool compositionCreated;
""",
        """        [SerializeField]
        private ReachyMainScreen? mainScreen;

        [SerializeField]
        private ReachyAndroidCameraDiscovery? cameraDiscovery;

        private bool compositionCreated;
""",
    )
    replace_once(
        path,
        """        public void Configure(
            ReachyProductionAuthoritativeRuntime runtime,
            Camera camera,
            ReachyMainScreen screen)
""",
        """        public void Configure(
            ReachyProductionAuthoritativeRuntime runtime,
            Camera camera,
            ReachyMainScreen screen,
            ReachyAndroidCameraDiscovery discovery)
""",
    )
    replace_once(
        path,
        """            mainScreen = screen ?? throw new ArgumentNullException(nameof(screen));
""",
        """            mainScreen = screen ?? throw new ArgumentNullException(nameof(screen));
            cameraDiscovery = discovery ??
                throw new ArgumentNullException(nameof(discovery));
""",
    )
    replace_once(
        path,
        """            ReachyMainScreen screen = mainScreen ??
                throw new InvalidOperationException(
                    "The settings application requires the main screen.");

            return ReachyApplicationComposition.CreateComplete(
""",
        """            ReachyMainScreen screen = mainScreen ??
                throw new InvalidOperationException(
                    "The settings application requires the main screen.");
            ReachyAndroidCameraDiscovery discovery = cameraDiscovery ??
                throw new InvalidOperationException(
                    "The settings application requires Android camera discovery.");

            return ReachyApplicationComposition.CreateComplete(
""",
    )
    replace_once(
        path,
        """                        resolver =>
                            new ReachyFixedCameraApplicationService(camera)),
""",
        """                        resolver =>
                            new ReachyDiscoveredCameraApplicationService(
                                camera,
                                discovery)),
""",
    )
    replace_once(
        path,
        """                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence))),
""",
        """                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence),
                            discovery)),
""",
    )
    replace_once(
        path,
        """        private readonly ReachySettingsPersistenceApplicationService persistence;
        private readonly ReachyMainScreenStateStore stateStore =
""",
        """        private readonly ReachySettingsPersistenceApplicationService persistence;
        private readonly ReachyAndroidCameraDiscovery cameraDiscovery;
        private readonly ReachyMainScreenStateStore stateStore =
""",
    )
    replace_once(
        path,
        """            IReachyBehaviorService behavior,
            ReachySettingsPersistenceApplicationService persistence)
""",
        """            IReachyBehaviorService behavior,
            ReachySettingsPersistenceApplicationService persistence,
            ReachyAndroidCameraDiscovery cameraDiscovery)
""",
    )
    replace_once(
        path,
        """            this.persistence = persistence ??
                throw new ArgumentNullException(nameof(persistence));
            dependencies = new IReachyApplicationService[]
""",
        """            this.persistence = persistence ??
                throw new ArgumentNullException(nameof(persistence));
            this.cameraDiscovery = cameraDiscovery ??
                throw new ArgumentNullException(nameof(cameraDiscovery));
            dependencies = new IReachyApplicationService[]
""",
    )
    replace_once(
        path,
        """            persistence.Settings.Changed += OnSettingsChanged;

            UpdateMainScreenCapabilities();
""",
        """            persistence.Settings.Changed += OnSettingsChanged;
            cameraDiscovery.State.Changed += OnCameraCapabilitiesChanged;

            UpdateMainScreenCapabilities();
""",
    )
    replace_once(
        path,
        """            screen.Bind(
                stateStore,
                persistence.Settings,
                BuildDiagnostics,
                ResetSimulation);
""",
        """            screen.Bind(
                stateStore,
                persistence.Settings,
                BuildDiagnostics,
                ResetSimulation,
                cameraDiscovery.State,
                cameraDiscovery.RequestAccessOrRefresh);
""",
    )
    replace_once(
        path,
        """            persistence.Settings.Changed -= OnSettingsChanged;
            for (int index = 0; index < dependencies.Length; ++index)
""",
        """            persistence.Settings.Changed -= OnSettingsChanged;
            cameraDiscovery.State.Changed -= OnCameraCapabilitiesChanged;
            for (int index = 0; index < dependencies.Length; ++index)
""",
    )
    replace_once(
        path,
        """        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
""",
        """        private void OnCameraCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            UpdateMainScreenCapabilities();
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
""",
    )
    replace_once(
        path,
        """            stateStore.SetCapabilities(
                "Fixed front / three-quarter",
                false,
""",
        """            stateStore.SetCapabilities(
                "Fixed front / three-quarter",
                cameraDiscovery.State.Current.SelectionAvailable,
""",
    )
    replace_once(
        path,
        """            var lines = new List<string>(dependencies.Length + 3)
            {
                "Application shell: active",
                $"Settings file: {persistence.PersistencePath}",
                $"Settings status: {persistence.Settings.Current.StatusMessage}",
            };
""",
        """            var lines = new List<string>(dependencies.Length + 4)
            {
                "Application shell: active",
                $"Settings file: {persistence.PersistencePath}",
                $"Settings status: {persistence.Settings.Current.StatusMessage}",
                $"Camera discovery: {cameraDiscovery.State.Current.Summary}",
            };
""",
    )


def patch_main_screen() -> None:
    path = "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.cs"
    replace_once(
        path,
        """        private ReachySettingsStateStore? settingsStore;
        private ReachySettingsSnapshot? settingsSnapshot;
        private Func<string>? diagnosticsProvider;
""",
        """        private ReachySettingsStateStore? settingsStore;
        private ReachySettingsSnapshot? settingsSnapshot;
        private ReachyCameraCapabilityStateStore? cameraCapabilityStore;
        private ReachyCameraCapabilitySnapshot? cameraCapabilitySnapshot;
        private Action? requestCameraAccess;
        private Func<string>? diagnosticsProvider;
""",
    )
    replace_once(
        path,
        """        public ReachySettingsSnapshot? SettingsSnapshot => settingsSnapshot;

        public Camera? PresentationCamera => presentationCamera;
""",
        """        public ReachySettingsSnapshot? SettingsSnapshot => settingsSnapshot;

        public ReachyCameraCapabilitySnapshot? CameraCapabilitySnapshot =>
            cameraCapabilitySnapshot;

        public Camera? PresentationCamera => presentationCamera;
""",
    )
    old_bind = """        public void Bind(
            ReachyMainScreenStateStore store,
            ReachySettingsStateStore durableSettings,
            Func<string> currentDiagnostics,
            Func<ReachySettingsResetOutcome> resetSimulationOperation)
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
            diagnosticsProvider = currentDiagnostics ??
                throw new ArgumentNullException(nameof(currentDiagnostics));
            resetSimulation = resetSimulationOperation ??
                throw new ArgumentNullException(nameof(resetSimulationOperation));
            snapshot = stateStore.Current;
            settingsSnapshot = settingsStore.Current;
            stateStore.Changed += OnStateChanged;
            settingsStore.Changed += OnSettingsChanged;
        }
"""
    new_bind = """        public void Bind(
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
"""
    replace_once(path, old_bind, new_bind)
    replace_once(
        path,
        """        public void RequestCameraSelection()
        {
            ReachyMainScreenStateStore store = RequireStore();
            if (!store.Current.CameraSelectionAvailable)
            {
                store.ReportUnavailableAction(
                    "Camera selection",
                    "the CameraX bridge begins in RMA-090; the fixed robot view remains active");
                return;
            }
            store.SetInteraction(
                store.Current.InteractionState,
                "Camera selector opened.");
        }
""",
        """        public void RequestCameraSelection()
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
                    "Camera discovery",
                    camera.Message);
                return;
            }

            (requestCameraAccess ?? throw new InvalidOperationException(
                "The camera access operation is not bound."))();
            camera = RequireCameraCapabilities();
            store.SetInteraction(
                camera.Permission == ReachyCameraPermissionState.Faulted
                    ? ReachyInteractionState.Error
                    : ReachyInteractionState.Unavailable,
                camera.Message);
        }

        public void RequestCameraAccess()
        {
            RequestCameraSelection();
        }
""",
    )
    replace_once(
        path,
        """            RequireSettings().ReportUnavailableAction(
                "Camera preview",
                "CameraX preview begins in RMA-091");
""",
        """            RequireSettings().ReportUnavailableAction(
                "Camera preview",
                "RMA-090 discovers capabilities only; CameraX preview and ImageAnalysis begin in RMA-091");
""",
    )
    replace_once(
        path,
        """            if (settingsStore != null)
            {
                settingsStore.Changed -= OnSettingsChanged;
            }
            stateStore = null;
            settingsStore = null;
            diagnosticsProvider = null;
            resetSimulation = null;
""",
        """            if (settingsStore != null)
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
""",
    )
    replace_once(
        path,
        """        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            settingsSnapshot = eventArgs.Snapshot;
        }

        private ReachyMainScreenStateStore RequireStore()
""",
        """        private void OnSettingsChanged(
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
""",
    )
    replace_once(
        path,
        """        private ReachySettingsStateStore RequireSettings()
        {
            return settingsStore ?? throw new InvalidOperationException(
                "The main screen is not bound to settings state.");
        }

        private void OnGUI()
""",
        """        private ReachySettingsStateStore RequireSettings()
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
""",
    )
    replace_once(
        path,
        """            string cameraLabel = current.CameraSelectionAvailable
                ? "CAMERA"
                : "CAMERA\nFIXED VIEW";
""",
        """            string cameraLabel = current.CameraSelectionAvailable
                ? "DEVICE CAMERA"
                : "CAMERA\nACCESS";
""",
    )
    old_camera_panel = """        private void DrawCameraSettings(
            Rect area,
            ReachySettingsSnapshot current)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width, 54f),
                "The robot presentation camera remains fixed. These controls " +
                "store the preferred Android device camera and expose future CameraX actions.",
                panelBodyStyle!);
            float y = area.y + 66f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 58f),
                    "PREFERRED DEVICE CAMERA  " +
                    ReachySettingsStateStore.GetCameraFacingLabel(
                        current.PreferredCameraFacing),
                    smallButtonStyle!))
            {
                CyclePreferredCamera();
            }
            y += 68f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 54f),
                    "PREVIEW — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestCameraPreview();
            }
            y += 64f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 54f),
                    $"CALIBRATION  {current.CameraCalibrationProfile}",
                    smallButtonStyle!))
            {
                RequestCameraCalibration();
            }
            y += 64f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 54f),
                    "REPROJECTION DIAGNOSTICS — UNAVAILABLE",
                    smallButtonStyle!))
            {
                RequestReprojectionDiagnostics();
            }
            GUI.Label(
                new Rect(area.x, y + 62f, area.width, 70f),
                current.ReprojectionStatus,
                warningStyle!);
        }
"""
    new_camera_panel = """        private void DrawCameraSettings(
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
"""
    replace_once(path, old_camera_panel, new_camera_panel)


def patch_local_validation() -> None:
    path = ".github/workflows/local-unity-android-validation.yml"
    anchor = """      - name: Install and run RMA-022 lifecycle acceptance
        id: lifecycle_acceptance
"""
    insertion = """      - name: Install and run RMA-090 camera discovery acceptance
        id: camera_discovery_acceptance
        if: ${{ github.event_name == 'push' || inputs.install_physical_device }}
        shell: bash
        env:
          RMA090_CAMERA_REPORT_DIR: ${{ runner.temp }}/rma090-camera-device-report
        run: bash scripts/run_rma090_camera_discovery_acceptance_android.sh

      - name: Upload RMA-090 camera discovery evidence
        if: ${{ always() && steps.camera_discovery_acceptance.outcome != 'skipped' }}
        uses: actions/upload-artifact@v4
        with:
          name: rma090-camera-device-report-${{ github.sha }}
          path: ${{ runner.temp }}/rma090-camera-device-report
          if-no-files-found: error
          retention-days: 30

""" + anchor
    replace_once(path, anchor, insertion)


def main() -> None:
    patch_camera_bridge()
    patch_java_bridge()
    patch_bootstrap()
    patch_composition()
    patch_main_screen()
    patch_local_validation()


if __name__ == "__main__":
    main()
