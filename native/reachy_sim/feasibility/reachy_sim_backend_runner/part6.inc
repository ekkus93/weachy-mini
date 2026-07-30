        "\"capability_flags\":%" PRIu64 ",\"body_count\":%u,"
        "\"joint_count\":%u,\"actuator_count\":%u,"
        "\"completed_steps\":%u,\"sequence\":%" PRIu64 ","
        "\"simulated_seconds\":%.17g,\"snapshot_replay_identical\":true,"
        "\"sleep_reset_status\":\"%s\",\"state_format_version\":%u,"
        "\"state_payload_bytes\":%zu,\"model_hash\":%" PRIu64 ","
        "\"qpos_count\":%u,\"qvel_count\":%u,\"continuity_id\":%u,"
        "\"constraint_count\":%u,\"equality_constraint_count\":%u,"
        "\"maximum_constraint_residual\":%.17g,"
        "\"maximum_equality_constraint_residual\":%.17g}\n",
        reachy_sim_version_string(),
        capabilities.capability_flags,
        initial.legacy.body_count,
        initial.legacy.joint_count,
        initial.legacy.actuator_count,
        step_count,
        final.legacy.sequence,
        final.legacy.simulation_time,
        reachy_sim_status_string(sleep_status),
        final.payload.state_format_version,
        final.byte_count,
        final.payload.model_hash,
        final.payload.qpos_count,
        final.payload.qvel_count,
        final.payload.continuity_id,
        final.payload.constraint_count,
        final.payload.equality_constraint_count,
        final.payload.maximum_constraint_residual,
        final.payload.maximum_equality_constraint_residual);
    result = 0;

cleanup:
    free_state(&reset);
    free_state(&restored);
    free_state(&final);
    free_state(&initial);
    if(reachy_sim_destroy(handle) != REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(stderr, "destroy failed\n");
        result = 1;
    }
    return result;
}
