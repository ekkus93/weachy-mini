            backend.context,
            2U,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "replayed wrench trajectory");
    size_t replay_size = 0U;
    uint8_t* const replay = copy_snapshot(&backend, &replay_size, "replayed wrench snapshot");
    check_true(expected != NULL && replay != NULL, "replay snapshots exist");
    if(expected != NULL && replay != NULL)
    {
        check_true(expected_size == replay_size, "replay snapshot sizes match");
        check_true(
            expected_size == replay_size &&
                memcmp(expected, replay, expected_size) == 0,
            "wrench snapshot replay is byte-identical");
    }

    wrench = wrench_command(-1.0);
    check_status(
        backend.operations->apply_wrench(
            backend.context,
            &wrench,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "negative wrench duration rejection");
    wrench = wrench_command(0.0);
    wrench.body_id = 0U;
    check_status(
        backend.operations->apply_wrench(
            backend.context,
            &wrench,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "world-body wrench rejection");

    if(replay != NULL)
    {
        ReachySimSnapshotHeader header = {0};
        memcpy(&header, replay, sizeof(header));
        header.model_hash ^= UINT64_C(1);
        memcpy(replay, &header, sizeof(header));
        check_status(
            backend.operations->restore_snapshot(
                backend.context,
                replay,
                replay_size,
                error,
                sizeof(error)),
            REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
            "snapshot model mismatch rejection");
    }

    free(replay);
    free(expected);
    free(initial);
    backend.operations->destroy(backend.context);
}

static void test_command_snapshot_replay(void)
{
    ReachySimBackendInstance backend = create_backend();
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    CommandBatchTwo first = command_batch(
        1U,
        1U,
        actuator_command(0U, 0.6),
        actuator_command(0U, 0.0));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &first,
            command_batch_size(first.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "first replay command");
    check_status(
        backend.operations->step(backend.context, 3U, error, sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "pre-snapshot trajectory");

    size_t checkpoint_size = 0U;
    uint8_t* const checkpoint = copy_snapshot(
        &backend,
        &checkpoint_size,
        "command replay checkpoint");
    CommandBatchTwo second = command_batch(
        2U,
        1U,
        actuator_command(0U, -0.2),
        actuator_command(0U, 0.0));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &second,
            command_batch_size(second.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "second command");
    check_status(
        backend.operations->step(backend.context, 5U, error, sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "first post-checkpoint trajectory");
    size_t expected_size = 0U;
    uint8_t* const expected = copy_snapshot(&backend, &expected_size, "expected command replay");

    if(checkpoint != NULL)
    {
        check_status(
            backend.operations->restore_snapshot(
                backend.context,
                checkpoint,
                checkpoint_size,
                error,
                sizeof(error)),
            REACHY_SIM_STATUS_OK,
            "restore command checkpoint");
        check_status(
            backend.operations->submit_commands(
                backend.context,
                &second,
                command_batch_size(second.header.command_count),
                error,
                sizeof(error)),
            REACHY_SIM_STATUS_OK,
            "replay second command after restore");
        check_status(
            backend.operations->step(backend.context, 5U, error, sizeof(error)),
            REACHY_SIM_STATUS_OK,
            "replayed post-checkpoint trajectory");
        size_t replay_size = 0U;
        uint8_t* const replay = copy_snapshot(&backend, &replay_size, "actual command replay");
        check_true(expected != NULL && replay != NULL, "command replay snapshots exist");
        if(expected != NULL && replay != NULL)
        {
            check_true(expected_size == replay_size, "command replay size");
            check_true(
                expected_size == replay_size &&
                    memcmp(expected, replay, expected_size) == 0,
                "command snapshot replay is byte-identical");
        }
        free(replay);
    }

    free(expected);
    free(checkpoint);
    backend.operations->destroy(backend.context);
}

static void test_snapshot_transaction_and_config_identity(void)
{
    ReachySimBackendInstance source = create_backend();
    size_t source_size = 0U;
    uint8_t* const source_snapshot = copy_snapshot(
        &source,
        &source_size,
        "source snapshot");

    ReachySimConfig different_config = default_config();
    different_config.timestep_seconds = 0.004;
    ReachySimBackendInstance different = create_backend_with_config(
        &different_config,
        "create different-timestep backend");
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    if(source_snapshot != NULL)
    {
        check_status(
            different.operations->restore_snapshot(
                different.context,
                source_snapshot,
                source_size,
                error,
                sizeof(error)),
            REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
            "snapshot timestep identity rejection");
    }

    size_t before_size = 0U;
    uint8_t* const before = copy_snapshot(
        &source,
        &before_size,
        "pre-transaction snapshot");
    if(before != NULL)
    {
        uint8_t* const incompatible = malloc(before_size);
        check_true(incompatible != NULL, "transaction test snapshot allocation");
        if(incompatible != NULL)
        {
            memcpy(incompatible, before, before_size);
            ReachySimSnapshotHeader header = {0};
            memcpy(&header, incompatible, sizeof(header));
            header.simulation_time += 1.0;
            memcpy(incompatible, &header, sizeof(header));
            check_status(
                source.operations->restore_snapshot(
                    source.context,
                    incompatible,
                    before_size,
                    error,
                    sizeof(error)),
                REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
                "transactional restore rejects mismatched time");
            size_t after_size = 0U;
            uint8_t* const after = copy_snapshot(
                &source,
                &after_size,
                "post-transaction snapshot");
            check_true(after != NULL, "post-transaction snapshot exists");
            if(after != NULL)
            {
                check_true(after_size == before_size, "transaction snapshot size unchanged");
                check_true(
                    after_size == before_size &&
                        memcmp(after, before, before_size) == 0,
                    "failed restore leaves live state byte-identical");
            }
            free(after);
            free(incompatible);
        }
    }

    free(before);
    free(source_snapshot);
    different.operations->destroy(different.context);
    source.operations->destroy(source.context);
}

int main(void)
{
    test_model_loading();
    test_state_reset_and_capabilities();
    test_commands_and_warnings();
    test_wrench_and_snapshot_replay();
    test_command_snapshot_replay();
    test_snapshot_transaction_and_config_identity();

    if(g_failures != 0)
    {
        (void)fprintf(stderr, "%d production MuJoCo backend test(s) failed\n", g_failures);
        return 1;
    }
    (void)printf("production MuJoCo backend contract passed\n");
    return 0;
}
