#nullable enable

using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreen
    {
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
