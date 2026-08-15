#nullable enable

using ReachyMini.Diagnostics;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreen
    {
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
            ReachyDiagnosticsScreenSnapshot diagnostics =
                diagnosticsProvider?.Invoke() ??
                ReachyDiagnosticsScreenSnapshot.FromLegacyText(
                    "Application diagnostics are unavailable because no diagnostics source is bound.");
            string diagnosticsText = diagnostics.ToDisplayText();
            Rect viewport = new Rect(
                panel.x + 28f,
                panel.y + 78f,
                panel.width - 56f,
                panel.height - 252f);
            float contentHeight = Mathf.Max(
                viewport.height,
                panelBodyStyle!.CalcHeight(
                    new GUIContent(diagnosticsText),
                    viewport.width - 20f) + 12f);
            Rect content = new Rect(
                0f,
                0f,
                viewport.width - 20f,
                contentHeight);
            diagnosticsScrollPosition = GUI.BeginScrollView(
                viewport,
                diagnosticsScrollPosition,
                new Rect(0f, 0f, viewport.width - 20f, contentHeight));
            GUI.Label(content, diagnosticsText, panelBodyStyle!);
            GUI.EndScrollView();

            GUI.Label(
                new Rect(
                    panel.x + 28f,
                    panel.yMax - 158f,
                    panel.width - 56f,
                    44f),
                diagnosticBundleExportStatus,
                detailStyle!);

            const float actionGap = 14f;
            float actionWidth = (panel.width - 56f - actionGap) * 0.5f;
            if (GUI.Button(
                    new Rect(panel.x + 28f, panel.yMax - 92f, actionWidth, 64f),
                    "EXPORT REDACTED\nBUNDLE",
                    buttonStyle!))
            {
                ExportDiagnosticBundle();
            }
            if (GUI.Button(
                    new Rect(
                        panel.x + 28f + actionWidth + actionGap,
                        panel.yMax - 92f,
                        actionWidth,
                        64f),
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
            float panelWidth = Mathf.Min(900f, width - EdgePadding * 2f);
            float panelHeight = Mathf.Min(620f, height - EdgePadding * 2f);
            return new Rect(
                (width - panelWidth) * 0.5f,
                Mathf.Max(EdgePadding, (height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);
        }
    }
}
