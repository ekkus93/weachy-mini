#nullable enable

using System;

namespace ReachyMini.Validation
{
    [Serializable]
    internal sealed class Rma134AcceptanceCheckpoint
    {
        public int schema_version;
        public int sequence;
        public string stage = string.Empty;
        public double elapsed_milliseconds;
        public int managed_thread_id;
        public string device_model = string.Empty;
        public string detail = string.Empty;
    }

    [Serializable]
    internal sealed class Rma134AcceptanceReport
    {
        public string status = string.Empty;
        public string error = string.Empty;
        public string device_model = string.Empty;
        public string operating_system = string.Empty;
        public int reachy_llama_abi;
        public string manifest_id = string.Empty;
        public string model_id = string.Empty;
        public string artifact_sha256 = string.Empty;
        public long artifact_bytes;
        public double load_milliseconds;
        public string initial_generation_status = string.Empty;
        public int initial_stream_text_events;
        public int initial_stream_utf8_bytes;
        public string initial_prompt_tokens = string.Empty;
        public string initial_generated_tokens = string.Empty;
        public string initial_speech = string.Empty;
        public string cancellation_status = string.Empty;
        public int cancellation_text_events_before_cancel;
        public string reset_status = string.Empty;
        public string reset_epoch_before = string.Empty;
        public string reset_epoch_after = string.Empty;
        public string reload_status = string.Empty;
        public string post_reload_generation_status = string.Empty;
        public string post_reload_speech = string.Empty;
        public int physics_steps;
        public double physics_p50_microseconds;
        public double physics_p95_microseconds;
        public double physics_max_microseconds;
        public int physics_deadline_misses;
        public double physics_p95_budget_microseconds;
        public long initial_rss_bytes;
        public long final_rss_bytes;
        public double initial_battery_temperature_c;
        public double final_battery_temperature_c;
        public bool adaptive_resource_changes_applied;
        public bool network_fallback_used;
        public bool json_repair_used;
    }

    internal sealed class Rma134ArtifactVerification
    {
        internal long bytes;
        internal string sha256 = string.Empty;
    }

    internal sealed class Rma134PhysicsTimingReport
    {
        internal int steps;
        internal double p50_microseconds;
        internal double p95_microseconds;
        internal double max_microseconds;
        internal int deadline_misses;
    }
}
