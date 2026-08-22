#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreen
    {
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
                case ReachySettingsSection.CloudLlm:
                    DrawCloudLlmSettings(body);
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
                    CameraPreviewActive
                        ? "STOP PREVIEW / IMAGE ANALYSIS"
                        : "START PREVIEW / IMAGE ANALYSIS",
                    smallButtonStyle!))
            {
                RequestCameraPreview();
            }
            y += 52f;
            if (CameraPreviewActive)
            {
                Texture? previewTexture = CameraPreviewTexture;
                float previewHeight = area.width * 0.75f;
                if (previewTexture != null)
                {
                    GUI.DrawTexture(
                        new Rect(area.x, y, area.width, previewHeight),
                        previewTexture,
                        ScaleMode.ScaleToFit);
                }
                else
                {
                    GUI.Label(
                        new Rect(area.x, y, area.width, 42f),
                        "Preview session starting; no frame uploaded yet.",
                        panelBodyStyle!);
                    previewHeight = 42f;
                }
                y += previewHeight + 8f;
            }
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
    }
}
