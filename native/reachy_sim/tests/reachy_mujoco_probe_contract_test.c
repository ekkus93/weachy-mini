#include "reachy_mujoco_probe.h"

#include <stdio.h>
#include <string.h>

static const char VALID_XML[] =
    "<mujoco model=\"fixture\"><option timestep=\"0.002\"/>"
    "<worldbody/><equality><connect name=\"loop\"/></equality></mujoco>";
static const char MALFORMED_XML[] =
    "<mujoco model=\"malformed\"><body name=\"missing-close\">";

static int test_valid_probe(void)
{
    ReachyMujocoProbeConfig config = reachy_mujoco_probe_default_config();
    config.step_count = 900000U;
    ReachyMujocoProbeReport report;
    char error[256];
    const ReachyMujocoProbeStatus status = reachy_mujoco_probe_run_xml(
        VALID_XML,
        sizeof(VALID_XML),
        &config,
        &report,
        error,
        sizeof(error));
    if(status != REACHY_MUJOCO_PROBE_OK)
    {
        (void)fprintf(stderr, "valid probe failed: %s\n", error);
        return 1;
    }
    if(report.completed_steps != config.step_count || report.simulated_seconds < 1799.9 ||
       report.simulated_seconds > 1800.1 || report.maximum_constraint_residual > 0.001)
    {
        (void)fprintf(stderr, "valid probe report was outside expected bounds\n");
        return 1;
    }
    if(report.body_count != 3U || report.joint_count != 2U ||
       report.actuator_count != 0U || report.equality_count != 1U ||
       report.site_count != 2U || report.camera_count != 0U ||
       report.position_count != 2U || report.velocity_count != 2U)
    {
        (void)fprintf(stderr, "valid probe compiled counts were incorrect\n");
        return 1;
    }
    return 0;
}

static int test_malformed_probe(void)
{
    ReachyMujocoProbeConfig config = reachy_mujoco_probe_default_config();
    config.step_count = 1U;
    ReachyMujocoProbeReport report;
    char error[256];
    const ReachyMujocoProbeStatus status = reachy_mujoco_probe_run_xml(
        MALFORMED_XML,
        sizeof(MALFORMED_XML),
        &config,
        &report,
        error,
        sizeof(error));
    if(status != REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED || error[0] == '\0')
    {
        (void)fprintf(stderr, "malformed model did not return a structured error\n");
        return 1;
    }
    return 0;
}

static int test_invalid_arguments(void)
{
    ReachyMujocoProbeConfig config = reachy_mujoco_probe_default_config();
    ReachyMujocoProbeReport report;
    char error[256];
    const ReachyMujocoProbeStatus status = reachy_mujoco_probe_run_xml(
        NULL,
        0U,
        &config,
        &report,
        error,
        sizeof(error));
    if(status != REACHY_MUJOCO_PROBE_INVALID_ARGUMENT ||
       strcmp(reachy_mujoco_probe_status_string(status), "invalid_argument") != 0)
    {
        (void)fprintf(stderr, "invalid arguments were not rejected\n");
        return 1;
    }
    return 0;
}

int main(void)
{
    if(test_valid_probe() != 0)
    {
        return 1;
    }
    if(test_malformed_probe() != 0)
    {
        return 1;
    }
    return test_invalid_arguments();
}
