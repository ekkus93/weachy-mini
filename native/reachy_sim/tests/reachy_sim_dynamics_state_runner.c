#include "reachy_sim.h"
#include "reachy_sim_state.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void fail(const char* message, const char* detail)
{
    fprintf(stderr, "%s%s%s\n", message, detail != NULL ? ": " : "", detail != NULL ? detail : "");
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
        fclose(stream);
        fail("cannot seek model", path);
    }
    const long length = ftell(stream);
    if(length <= 0L || fseek(stream, 0L, SEEK_SET) != 0)
    {
        fclose(stream);
        fail("invalid model size", path);
    }
    uint8_t* const bytes = malloc((size_t)length);
    if(bytes == NULL)
    {
        fclose(stream);
        fail("model allocation failed", NULL);
    }
    if(fread(bytes, 1U, (size_t)length, stream) != (size_t)length)
    {
        free(bytes);
        fclose(stream);
        fail("cannot read model", path);
    }
    fclose(stream);
    *size = (size_t)length;
    return bytes;
}

static uint8_t* copy_dynamics_state(
    ReachySimHandle* handle,
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
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    uint8_t query[sizeof(request)] = {0};
    memcpy(query, &request, sizeof(request));
    *byte_count = 0U;
    ReachySimStatus status = reachy_sim_get_state(
        handle,
        query,
        sizeof(query),
        byte_count,
        error,
        sizeof(error));
    if(status != REACHY_SIM_STATUS_BUFFER_TOO_SMALL)
    {
        fail("dynamics state size query failed", error);
    }
    uint8_t* const bytes = calloc(*byte_count, 1U);
    if(bytes == NULL)
    {
        fail("dynamics state allocation failed", NULL);
    }
    memcpy(bytes, &request, sizeof(request));
    size_t actual_size = 0U;
    status = reachy_sim_get_state(
        handle,
        bytes,
        *byte_count,
        &actual_size,
        error,
        sizeof(error));
    if(status != REACHY_SIM_STATUS_OK || actual_size != *byte_count)
    {
        free(bytes);
        fail("dynamics state copy failed", error);
    }
    memcpy(legacy, bytes, sizeof(*legacy));
    memcpy(payload, bytes + sizeof(*legacy), sizeof(*payload));
    return bytes;
}

static void verify_invalid_command_is_reported(ReachySimHandle* handle)
{
    struct OneCommandBatch {
        ReachySimCommandBatchHeader header;
        ReachySimActuatorCommand command;
    } batch = {
        {
            REACHY_SIM_ABI_VERSION,
            (uint32_t)sizeof(ReachySimCommandBatchHeader),
            1U,
            1U,
            0U
        },
        {
            0U,
            (uint32_t)REACHY_SIM_ACTUATOR_MODE_POSITION,
            100.0,
            0.0
        }};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status = reachy_sim_submit_commands(
        handle,
        &batch,
        sizeof(batch),
        error,
        sizeof(error));
    if(status != REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR)
    {
        fail("out-of-range command was not reported", error);
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
    const ReachySimConfig config = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimConfig),
        0.002,
        9U,
        (uint32_t)REACHY_SIM_CONFIG_FLAG_MODEL_MJB};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    ReachySimHandle* handle = NULL;
    ReachySimStatus status = reachy_sim_create(
        model_bytes,
        model_size,
        &config,
        &handle,
        error,
        sizeof(error));
    free(model_bytes);
    if(status != REACHY_SIM_STATUS_OK || handle == NULL)
    {
        fail("simulation create failed", error);
    }

    verify_invalid_command_is_reported(handle);
    status = reachy_sim_step(handle, 1U, error, sizeof(error));
    if(status != REACHY_SIM_STATUS_OK)
    {
        reachy_sim_destroy(handle);
        fail("simulation step failed", error);
    }

    ReachySimStateHeader legacy = {0};
    ReachySimDynamicsStatePayloadHeader payload = {0};
    size_t state_size = 0U;
    uint8_t* const state = copy_dynamics_state(
        handle,
        &state_size,
        &legacy,
        &payload);
    if(payload.state_format_version != REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION ||
       payload.struct_size != sizeof(payload) ||
       payload.total_size != state_size)
    {
        free(state);
        reachy_sim_destroy(handle);
        fail("dynamics state header is invalid", NULL);
    }
    if(payload.hard_stop_observation_count != 9U)
    {
        free(state);
        reachy_sim_destroy(handle);
        fail("dynamics state did not expose nine hard stops", NULL);
    }
    if((expect_contact != 0 && payload.contact_observation_count == 0U) ||
       (expect_contact == 0 && payload.contact_observation_count != 0U))
    {
        free(state);
        reachy_sim_destroy(handle);
        fail("dynamics contact count differs from expectation", NULL);
    }
    if(expect_contact != 0 &&
       (payload.maximum_contact_normal_force_newtons <= 0.0 ||
        payload.maximum_contact_impulse_newton_seconds <= 0.0))
    {
        free(state);
        reachy_sim_destroy(handle);
        fail("dynamics contact force or impulse was not exposed", NULL);
    }
    if(!isfinite(payload.maximum_contact_penetration_metres) ||
       !isfinite(payload.maximum_hard_stop_force))
    {
        free(state);
        reachy_sim_destroy(handle);
        fail("dynamics metrics are non-finite", NULL);
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
           !isfinite(observation.position) ||
           !isfinite(observation.lower_limit) ||
           !isfinite(observation.upper_limit) ||
           !(observation.lower_limit < observation.upper_limit))
        {
            free(state);
            reachy_sim_destroy(handle);
            fail("hard-stop observation is invalid", NULL);
        }
    }

    printf(
        "{\"status\":\"ok\",\"contacts\":%u,\"hard_stops\":%u,"
        "\"maximum_normal_force_newtons\":%.17g,\"maximum_impulse_newton_seconds\":%.17g}\n",
        payload.contact_observation_count,
        payload.hard_stop_observation_count,
        payload.maximum_contact_normal_force_newtons,
        payload.maximum_contact_impulse_newton_seconds);
    free(state);
    reachy_sim_destroy(handle);
    return 0;
}
