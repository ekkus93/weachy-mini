static void test_dynamics_state_payload(void)
{
    ReachySimBackendInstance backend = create_backend();
    const ReachySimStateRequest request = {
        REACHY_SIM_STATE_REQUEST_MAGIC,
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateRequest),
        REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION,
        0U};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    uint8_t query[sizeof(request)] = {0};
    memcpy(query, &request, sizeof(request));
    size_t required_size = 0U;
    check_status(
        backend.operations->copy_state(
            backend.context,
            query,
            sizeof(query),
            &required_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "dynamics state size query");
    check_true(
        required_size > sizeof(ReachySimStateHeader) +
            sizeof(ReachySimDynamicsStatePayloadHeader),
        "dynamics state has a variable payload");

    uint8_t* const bytes = calloc(required_size, 1U);
    check_true(bytes != NULL, "dynamics state allocation");
    if(bytes == NULL)
    {
        backend.operations->destroy(backend.context);
        return;
    }
    memcpy(bytes, &request, sizeof(request));
    size_t actual_size = 0U;
    check_status(
        backend.operations->copy_state(
            backend.context,
            bytes,
            required_size,
            &actual_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "dynamics state copy");
    check_true(actual_size == required_size, "dynamics state size is stable");

    ReachySimStateHeader legacy = {0};
    ReachySimDynamicsStatePayloadHeader payload = {0};
    memcpy(&legacy, bytes, sizeof(legacy));
    memcpy(&payload, bytes + sizeof(legacy), sizeof(payload));
    const uint64_t qpos_offset =
        sizeof(ReachySimStateHeader) +
        sizeof(ReachySimDynamicsStatePayloadHeader);
    const uint64_t qvel_offset = qpos_offset +
        (uint64_t)payload.qpos_count * sizeof(double);
    const uint64_t actuator_offset = qvel_offset +
        (uint64_t)payload.qvel_count * sizeof(double);
    const uint64_t body_offset = actuator_offset +
        (uint64_t)payload.actuator_observation_count *
            sizeof(ReachySimActuatorObservation);
    const uint64_t contact_offset = body_offset +
        (uint64_t)payload.body_pose_count * sizeof(ReachySimBodyPose);
    const uint64_t hard_stop_offset = contact_offset +
        (uint64_t)payload.contact_observation_count *
            sizeof(ReachySimContactObservation);
    const uint64_t total_size = hard_stop_offset +
        (uint64_t)payload.hard_stop_observation_count *
            sizeof(ReachySimHardStopObservation);

    check_true(
        payload.state_format_version == REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION,
        "dynamics state format version");
    check_true(
        payload.struct_size == sizeof(ReachySimDynamicsStatePayloadHeader),
        "dynamics state header size");
    check_true(payload.total_size == required_size, "dynamics total size");
    check_true(payload.sequence == legacy.sequence, "dynamics sequence consistency");
    check_true(payload.qpos_offset == qpos_offset, "dynamics qpos offset");
    check_true(payload.qvel_offset == qvel_offset, "dynamics qvel offset");
    check_true(
        payload.actuator_observation_offset == actuator_offset,
        "dynamics actuator offset");
    check_true(payload.body_pose_offset == body_offset, "dynamics body offset");
    check_true(
        payload.contact_observation_offset == contact_offset,
        "dynamics contact offset");
    check_true(
        payload.hard_stop_observation_offset == hard_stop_offset,
        "dynamics hard-stop offset");
    check_true(total_size == required_size, "dynamics contiguous layout");
    check_true(payload.contact_observation_count == 0U, "fake dynamics contact count");
    check_true(payload.hard_stop_observation_count == 0U, "fake dynamics hard-stop count");
    check_true(payload.contact_overload_count == 0U, "fake dynamics overload count");
    check_true(payload.hard_stop_event_count == 0U, "fake dynamics hard-stop event count");
    check_true(
        payload.maximum_contact_penetration_metres == 0.0,
        "fake dynamics contact penetration");
    check_true(payload.maximum_hard_stop_force == 0.0, "fake dynamics hard-stop force");

    free(bytes);
    backend.operations->destroy(backend.context);
}
