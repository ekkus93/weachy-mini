#include "reachy_sim_backend.h"

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(ReachySimActuatorCommand) == 24U, "actuator command layout changed");

typedef struct CommandBatchTwo {
    ReachySimCommandBatchHeader header;
    ReachySimActuatorCommand commands[2];
} CommandBatchTwo;

static int g_failures = 0;

static void check_true(int condition, const char* message)
{
    if(condition == 0)
    {
        (void)fprintf(stderr, "FAIL: %s\n", message);
        ++g_failures;
    }
}

static void check_status(
    ReachySimStatus actual,
    ReachySimStatus expected,
    const char* message)
{
    if(actual != expected)
    {
        (void)fprintf(
            stderr,
            "FAIL: %s: expected %d, received %d\n",
            message,
            (int)expected,
            (int)actual);
        ++g_failures;
    }
}

static ReachySimConfig default_config(void)
{
    const ReachySimConfig config = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimConfig),
        0.002,
        8U,
        REACHY_SIM_CONFIG_FLAG_MODEL_MJB};
    return config;
}

static ReachySimBackendInstance create_backend_with_config(
    const ReachySimConfig* config,
    const char* message)
{
    static const uint8_t MODEL[] = {'M', 'J', 'B', 1U, 2U, 3U};
    ReachySimBackendInstance backend = {0};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    check_status(
        reachy_sim_backend_create(
            MODEL,
            sizeof(MODEL),
            config,
            &backend,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        message);
    check_true(backend.context != NULL, "backend context is present");
    check_true(backend.operations != NULL, "backend operations are present");
    return backend;
}

static ReachySimBackendInstance create_backend(void)
{
    const ReachySimConfig config = default_config();
    return create_backend_with_config(&config, "create MJB backend");
}

static ReachySimStateHeader copy_state(
    const ReachySimBackendInstance* backend,
    const char* message)
{
    ReachySimStateHeader state = {0};
    size_t required_size = 0U;
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    check_status(
        backend->operations->copy_state(
            backend->context,
            &state,
            sizeof(state),
            &required_size,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        message);
    check_true(required_size == sizeof(state), "state required size");
    return state;
}

static uint8_t* copy_snapshot(
    const ReachySimBackendInstance* backend,
    size_t* byte_count,
    const char* message)
{
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    *byte_count = 0U;
    check_status(
        backend->operations->copy_snapshot(
            backend->context,
            NULL,
            0U,
            byte_count,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "snapshot size query");
    check_true(
        *byte_count > sizeof(ReachySimSnapshotHeader),
        "snapshot has private MuJoCo payload");
    uint8_t* const bytes = calloc(*byte_count, 1U);
    check_true(bytes != NULL, "snapshot allocation");
    if(bytes == NULL)
    {
        return NULL;
    }
    check_status(
        backend->operations->copy_snapshot(
            backend->context,
            bytes,
            *byte_count,
            byte_count,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_OK,
        message);
    return bytes;
}

static ReachySimActuatorCommand actuator_command(uint32_t id, double value)
{
    const ReachySimActuatorCommand command = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimActuatorCommand),
        id,
        0U,
        value};
    return command;
}

static CommandBatchTwo command_batch(
    uint64_t sequence,
    uint32_t count,
    ReachySimActuatorCommand first,
    ReachySimActuatorCommand second)
{
    const CommandBatchTwo batch = {
        {
            REACHY_SIM_ABI_VERSION,
            (uint32_t)sizeof(ReachySimCommandBatchHeader),
            sequence,
            count,
            (uint32_t)(sizeof(ReachySimCommandBatchHeader) +
                (size_t)count * sizeof(ReachySimActuatorCommand))
        },
        {first, second}};
    return batch;
}

static size_t command_batch_size(uint32_t count)
{
    return sizeof(ReachySimCommandBatchHeader) +
        (size_t)count * sizeof(ReachySimActuatorCommand);
}

static void test_model_loading(void)
{
    static const uint8_t MJB[] = {'M', 'J', 'B', 0U};
    static const uint8_t BAD_MJB[] = {'B', 'A', 'D', 0U};
    static const uint8_t XML[] = "<mujoco model=\"backend_fixture\"></mujoco>";
    static const uint8_t EXTERNAL_XML[] =
        "<mujoco model=\"external_asset\"></mujoco>";
    ReachySimConfig config = default_config();
    ReachySimBackendInstance backend = {0};
    char error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};

    config.flags = 0U;
    check_status(
        reachy_sim_backend_create(
            MJB, sizeof(MJB), &config, &backend, error, sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "auto-detect MJB");
    backend.operations->destroy(backend.context);

    memset(&backend, 0, sizeof(backend));
    check_status(
        reachy_sim_backend_create(
            XML, sizeof(XML), &config, &backend, error, sizeof(error)),
        REACHY_SIM_STATUS_OK,
        "auto-detect self-contained XML");
    backend.operations->destroy(backend.context);

    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_XML;
    memset(&backend, 0, sizeof(backend));
    check_status(
        reachy_sim_backend_create(
            EXTERNAL_XML,
            sizeof(EXTERNAL_XML),
            &config,
            &backend,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BACKEND_ERROR,
        "external XML asset rejection");
    check_true(
        strstr(error, "resource not found") != NULL,
        "external XML failure preserves MuJoCo diagnostic");

    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_MJB;
    check_status(
        reachy_sim_backend_create(
            BAD_MJB,
            sizeof(BAD_MJB),
            &config,
            &backend,
            error,
            sizeof(error)),
        REACHY_SIM_STATUS_BACKEND_ERROR,
        "invalid MJB rejection");

    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_XML |
        REACHY_SIM_CONFIG_FLAG_MODEL_MJB;
    check_status(
        reachy_sim_backend_create(
            MJB, sizeof(MJB), &config, &backend, error, sizeof(error)),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "conflicting model flags");
