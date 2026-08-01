#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyMainScreen : MonoBehaviour
    {
        private const float ReferenceWidth = 1080f;
        private const float EdgePadding = 28f;
        private const float TopCardWidth = 430f;
        private const float TopCardHeight = 190f;
        private const float BottomBarHeight = 118f;

        [SerializeField]
        private Camera? presentationCamera;

        private ReachyMainScreenStateStore? stateStore;
        private ReachyMainScreenSnapshot? snapshot;
        private Func<string>? diagnosticsProvider;
        private GUIStyle? titleStyle;
        private GUIStyle? stateStyle;
        private GUIStyle? detailStyle;
        private GUIStyle? indicatorStyle;
        private GUIStyle? buttonStyle;
        private GUIStyle? panelStyle;
        private GUIStyle? panelTitleStyle;
        private GUIStyle? panelBodyStyle;

        public ReachyMainScreenSnapshot? Snapshot => snapshot;

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
            diagnosticsProvider = currentDiagnostics ??
                throw new ArgumentNullException(nameof(currentDiagnostics));
            snapshot = stateStore.Current;
            stateStore.Changed += OnStateChanged;
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
                    "Settings shell opened. Provider and device settings are implemented in RMA-082.");
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

        private void OnDestroy()
        {
            if (stateStore != null)
            {
                stateStore.Changed -= OnStateChanged;
            }
            stateStore = null;
            diagnosticsProvider = null;
        }

        private void OnStateChanged(
            object? sender,
            ReachyMainScreenChangedEventArgs eventArgs)
        {
            snapshot = eventArgs.Snapshot;
        }

        private ReachyMainScreenStateStore RequireStore()
        {
            return stateStore ?? throw new InvalidOperationException(
                "The main screen is not bound to application state.");
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
            float scale = Mathf.Clamp(safeArea.width / ReferenceWidth, 0.72f, 1.35f);
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
                ? "CAMERA"
                : "CAMERA\nFIXED VIEW";
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
            Rect panel = CenterPanel(width, height);
            GUI.Box(panel, GUIContent.none, panelStyle!);
            GUI.Label(
                new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, 40f),
                "Settings",
                panelTitleStyle!);
            GUI.Label(
                new Rect(panel.x + 28f, panel.y + 78f, panel.width - 56f, 150f),
                "The settings entry point is active. RMA-082 adds provider, " +
                "camera, speech, local-model, simulation, privacy, and license settings. " +
                "No placeholder selection is treated as configured.",
                panelBodyStyle!);
            if (GUI.Button(
                    new Rect(panel.x + 28f, panel.yMax - 78f, panel.width - 56f, 50f),
                    "CLOSE",
                    buttonStyle!))
            {
                ToggleSettings();
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
                fontSize = 16,
                normal = { textColor = new Color(0.9f, 0.92f, 0.96f, 1f) },
            };
        }

        private static Texture2D CreatePanelTexture()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "ReachyMainScreenPanel",
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, new Color(0.035f, 0.045f, 0.062f, 0.94f));
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }
    }
}
