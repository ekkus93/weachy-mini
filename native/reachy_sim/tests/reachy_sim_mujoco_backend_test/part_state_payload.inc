#include "reachy_sim_state.h"

static uint8_t* copy_authoritative_state(
    const ReachySimBackendInstance* backend,
    size_t* byte_count,
    ReachySimStateHeader* legacy,
    ReachySimStatePayloadHeader* payload)
{
    const ReachySimStateRequest request = {
        REACHY_SIM_STATE_REQUEST_MAGIC,
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateRequest),
        REACHY_SIM_STATE_FORMAT_VERSION,
        0U};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    uint8_t query[sizeof(request)] = {0};
    memcpy(query, &request, sizeof(request));
    *byte_count = 0U;
    check_status(
        backend->operations->copy_state(
            backend->context,
            query,
            sizeof(query),
            byte_count,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "authoritative state size query");
    check_true(
        *byte_count > sizeof(ReachySimStateHeader) +
            sizeof(ReachySimStatePayloadHeader),
        "authoritative state has a variable payload");

    uint8_t* const bytes = calloc(*byte_count, 1U);
    check_true(bytes != NULL, "authoritative state allocation");
    if(bytes == NULL)
    {
        return NULL;
    }
    memcpy(bytes, &request, sizeof(request));
    size_t actual_size = 0U;
    check_status(
        backend->operations->copy_state(
            backend->context,
            bytes,
            *byte_count,
            &actual_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "authoritative state copy");
    check_true(actual_size == *byte_count, "authoritative state size is stable");
    memcpy(legacy, bytes, sizeof(*legacy));
    memcpy(payload, bytes + sizeof(*legacy), sizeof(*payload));
    return bytes;
}

static void validate_authoritative_payload_layout(
    const uint8_t* bytes,
    size_t byte_count,
    const ReachySimStateHeader* legacy,
    const ReachySimStatePayloadHeader* payload)
{
    const uint64_t qpos_offset =
        sizeof(ReachySimStateHeader) + sizeof(ReachySimStatePayloadHeader);
    const uint64_t qvel_offset = qpos_offset +
        (uint64_t)payload->qpos_count * sizeof(double);
    const uint64_t actuator_offset = qvel_offset +
        (uint64_t)payload->qvel_count * sizeof(double);
    const uint64_t body_offset = actuator_offset +
        (uint64_t)payload->actuator_observation_count *
            sizeof(ReachySimActuatorObservation);
    const uint64_t total_size = body_offset +
        (uint64_t)payload->body_pose_count * sizeof(ReachySimBodyPose);

    check_true(legacy->abi_version == REACHY_SIM_ABI_VERSION, "state ABI prefix");
    check_true(
        legacy->struct_size == sizeof(ReachySimStateHeader),
        "state prefix size");
    check_true(
        payload->state_format_version == REACHY_SIM_STATE_FORMAT_VERSION,
        "state format version");
    check_true(
        payload->struct_size == sizeof(ReachySimStatePayloadHeader),
        "state payload header size");
    check_true(payload->total_size == byte_count, "state total size");
    check_true(payload->model_hash != 0U, "state model identity");
    check_true(payload->sequence == legacy->sequence, "state sequence consistency");
    check_true(
        payload->simulation_time == legacy->simulation_time,
        "state time consistency");
    check_true(payload->body_pose_count == legacy->body_count, "state body count");
    check_true(
        payload->actuator_observation_count == legacy->actuator_count,
        "state actuator count");
    check_true(payload->qpos_offset == qpos_offset, "state qpos offset");
    check_true(payload->qvel_offset == qvel_offset, "state qvel offset");
    check_true(
        payload->actuator_observation_offset == actuator_offset,
        "state actuator offset");
    check_true(payload->body_pose_offset == body_offset, "state body offset");
    check_true(total_size == byte_count, "state canonical contiguous layout");
    check_true(
        payload->equality_constraint_count <= payload->constraint_count,
        "state equality constraint count");
    check_true(
        isfinite(payload->maximum_constraint_residual) &&
            payload->maximum_constraint_residual >= 0.0,
        "state constraint residual");
    check_true(
        isfinite(payload->maximum_equality_constraint_residual) &&
            payload->maximum_equality_constraint_residual >= 0.0,
        "state equality residual");

    for(uint32_t index = 0U; index < payload->actuator_observation_count; ++index)
    {
        ReachySimActuatorObservation observation = {0};
        memcpy(
            &observation,
            bytes + payload->actuator_observation_offset +
                (size_t)index * sizeof(observation),
            sizeof(observation));
        check_true(observation.actuator_id == index, "canonical actuator state order");
        check_true(observation.reserved == 0U, "actuator state reserved field");
        check_true(isfinite(observation.control_value), "finite actuator control");
        check_true(isfinite(observation.actuator_force), "finite actuator force");
        check_true(isfinite(observation.length), "finite actuator length");
        check_true(isfinite(observation.velocity), "finite actuator velocity");
    }

    for(uint32_t index = 0U; index < payload->body_pose_count; ++index)
    {
        ReachySimBodyPose pose = {0};
        memcpy(
            &pose,
            bytes + payload->body_pose_offset + (size_t)index * sizeof(pose),
            sizeof(pose));
        check_true(pose.body_id == index + 1U, "canonical non-world body order");
        check_true(pose.reserved == 0U, "body state reserved field");
        double norm_squared = 0.0;
        for(size_t component = 0U; component < 3U; ++component)
        {
            check_true(isfinite(pose.position_metres[component]), "finite body position");
        }
        for(size_t component = 0U; component < 4U; ++component)
        {
            check_true(isfinite(pose.quaternion_wxyz[component]), "finite body quaternion");
            norm_squared += pose.quaternion_wxyz[component] *
                pose.quaternion_wxyz[component];
        }
        check_true(norm_squared > 0.0, "nonzero body quaternion");
    }
}

static void test_authoritative_state_payload(void)
{
    ReachySimBackendInstance backend = create_backend();
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    ReachySimStateRequest unsupported_request = {
        REACHY_SIM_STATE_REQUEST_MAGIC,
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateRequest),
        REACHY_SIM_STATE_FORMAT_VERSION + 1U,
        0U};
    size_t unsupported_size = 0U;
    check_status(
        backend.operations->copy_state(
            backend.context,
            &unsupported_request,
            sizeof(unsupported_request),
            &unsupported_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_UNSUPPORTED,
        "unsupported authoritative state format rejection");
    ReachySimStateHeader initial = {0};
    ReachySimStatePayloadHeader initial_payload = {0};
    size_t initial_size = 0U;
    uint8_t* initial_bytes = copy_authoritative_state(
        &backend,
        &initial_size,
        &initial,
        &initial_payload);
    check_true(initial_payload.continuity_id == 1U, "initial continuity epoch");
    if(initial_bytes != NULL)
    {
        validate_authoritative_payload_layout(
            initial_bytes,
            initial_size,
            &initial,
            &initial_payload);
    }

    memset(error, 0, sizeof(error));
    check_status(
        backend.operations->step(backend.context, 2U, error, sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "step before authoritative state recapture");
    ReachySimStateHeader stepped = {0};
    ReachySimStatePayloadHeader stepped_payload = {0};
    size_t stepped_size = 0U;
    uint8_t* stepped_bytes = copy_authoritative_state(
        &backend,
        &stepped_size,
        &stepped,
        &stepped_payload);
    check_true(stepped.sequence == 2U, "authoritative state stepped sequence");
    check_true(
        stepped_payload.continuity_id == initial_payload.continuity_id,
        "ordinary steps preserve continuity epoch");

    size_t snapshot_size = 0U;
    uint8_t* snapshot = copy_snapshot(
        &backend,
        &snapshot_size,
        "authoritative state continuity snapshot");
    if(snapshot != NULL)
    {
        check_status(
            backend.operations->step(backend.context, 1U, error, sizeof(error)),
            REACHY_SIM_STATUS_OK,
            "step after authoritative state snapshot");
        check_status(
            backend.operations->restore_snapshot(
                backend.context,
                snapshot,
                snapshot_size,
                error,
                sizeof(error)),
            REACHY_SIM_STATUS_OK,
            "restore authoritative state snapshot");
        ReachySimStateHeader restored = {0};
        ReachySimStatePayloadHeader restored_payload = {0};
        size_t restored_size = 0U;
        uint8_t* restored_bytes = copy_authoritative_state(
            &backend,
            &restored_size,
            &restored,
            &restored_payload);
        check_true(restored.sequence == stepped.sequence, "restored state sequence");
        check_true(
            restored_payload.continuity_id == stepped_payload.continuity_id + 1U,
            "snapshot restore advances continuity epoch");
        free(restored_bytes);
    }

    check_status(
        backend.operations->reset(
            backend.context,
            (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "authoritative state neutral reset");
    ReachySimStateHeader reset = {0};
    ReachySimStatePayloadHeader reset_payload = {0};
    size_t reset_size = 0U;
    uint8_t* reset_bytes = copy_authoritative_state(
        &backend,
        &reset_size,
        &reset,
        &reset_payload);
    check_true(reset.sequence == 0U, "authoritative reset sequence");
    check_true(
        reset_payload.continuity_id > stepped_payload.continuity_id,
        "reset advances continuity epoch");

    ReachySimStateHeader legacy = {0};
    size_t legacy_size = 0U;
    check_status(
        backend.operations->copy_state(
            backend.context,
            &legacy,
            sizeof(legacy),
            &legacy_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "legacy state header remains available");
    check_true(legacy_size == sizeof(legacy), "legacy state size remains compatible");

    free(reset_bytes);
    free(snapshot);
    free(stepped_bytes);
    free(initial_bytes);
    backend.operations->destroy(backend.context);
}
