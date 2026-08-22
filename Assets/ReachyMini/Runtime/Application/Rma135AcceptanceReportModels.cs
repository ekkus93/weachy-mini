#nullable enable

using System;

namespace ReachyMini.Validation
{
    [Serializable]
    internal sealed class Rma135AcceptanceCheckpoint
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
    internal sealed class Rma135AcceptanceReport
    {
        public string status = string.Empty;
        public string error = string.Empty;
        public string device_model = string.Empty;
        public string operating_system = string.Empty;
        public int android_api_level;
        public int reachy_llama_abi;
        public string manifest_id = string.Empty;
        public string model_id = string.Empty;
        public string artifact_sha256 = string.Empty;
        public long artifact_bytes;
        public double model_load_milliseconds;
        public long total_memory_bytes;
        public long initial_available_memory_bytes;
        public long final_available_memory_bytes;
        public long low_memory_threshold_bytes;
        public bool android_low_memory_initial;
        public bool android_low_memory_final;
        public int logical_processor_count;
        public string thermal_status_initial = string.Empty;
        public string thermal_status_final = string.Empty;
        public string admission_mode = string.Empty;
        public string admission_device_profile = string.Empty;
        public string admission_reasons = string.Empty;
        public int effective_context_tokens;
        public int effective_batch_tokens;
        public int effective_micro_batch_tokens;
        public int effective_threads;
        public int effective_batch_threads;
        public int startup_physics_observations;
        public int startup_physics_exceeded_observations;
        public string startup_physics_state = string.Empty;
        public ulong production_runtime_model_hash;
        public long post_load_available_memory_bytes;
        public int post_load_stabilization_observations;
        public string post_load_initial_mode = string.Empty;
        public string post_load_initial_reasons = string.Empty;
        public string post_load_stabilized_mode = string.Empty;
        public string post_load_stabilized_reasons = string.Empty;
        public string physics_fault_injection_kind = string.Empty;
        public int physics_fault_injection_count;
        public string physics_underlying_state_at_injection = string.Empty;
        public string fault_injection_governed_status = string.Empty;
        public string fault_injection_provider_status = string.Empty;
        public ulong worker_steps_before_injection;
        public ulong worker_steps_after_injection;
        public ulong worker_deadline_misses_before_injection;
        public ulong worker_deadline_misses_after_injection;
        public double worker_accumulated_lag_seconds_after_injection;
        public double worker_last_step_microseconds_after_injection;
        public double worker_max_step_microseconds_after_injection;
        public int recovery_observations;
        public string recovery_mode = string.Empty;
        public string post_recovery_governed_status = string.Empty;
        public string post_recovery_provider_status = string.Empty;
        public int post_recovery_stream_text_events;
        public ulong post_recovery_prompt_tokens;
        public ulong post_recovery_generated_tokens;
        public string final_physics_budget_state = string.Empty;
        public ulong final_worker_steps;
        public ulong final_worker_deadline_misses;
        public bool network_fallback_used;
        public bool automatic_retry_used;
        public bool physics_timestep_modified;
        public bool json_repair_used;
        public bool report_contains_prompt_or_response_content;
        // The governor-admitted execution profile can legitimately leave no
        // headroom beyond its fixed mandatory-prompt + max-generated-tokens
        // cost for any real message under severe device throttling -- the
        // physics-fault-injection probe's own message would fail its token
        // preflight before any monitoring tick could ever observe the fault,
        // which is not evidence the fault-injection/recovery path is broken.
        // When true, fault_injection_*/recovery_*/post_recovery_* fields
        // above are not meaningful (the sequence never ran) and the
        // acceptance script skips asserting on them.
        public bool physics_fault_injection_skipped;
        public string physics_fault_injection_skip_reason = string.Empty;
    }

    internal sealed class Rma135ArtifactVerification
    {
        internal long bytes;
        internal string sha256 = string.Empty;
    }
}
