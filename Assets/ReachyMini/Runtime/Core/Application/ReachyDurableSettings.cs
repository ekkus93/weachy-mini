#nullable enable

namespace ReachyMini.AppState
{
    public sealed class ReachyDurableSettings
    {
        public int AsrExecution { get; set; }

        public int TtsExecution { get; set; }

        public int LlmExecution { get; set; }

        public int VlmExecution { get; set; }

        public int PreferredCameraFacing { get; set; }

        public string SpeechLanguage { get; set; } = "System default";

        public string SpeechVoice { get; set; } = "System default";

        public int LocalModelMemoryBudgetMb { get; set; } = 1024;

        public int LocalModelContextTokens { get; set; } = 4096;

        public int SimulationFidelity { get; set; }

        public bool HistoryEnabled { get; set; }

        public int RetentionDays { get; set; } = 30;
    }
}
