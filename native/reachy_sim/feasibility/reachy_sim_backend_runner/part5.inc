        &command_size);
    if(commands == NULL)
    {
        (void)fprintf(stderr, "command batch allocation failed\n");
        goto cleanup;
    }
    if(reachy_sim_submit_commands(handle, commands, command_size) !=
       REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "submit commands");
        free(commands);
        goto cleanup;
    }
    free(commands);

    if(initial.legacy.body_count > 0U)
    {
        ReachySimWrenchCommand wrench = {0};
        wrench.abi_version = REACHY_SIM_ABI_VERSION;
        wrench.struct_size = (uint32_t)sizeof(wrench);
        wrench.body_id = 1U;
        wrench.duration_seconds = 0.0;
        if(reachy_sim_apply_wrench(handle, &wrench) != REACHY_SIM_STATUS_OK)
        {
            print_handle_error(handle, "apply wrench");
            goto cleanup;
        }
    }

    if(reachy_sim_step(handle, step_count) != REACHY_SIM_STATUS_OK ||
       !copy_authoritative_state(handle, &final))
    {
        print_handle_error(handle, "step");
        goto cleanup;
    }
    if(final.legacy.sequence != step_count ||
       final.payload.continuity_id != initial.payload.continuity_id ||
       final.payload.model_hash != initial.payload.model_hash)
    {
        (void)fprintf(stderr, "authoritative state identity changed during ordinary steps\n");
        goto cleanup;
    }

    size_t checkpoint_size = 0U;
    uint8_t* const checkpoint = copy_snapshot(handle, &checkpoint_size);
    if(checkpoint == NULL)
    {
        goto cleanup;
    }
    if(reachy_sim_step(handle, 10U) != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "first replay step");
        free(checkpoint);
        goto cleanup;
    }
    size_t expected_size = 0U;
    uint8_t* const expected = copy_snapshot(handle, &expected_size);
    if(expected == NULL)
    {
        free(checkpoint);
        goto cleanup;
    }
    if(reachy_sim_restore_snapshot(handle, checkpoint, checkpoint_size) !=
            REACHY_SIM_STATUS_OK ||
       !copy_authoritative_state(handle, &restored) ||
       reachy_sim_step(handle, 10U) != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "snapshot replay");
        free(expected);
        free(checkpoint);
        goto cleanup;
    }
    if(restored.payload.continuity_id != final.payload.continuity_id + 1U ||
       restored.legacy.sequence != final.legacy.sequence)
    {
        (void)fprintf(stderr, "snapshot restore did not create a new continuity epoch\n");
        free(expected);
        free(checkpoint);
        goto cleanup;
    }
    size_t replay_size = 0U;
    uint8_t* const replay = copy_snapshot(handle, &replay_size);
    const int replay_identical = replay != NULL && replay_size == expected_size &&
        memcmp(replay, expected, expected_size) == 0;
    free(replay);
    free(expected);
    free(checkpoint);
    if(!replay_identical)
    {
        (void)fprintf(stderr, "snapshot replay was not byte-identical\n");
        goto cleanup;
    }

    const int32_t sleep_status = reachy_sim_reset(
        handle,
        (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST);
    if(sleep_status != REACHY_SIM_STATUS_OK &&
       sleep_status != REACHY_SIM_STATUS_UNSUPPORTED)
    {
        print_handle_error(handle, "sleep reset");
        goto cleanup;
    }
    if(reachy_sim_reset(
           handle,
           (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE) !=
            REACHY_SIM_STATUS_OK ||
       !copy_authoritative_state(handle, &reset))
    {
        print_handle_error(handle, "neutral reset");
        goto cleanup;
    }
    if(reset.payload.continuity_id <= restored.payload.continuity_id ||
       reset.legacy.sequence != 0U || reset.legacy.simulation_time != 0.0)
    {
        (void)fprintf(stderr, "neutral reset state is inconsistent\n");
        goto cleanup;
    }

    (void)printf(
        "{\"status\":\"ok\",\"backend_version\":\"%s\"," 
