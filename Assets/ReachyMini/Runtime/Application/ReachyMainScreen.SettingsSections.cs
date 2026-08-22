#nullable enable

using ReachyMini.Providers;
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
            y += 66f;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 56f),
                    "CLEAN UP RECOVERABLE STORAGE",
                    smallButtonStyle!))
            {
                CleanupRecoverableStorage();
            }
            GUI.Label(
                new Rect(area.x, y + 60f, area.width, 58f),
                storageCleanupStatus,
                warningStyle!);
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

        private void DrawCloudLlmSettings(Rect area)
        {
            ReachyCloudLlmCredentialCoordinator coordinator = RequireCloudLlmCredentials();
            ReachyProviderProfile? profile = coordinator.CurrentProfile;

            GUI.Label(
                new Rect(area.x, area.y, area.width, 54f),
                "Configure an OpenAI-compatible cloud LLM endpoint. This is off by " +
                "default and requires explicit authorization below before any " +
                "request leaves the device.",
                panelBodyStyle!);
            float y = area.y + 62f;

            GUI.Label(new Rect(area.x, y, area.width, 24f), "BASE URL", detailStyle!);
            y += 26f;
            cloudLlmBaseUrlDraft = GUI.TextField(
                new Rect(area.x, y, area.width, 40f),
                cloudLlmBaseUrlDraft,
                textFieldStyle!);
            y += 48f;

            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    "ENDPOINT STYLE  " +
                        GetCloudLlmEndpointStyleLabel(cloudLlmEndpointStyleDraft),
                    smallButtonStyle!))
            {
                CycleCloudLlmEndpointStyle();
            }
            y += 52f;

            GUI.Label(new Rect(area.x, y, area.width, 24f), "MODEL ID", detailStyle!);
            y += 26f;
            cloudLlmModelIdDraft = GUI.TextField(
                new Rect(area.x, y, area.width, 40f),
                cloudLlmModelIdDraft,
                textFieldStyle!);
            y += 48f;

            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    "SAVE PROFILE",
                    smallButtonStyle!))
            {
                SaveCloudLlmProfile();
                profile = coordinator.CurrentProfile;
            }
            y += 52f;

            GUI.Label(
                new Rect(area.x, y, area.width, 42f),
                profile == null
                    ? "CURRENT PROFILE  none saved"
                    : $"CURRENT PROFILE  {profile.BaseUri} · " +
                        $"{GetCloudLlmEndpointStyleLabel(profile.EndpointStyle)} · " +
                        $"{profile.GetModelId(ReachyProviderModelRole.Text)}",
                warningStyle!);
            y += 48f;

            GUI.Label(new Rect(area.x, y, area.width, 24f), "API KEY", detailStyle!);
            y += 26f;
            cloudLlmApiKeyDraft = GUI.PasswordField(
                new Rect(area.x, y, area.width, 40f),
                cloudLlmApiKeyDraft,
                '*',
                textFieldStyle!);
            y += 48f;

            if (GUI.Button(
                    new Rect(area.x, y, area.width, 44f),
                    "SAVE API KEY",
                    smallButtonStyle!))
            {
                SaveCloudLlmApiKey();
            }
            y += 52f;

            GUI.Label(
                new Rect(area.x, y, area.width, 24f),
                coordinator.SecretStoreAvailable
                    ? $"API KEY  {(coordinator.HasApiKey ? "configured" : "not configured")} · Android Keystore"
                    : "API KEY  storage requires an Android device",
                warningStyle!);
            y += 42f;

            GUI.Label(
                new Rect(area.x, y, area.width, 54f),
                "Enabling cloud LLM sends your conversation text to the base URL " +
                "above. Nothing is sent off-device until you explicitly authorize it.",
                warningStyle!);
            y += 62f;

            bool authorized = coordinator.IsAuthorized;
            if (GUI.Button(
                    new Rect(area.x, y, area.width, 48f),
                    authorized
                        ? "REVOKE CLOUD LLM AUTHORIZATION"
                        : "AUTHORIZE CLOUD LLM",
                    smallButtonStyle!))
            {
                if (authorized)
                {
                    RevokeCloudLlmAuthorization();
                }
                else
                {
                    AuthorizeCloudLlm();
                }
            }
            y += 56f;

            GUI.Label(
                new Rect(area.x, y, area.width, 62f),
                cloudLlmStatus,
                warningStyle!);
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
