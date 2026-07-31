#include "reachy_sim.h"
#include "reachy_sim_state.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void fail(const char* message, const char* detail)
{
    (void)fprintf(
        stderr,
        "%s%s%s\n",
        message,
        detail != NULL ? ": " : "",
        detail != NULL ? detail : "");
    exit(1);
}

static ReachySimErrorInfo initialized_error(void)
{
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);
    return error;
}

static void fail_handle(ReachySimHandle handle, const char* operation)
{
    ReachySimErrorInfo error = initialized_error();
    if(reachy_sim_get_last_error(handle, &error) == REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(
            stderr,
            "%s failed: status=%s recoverability=%u message=%s\n",
            operation,
            reachy_sim_status_string(error.status),
            error.recoverability,
            error.message);
    }
    else
    {
        (void)fprintf(
            stderr,
            "%s failed without retrievable diagnostics\n",
            operation);
    }
    exit(1);
}

static uint8_t* read_file(const char* path, size_t* size)
{
    FILE* stream = fopen(path, "rb");
    if(stream == NULL)
    {
        fail("cannot open model", path);
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        (void)fclose(stream);
        fail("cannot seek model", path);
    }
    const long length = ftell(stream);
    if(length <= 0L || fseek(stream, 0L, SEEK_SET) != 0)
    {
        (void)fclose(stream);
        fail("invalid model size", path);
    }
    uint8_t* const bytes = malloc((size_t)length);
    if(bytes == NULL)
    {
        (void)fclose(stream);
        fail("model allocation failed", NULL);
    }
    if(fread(bytes, 1U, (size_t)length, stream) != (size_t)length)
    {
        free(bytes);
        (void)fclose(stream);
        fail("cannot read model", path);
    }
    if(fclose(stream) != 0)
    {
        free(bytes);
        fail("cannot close model", path);
    }
    *size = (size_t)length;
    return bytes;
}

static int range_is_inside(
    size_t total_size,
    uint64_t offset,
    uint32_t count,
    size_t element_size)
{
    if(offset > SIZE_MAX || count > SIZE_MAX / element_size)
    {
        return 0;
    }
    const size_t start = (size_t)offset;
    const size_t bytes = (size_t)count * element_size;
    return start <= total_size && bytes <= total_size - start;
}

static uint8_t* copy_dynamics_state(
    ReachySimHandle handle,
    size_t* byte_count,
    ReachySimStateHeader* legacy,
    ReachySimDynamicsStatePayloadHeader* payload)
{
    const ReachySimStateRequest request = {
        REACHY_SIM_STATE_REQUEST_MAGIC,
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateRequest),
        REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION,
        0U};
    uint8_t query[sizeof(request)] = {0};
    memcpy(query, &request, sizeof(request));
    *byte_count = 0U;
    int32_t status = reachy_sim_copy_state(
        handle,
        query,
        sizeof(query),
        byte_count);
    if(status != REACHY_SIM_STATUS_BUFFER_TOO_SMALL)
    {
        fail_handle(handle, "dynamics state size query");
    }
    if(*byte_count < sizeof(*legacy) + sizeof(*payload))
    {
        fail("dynamics state size is too small", NULL);
    }

    uint8_t* const bytes = calloc(*byte_count, 1U);
    if(bytes == NULL)
    {
        fail("dynamics state allocation failed", NULL);
    }
    memcpy(bytes, &request, sizeof(request));
    size_t actual_size = 0U;
    status = reachy_sim_copy_state(
        handle,
        bytes,
        *byte_count,
        &actual_size);
    if(status != REACHY_SIM_STATUS_OK || actual_size != *byte_count)
    {
        free(bytes);
        fail_handle(handle, "dynamics state copy");
    }
    memcpy(legacy, bytes, sizeof(*legacy));
    memcpy(payload, bytes + sizeof(*legacy), sizeof(*payload));
    return bytes;
}

