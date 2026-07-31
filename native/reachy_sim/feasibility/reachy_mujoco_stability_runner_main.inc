int main(int argc, char** argv)
{
    if(argc < 3 || argc > 4)
    {
        (void)fprintf(stderr, "usage: %s MODEL_XML CYCLES [PLATFORM_LABEL]\n", argv[0]);
        return 2;
    }

    uint32_t cycles = 0U;
    char error[1024] = {0};
    if(!parse_cycles(argv[2], &cycles))
    {
        print_failure("cycles must be a positive 32-bit integer");
        return 2;
    }
    const char* const platform = argc == 4 ? argv[3] : "native";

    uint64_t total_steps = 0U;
    if(!checked_total_steps(cycles, &total_steps))
    {
        print_failure("requested cycle count overflows the stability schedule");
        return 2;
    }

    char model_error[1024] = {0};
    mjModel* model = mj_loadXML(argv[1], NULL, model_error, (int)sizeof(model_error));
    if(model == NULL)
    {
        print_failure(model_error);
        return 1;
    }
    if(strcmp(mj_versionString(), REACHY_STABILITY_MUJOCO_VERSION) != 0)
    {
        (void)snprintf(
            error,
            sizeof(error),
            "MuJoCo version mismatch: expected %s, found %s",
            REACHY_STABILITY_MUJOCO_VERSION,
            mj_versionString());
        print_failure(error);
        mj_deleteModel(model);
        return 1;
    }
    if(fabs((double)model->opt.timestep - REACHY_STABILITY_TIMESTEP_SECONDS) > 1e-12)
    {
        print_failure("model timestep differs from the 500 Hz upstream baseline");
        mj_deleteModel(model);
        return 1;
    }
    if(!model_counts_match(model))
    {
        print_failure("compiled model counts differ from the stability profile");
        mj_deleteModel(model);
        return 1;
    }

    ReachyStabilityRun run;
    initialize_run(&run);
    if(!allocate_timings(&run, total_steps) ||
       !resolve_actuators(model, &run, error, sizeof(error)) ||
       !validate_phase_ranges(model, &run, error, sizeof(error)))
    {
        print_failure(error[0] == '\0' ? "cannot initialize stability run" : error);
        free(run.step_timings);
        mj_deleteModel(model);
        return 1;
    }

    mjData* data = mj_makeData(model);
    if(data == NULL)
    {
        print_failure("cannot allocate MuJoCo data");
        free(run.step_timings);
        mj_deleteModel(model);
        return 1;
    }
    mj_forward(model, data);
    if(!finite_state(model, data))
    {
        print_failure("initial MuJoCo state is non-finite");
        mj_deleteData(data);
        free(run.step_timings);
        mj_deleteModel(model);
        return 1;
    }

    double previous_targets[REACHY_STABILITY_ACTUATOR_COUNT] = {0.0};
    for(size_t index = 0U; index < REACHY_STABILITY_ACTUATOR_COUNT; ++index)
    {
        previous_targets[index] = (double)data->ctrl[run.actuator_ids[index]];
    }

    struct timespec wall_start = {0};
    struct timespec wall_end = {0};
    if(clock_gettime(CLOCK_MONOTONIC, &wall_start) != 0)
    {
        print_failure("cannot read monotonic clock before stability run");
        mj_deleteData(data);
        free(run.step_timings);
        mj_deleteModel(model);
        return 1;
    }

    uint64_t global_step = 0U;
    int succeeded = 1;
    for(uint32_t cycle = 0U; cycle < cycles && succeeded != 0; ++cycle)
    {
        (void)cycle;
        for(size_t phase_index = 0U;
            phase_index < REACHY_STABILITY_PHASE_COUNT && succeeded != 0;
            ++phase_index)
        {
            const ReachyStabilityPhase* phase = &REACHY_STABILITY_PHASES[phase_index];
            for(uint64_t phase_step = 0U;
                phase_step < REACHY_STABILITY_STEPS_PER_PHASE;
                ++phase_step)
            {
                double blend = 1.0;
                if(phase_step < (uint64_t)REACHY_STABILITY_TRANSITION_STEPS)
                {
                    const double progress =
                        (double)(phase_step + 1U) /
                        (double)REACHY_STABILITY_TRANSITION_STEPS;
                    blend = minimum_jerk(progress);
                }
                for(size_t actuator_index = 0U;
                    actuator_index < REACHY_STABILITY_ACTUATOR_COUNT;
                    ++actuator_index)
                {
                    const double start_target = previous_targets[actuator_index];
                    const double target = phase->targets[actuator_index];
                    data->ctrl[run.actuator_ids[actuator_index]] =
                        (mjtNum)(start_target + ((target - start_target) * blend));
                }

                struct timespec step_start = {0};
                struct timespec step_end = {0};
                if(clock_gettime(CLOCK_MONOTONIC, &step_start) != 0)
                {
                    (void)snprintf(error, sizeof(error), "cannot read monotonic step start");
                    succeeded = 0;
                    break;
                }
                mj_step(model, data);
                if(clock_gettime(CLOCK_MONOTONIC, &step_end) != 0)
                {
                    (void)snprintf(error, sizeof(error), "cannot read monotonic step end");
                    succeeded = 0;
                    break;
                }
                double step_microseconds = 0.0;
                if(!elapsed_microseconds(&step_start, &step_end, &step_microseconds))
                {
                    (void)snprintf(error, sizeof(error), "invalid monotonic step timing");
                    succeeded = 0;
                    break;
                }
                global_step += 1U;
                if(!monitor_step(
                       model,
                       data,
                       &run.phases[phase_index],
                       &run,
                       step_microseconds,
                       phase->name,
                       global_step,
                       error,
                       sizeof(error)))
                {
                    succeeded = 0;
                    break;
                }
            }
            for(size_t actuator_index = 0U;
                actuator_index < REACHY_STABILITY_ACTUATOR_COUNT;
                ++actuator_index)
            {
                previous_targets[actuator_index] = phase->targets[actuator_index];
            }
        }
    }

    if(clock_gettime(CLOCK_MONOTONIC, &wall_end) != 0)
    {
        (void)snprintf(error, sizeof(error), "cannot read monotonic clock after stability run");
        succeeded = 0;
    }
    double wall_microseconds = 0.0;
    if(succeeded != 0 && !elapsed_microseconds(&wall_start, &wall_end, &wall_microseconds))
    {
        (void)snprintf(error, sizeof(error), "invalid total stability timing");
        succeeded = 0;
    }

    const double expected_seconds = (double)total_steps * REACHY_STABILITY_TIMESTEP_SECONDS;
    if(succeeded != 0 && fabs((double)data->time - expected_seconds) > 1e-7)
    {
        (void)snprintf(
            error,
            sizeof(error),
            "simulation time mismatch: expected %.17g, found %.17g",
            expected_seconds,
            (double)data->time);
        succeeded = 0;
    }
    if(succeeded != 0 && run.aggregate.completed_steps != total_steps)
    {
        (void)snprintf(
            error,
            sizeof(error),
            "completed step mismatch: expected %" PRIu64 ", found %" PRIu64,
            total_steps,
            run.aggregate.completed_steps);
        succeeded = 0;
    }
    if(succeeded != 0)
    {
        const double wall_seconds = wall_microseconds / 1000000.0;
        const double realtime_factor =
            wall_seconds > 0.0 ? (double)data->time / wall_seconds : 0.0;
        if(realtime_factor < REACHY_STABILITY_MINIMUM_SOLVER_REALTIME_FACTOR)
        {
            (void)snprintf(
                error,
                sizeof(error),
                "solver realtime factor %.17g is below required %.17g",
                realtime_factor,
                REACHY_STABILITY_MINIMUM_SOLVER_REALTIME_FACTOR);
            succeeded = 0;
        }
    }

    if(succeeded != 0)
    {
        qsort(run.step_timings, run.timing_count, sizeof(*run.step_timings), compare_doubles);
        print_success(
            &run,
            platform,
            cycles,
            (double)data->time,
            wall_microseconds / 1000000.0);
    }
    else
    {
        print_failure(error[0] == '\0' ? "stability run failed" : error);
    }

    mj_deleteData(data);
    free(run.step_timings);
    mj_deleteModel(model);
    return succeeded != 0 ? 0 : 1;
}
