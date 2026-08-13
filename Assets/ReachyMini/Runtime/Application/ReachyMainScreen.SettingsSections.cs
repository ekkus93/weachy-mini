#nullable enable

using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreen
    {
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
                new Rect(area.x, area.y, area.width, 112f),
                current.PrivacyCloudSummary + "\n" +
                "Raw camera, microphone, and cloud-request media are not retained by default.",
                warningStyle!);
            float y = area.y + 124f;
            string historyLabel = current.HistoryEnabled
                ? current.RetentionDays == 0
                    ? "HISTORY  SESSION ONLY"
                    : "HISTORY PERSISTENCE  ENABLED"
                : "HISTORY PERSISTENCE  DISABLED";
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 52f),
                    historyLabel,
                    smallButtonStyle!))
            {
                ToggleHistory();
            }
            y += 62f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 52f),
                    current.RetentionDays == 0
                        ? "RETENTION  SESSION ONLY"
                        : $"RETENTION  {current.RetentionDays} DAYS",
                    smallButtonStyle!))
            {
                CycleRetentionDays();
            }
            y += 62f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 52f),
                    "MEDIA RECORDING  OFF — OPT-IN REQUIRED",
                    smallButtonStyle!))
            {
                RequireSettings().ReportUnavailableAction(
                    "Private-media recording",
                    ReachyPrivateMediaRetentionPolicy.PersistentMediaRetentionUnavailableReason);
            }
            y += 62f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 52f),
                    "MEDIA EXPORT  UNAVAILABLE — CONSENT REQUIRED",
                    smallButtonStyle!))
            {
                RequireSettings().ReportUnavailableAction(
                    "Private-media export",
                    ReachyPrivateMediaRetentionPolicy.PersistentMediaRetentionUnavailableReason);
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
    }
}