static void verify_invalid_command_is_reported(ReachySimHandle handle)
{
    struct OneCommandBatch {
        ReachySimCommandBatchHeader header;
        ReachySimActuatorCommand command;
    } batch = {0};
    batch.header.abi_version = REACHY_SIM_ABI_VERSION;
    batch.header.struct_size = (uint32_t)sizeof(batch.header);
    batch.header.sequence = 1U;
    batch.header.command_count = 1U;
    batch.header.byte_count = (uint32_t)sizeof(batch);
    batch.command.abi_version = REACHY_SIM_ABI_VERSION;
    batch.command.struct_size = (uint32_t)sizeof(batch.command);
    batch.command.actuator_id = 0U;
    batch.command.reserved = 0U;
    batch.command.control_value = 100.0;

    const int32_t status = reachy_sim_submit_commands(
        handle,
        &batch,
        sizeof(batch));
    if(status != REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR)
    {
        fail_handle(handle, "out-of-range command was not reported");
    }
}

int main(int argc, char** argv)
{
    if(argc != 3)
    {
        fail("usage", "reachy_sim_dynamics_state_runner MODEL.mjb EXPECT_CONTACT_0_OR_1");
    }
    const int expect_contact = atoi(argv[2]);
    if(expect_contact != 0 && expect_contact != 1)
    {
        fail("EXPECT_CONTACT must be 0 or 1", argv[2]);
    }

    size_t model_size = 0U;
    uint8_t* const model_bytes = read_file(argv[1], &model_size);
    ReachySimConfig config = reachy_sim_default_config();
    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_MJB;
    config.max_command_count = 9U;
    ReachySimHandle handle = REACHY_SIM_INVALID_HANDLE;
    ReachySimErrorInfo create_error = initialized_error();
    const int32_t create_status = reachy_sim_create(
        model_bytes,
        model_size,
        &config,
        &handle,
        &create_error);
    free(model_bytes);
    if(create_status != REACHY_SIM_STATUS_OK ||
       handle == REACHY_SIM_INVALID_HANDLE)
    {
        fail("simulation create failed", create_error.message);
    }

    verify_invalid_command_is_reported(handle);
    if(reachy_sim_step(handle, 1U) != REACHY_SIM_STATUS_OK)
    {
        fail_handle(handle, "simulation step");
    }

    ReachySimStateHeader legacy = {0};
    ReachySimDynamicsStatePayloadHeader payload = {0};
    size_t state_size = 0U;
    uint8_t* const state = copy_dynamics_state(
        handle,
        &state_size,
        &legacy,
        &payload);
    if(legacy.abi_version != REACHY_SIM_ABI_VERSION ||
       legacy.struct_size != sizeof(legacy) ||
       payload.state_format_version != REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION ||
       payload.struct_size != sizeof(payload) ||
       payload.total_size != state_size ||
       payload.sequence != legacy.sequence ||
       payload.simulation_time != legacy.simulation_time ||
       payload.reserved != 0U)
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("dynamics state header is invalid", NULL);
    }
    if(payload.hard_stop_observation_count != 9U)
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("dynamics state did not expose nine hard stops", NULL);
    }
    if(!range_is_inside(
           state_size,
           payload.contact_observation_offset,
           payload.contact_observation_count,
           sizeof(ReachySimContactObservation)) ||
       !range_is_inside(
           state_size,
           payload.hard_stop_observation_offset,
           payload.hard_stop_observation_count,
           sizeof(ReachySimHardStopObservation)))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("dynamics observation range is invalid", NULL);
    }
    if((expect_contact != 0 && payload.contact_observation_count == 0U) ||
       (expect_contact == 0 && payload.contact_observation_count != 0U))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("dynamics contact count differs from expectation", NULL);
    }
    if(expect_contact != 0 &&
       (payload.maximum_contact_normal_force_newtons <= 0.0 ||
        payload.maximum_contact_impulse_newton_seconds <= 0.0 ||
        payload.contact_overload_count == 0U ||
        (legacy.health_flags & REACHY_SIM_HEALTH_FLAG_CONTACT_OVERLOAD) == 0U))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("contact force, impulse, or overload was not exposed", NULL);
    }
    if(expect_contact == 0 &&
       (payload.contact_overload_count != 0U ||
        (legacy.health_flags & REACHY_SIM_HEALTH_FLAG_CONTACT_OVERLOAD) != 0U))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("neutral state reported a contact overload", NULL);
    }
    if(!isfinite(payload.maximum_contact_penetration_metres) ||
       !isfinite(payload.maximum_contact_normal_force_newtons) ||
       !isfinite(payload.maximum_contact_tangent_force_newtons) ||
       !isfinite(payload.maximum_contact_impulse_newton_seconds) ||
       !isfinite(payload.maximum_hard_stop_force))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("dynamics metrics are non-finite", NULL);
    }

    uint32_t classified_contact_count = 0U;
    uint32_t overload_contact_count = 0U;
    for(uint32_t index = 0U; index < payload.contact_observation_count; ++index)
    {
        ReachySimContactObservation observation = {0};
        memcpy(
            &observation,
            state + payload.contact_observation_offset +
                (size_t)index * sizeof(observation),
            sizeof(observation));
        if(observation.contact_id != index ||
           observation.geom1_id == REACHY_SIM_INVALID_OBJECT_ID ||
           observation.geom2_id == REACHY_SIM_INVALID_OBJECT_ID ||
           observation.body1_id == REACHY_SIM_INVALID_OBJECT_ID ||
           observation.body2_id == REACHY_SIM_INVALID_OBJECT_ID ||
           !isfinite(observation.penetration_metres) ||
           !isfinite(observation.normal_force_newtons) ||
           !isfinite(observation.tangent_force_newtons) ||
           !isfinite(observation.impulse_newton_seconds))
        {
            free(state);
            (void)reachy_sim_destroy(handle);
            fail("contact observation is invalid", NULL);
        }
        if((observation.flags &
            (REACHY_SIM_CONTACT_FLAG_INTERNAL | REACHY_SIM_CONTACT_FLAG_EXTERNAL)) != 0U)
        {
            ++classified_contact_count;
        }
        if((observation.flags & REACHY_SIM_CONTACT_FLAG_OVERLOAD) != 0U)
        {
            ++overload_contact_count;
        }
    }
    if(expect_contact != 0 &&
       (classified_contact_count == 0U || overload_contact_count == 0U))
    {
        free(state);
        (void)reachy_sim_destroy(handle);
        fail("contact classification or overload flag was not exposed", NULL);
    }

    for(uint32_t index = 0U; index < payload.hard_stop_observation_count; ++index)
    {
        ReachySimHardStopObservation observation = {0};
        memcpy(
            &observation,
            state + payload.hard_stop_observation_offset +
                (size_t)index * sizeof(observation),
            sizeof(observation));
        if(observation.joint_id == REACHY_SIM_INVALID_OBJECT_ID ||
           observation.reserved != 0U ||
           !isfinite(observation.position) ||
           !isfinite(observation.lower_limit) ||
           !isfinite(observation.upper_limit) ||
           !isfinite(observation.signed_distance_to_limit) ||
           !isfinite(observation.limit_force) ||
           !isfinite(observation.impulse) ||
           !(observation.lower_limit < observation.upper_limit))
        {
            free(state);
            (void)reachy_sim_destroy(handle);
            fail("hard-stop observation is invalid", NULL);
        }
    }

    (void)printf(
        "{\"status\":\"ok\",\"contacts\":%u,\"contact_overloads\":%u,"
        "\"hard_stops\":%u,\"hard_stop_events\":%u,"
        "\"health_flags\":%u,\"maximum_normal_force_newtons\":%.17g,"
        "\"maximum_impulse_newton_seconds\":%.17g}\n",
        payload.contact_observation_count,
        payload.contact_overload_count,
        payload.hard_stop_observation_count,
        payload.hard_stop_event_count,
        legacy.health_flags,
        payload.maximum_contact_normal_force_newtons,
        payload.maximum_contact_impulse_newton_seconds);
    free(state);
    if(reachy_sim_destroy(handle) != REACHY_SIM_STATUS_OK)
    {
        fail("simulation destroy failed", NULL);
    }
    return 0;
}
