
    config.flags = UINT32_C(1) << 31;
    check_status(
        reachy_sim_backend_create(
            MJB, sizeof(MJB), &config, &backend, error, sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "unknown model flag");

    config = default_config();
    config.timestep_seconds = NAN;
    check_status(
        reachy_sim_backend_create(
            MJB, sizeof(MJB), &config, &backend, error, sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "non-finite timestep rejection");
}

static void test_state_reset_and_capabilities(void)
{
    ReachySimBackendInstance backend = create_backend();
    check_true(
        (backend.capability_flags & REACHY_SIM_CAPABILITY_COMMANDS) != 0U,
        "commands capability");
    check_true(
        (backend.capability_flags & REACHY_SIM_CAPABILITY_WRENCH) != 0U,
        "wrench capability");
    check_true(
        (backend.capability_flags & REACHY_SIM_CAPABILITY_SNAPSHOT) != 0U,
        "snapshot capability");

    size_t required_size = 0U;
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    check_status(
        backend.operations->copy_state(
            backend.context,
            NULL,
            0U,
            &required_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "state size query");
    check_true(required_size == sizeof(ReachySimStateHeader), "header-only ABI v2 state size");

    ReachySimStateHeader state = copy_state(&backend, "initial state");
    check_true(state.sequence == 0U, "initial sequence");
    check_true(state.body_count == 3U, "non-world body count");
    check_true(state.joint_count == 4U, "joint count");
    check_true(state.actuator_count == 3U, "actuator count");
    check_true(state.health_flags == 0U, "neutral health flags");

    check_status(
        backend.operations->reset(
            backend.context,
            (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "sleep keyframe reset");
    state = copy_state(&backend, "sleep state");
    check_true(
        (state.health_flags & REACHY_SIM_HEALTH_FLAG_SLEEPING) != 0U,
        "sleeping health flag");

    check_status(
        backend.operations->reset(
            backend.context,
            99U,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "unknown reset pose");
    backend.operations->destroy(backend.context);
}

static void test_commands_and_warnings(void)
{
    ReachySimBackendInstance backend = create_backend();
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    CommandBatchTwo batch = command_batch(
        1U,
        2U,
        actuator_command(0U, 0.5),
        actuator_command(1U, -1.5));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &batch,
            command_batch_size(batch.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "valid actuator batch");
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &batch,
            command_batch_size(batch.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
        "stale command sequence");

    batch = command_batch(
        2U,
        2U,
        actuator_command(0U, 0.25),
        actuator_command(0U, 0.5));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &batch,
            command_batch_size(batch.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
        "duplicate actuator rejection");

    batch = command_batch(
        2U,
        1U,
        actuator_command(0U, 1.5),
        actuator_command(0U, 0.0));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &batch,
            command_batch_size(batch.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
        "out-of-range actuator rejection");

    batch = command_batch(
        2U,
        1U,
        actuator_command(2U, 99.0),
        actuator_command(0U, 0.0));
    check_status(
        backend.operations->submit_commands(
            backend.context,
            &batch,
            command_batch_size(batch.header.command_count),
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "unlimited warning trigger actuator");
    check_status(
        backend.operations->step(
            backend.context,
            1U,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BACKEND_ERROR,
        "MuJoCo warning is surfaced");
    check_true(strstr(error, "warning 2") != NULL, "warning diagnostic includes category");
    const ReachySimStateHeader state = copy_state(&backend, "warning state");
    check_true(state.sequence == 1U, "warning step advances sequence visibly");
    check_true(
        (state.health_flags & REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING) != 0U,
        "warning health flag retained");
    size_t snapshot_size = 0U;
    check_status(
        backend.operations->copy_snapshot(
            backend.context,
            NULL,
            0U,
            &snapshot_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BACKEND_ERROR,
        "warning-faulted state cannot be snapshotted");
    backend.operations->destroy(backend.context);
}

static ReachySimWrenchCommand wrench_command(double duration)
{
    ReachySimWrenchCommand command = {0};
    command.abi_version = REACHY_SIM_ABI_VERSION;
    command.struct_size = (uint32_t)sizeof(command);
    command.body_id = 1U;
    command.force_newtons[0] = 2.0;
    command.torque_newton_metres[1] = -0.5;
    command.application_point_metres[2] = 0.2;
    command.duration_seconds = duration;
    return command;
}

static void test_wrench_and_snapshot_replay(void)
{
    ReachySimBackendInstance backend = create_backend();
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    size_t initial_size = 0U;
    uint8_t* const initial = copy_snapshot(&backend, &initial_size, "initial snapshot");
    check_true(initial != NULL, "initial snapshot exists");
    if(initial == NULL)
    {
        backend.operations->destroy(backend.context);
        return;
    }

    ReachySimWrenchCommand wrench = wrench_command(0.004);
    check_status(
        backend.operations->apply_wrench(
            backend.context,
            &wrench,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "finite-duration wrench");
    check_status(
        backend.operations->step(
            backend.context,
            2U,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "wrench trajectory");
    size_t expected_size = 0U;
    uint8_t* const expected = copy_snapshot(&backend, &expected_size, "wrench result snapshot");

    check_status(
        backend.operations->restore_snapshot(
            backend.context,
            initial,
            initial_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "restore initial snapshot");
    check_status(
        backend.operations->apply_wrench(
            backend.context,
            &wrench,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "reapply wrench");
    check_status(
        backend.operations->step(
