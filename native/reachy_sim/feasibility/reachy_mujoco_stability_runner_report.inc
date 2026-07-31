static int monitor_step(
    const mjModel* model,
    mjData* data,
    ReachyStabilityMetrics* phase_metrics,
    ReachyStabilityRun* run,
    double step_microseconds,
    const char* phase_name,
    uint64_t global_step,
    char* error,
    size_t error_size)
{
    if(!finite_state(model, data))
    {
        (void)snprintf(
            error,
            error_size,
            "non-finite state in phase %s at step %" PRIu64,
            phase_name,
            global_step);
        return 0;
    }

    double residual = 0.0;
    double joint_violation = 0.0;
    double contact_penetration = 0.0;
    if(!maximum_equality_residual(data, &residual) ||
       !maximum_joint_limit_violation(model, data, &joint_violation) ||
       !maximum_contact_penetration(data, &contact_penetration))
    {
        (void)snprintf(
            error,
            error_size,
            "invalid monitor state in phase %s at step %" PRIu64,
            phase_name,
            global_step);
        return 0;
    }

    mj_energyPos(model, data);
    mj_energyVel(model, data);
    const double total_energy = (double)data->energy[0] + (double)data->energy[1];
    const uint64_t warnings = warning_count(data);
    if(!isfinite(total_energy))
    {
        (void)snprintf(
            error,
            error_size,
            "non-finite energy in phase %s at step %" PRIu64,
            phase_name,
            global_step);
        return 0;
    }

    const struct {
        double actual;
        double maximum;
        const char* label;
    } checks[] = {
        {residual, REACHY_STABILITY_MAXIMUM_EQUALITY_RESIDUAL, "equality residual"},
        {
            joint_violation,
            REACHY_STABILITY_MAXIMUM_JOINT_LIMIT_VIOLATION,
            "joint-limit violation",
        },
        {
            contact_penetration,
            REACHY_STABILITY_MAXIMUM_CONTACT_PENETRATION,
            "contact penetration",
        },
        {
            fabs(total_energy),
            REACHY_STABILITY_MAXIMUM_ABSOLUTE_TOTAL_ENERGY,
            "absolute total energy",
        },
    };
    for(size_t index = 0U; index < sizeof(checks) / sizeof(checks[0]); ++index)
    {
        if(checks[index].actual > checks[index].maximum)
        {
            (void)snprintf(
                error,
                error_size,
                "%s %.17g exceeds %.17g in phase %s at step %" PRIu64,
                checks[index].label,
                checks[index].actual,
                checks[index].maximum,
                phase_name,
                global_step);
            return 0;
        }
    }
    if(warnings != 0U)
    {
        (void)snprintf(
            error,
            error_size,
            "MuJoCo warning count increased by %" PRIu64 " in phase %s at step %" PRIu64,
            warnings,
            phase_name,
            global_step);
        return 0;
    }
    if(run->timing_count >= run->timing_capacity)
    {
        (void)snprintf(error, error_size, "step timing buffer is exhausted");
        return 0;
    }

    run->step_timings[run->timing_count] = step_microseconds;
    run->timing_count += 1U;
    const uint64_t contacts = data->ncon > 0 ? (uint64_t)data->ncon : 0U;
    update_metrics(
        phase_metrics,
        residual,
        joint_violation,
        contact_penetration,
        contacts,
        total_energy,
        warnings,
        step_microseconds);
    update_metrics(
        &run->aggregate,
        residual,
        joint_violation,
        contact_penetration,
        contacts,
        total_energy,
        warnings,
        step_microseconds);
    return 1;
}

static int compare_doubles(const void* left, const void* right)
{
    const double left_value = *(const double*)left;
    const double right_value = *(const double*)right;
    return (left_value > right_value) - (left_value < right_value);
}

static double percentile(const double* sorted_values, size_t count, double fraction)
{
    if(sorted_values == NULL || count == 0U)
    {
        return 0.0;
    }
    const double scaled = fraction * (double)(count - 1U);
    const size_t lower = (size_t)floor(scaled);
    const size_t upper = (size_t)ceil(scaled);
    if(lower == upper)
    {
        return sorted_values[lower];
    }
    const double weight = scaled - (double)lower;
    return sorted_values[lower] + ((sorted_values[upper] - sorted_values[lower]) * weight);
}

static double normalized_minimum_energy(const ReachyStabilityMetrics* metrics)
{
    return isfinite(metrics->minimum_total_energy) ? metrics->minimum_total_energy : 0.0;
}

static double normalized_maximum_energy(const ReachyStabilityMetrics* metrics)
{
    return isfinite(metrics->maximum_total_energy) ? metrics->maximum_total_energy : 0.0;
}

static void print_metrics(const ReachyStabilityMetrics* metrics)
{
    const double mean_step = metrics->completed_steps == 0U
                                 ? 0.0
                                 : metrics->total_step_microseconds /
                                       (double)metrics->completed_steps;
    (void)printf(
        "{\"completed_steps\":%" PRIu64
        ",\"maximum_equality_residual\":%.17g"
        ",\"maximum_scalar_joint_limit_violation_radians\":%.17g"
        ",\"maximum_contact_penetration_metres\":%.17g"
        ",\"maximum_contact_count\":%" PRIu64
        ",\"minimum_total_energy_joules\":%.17g"
        ",\"maximum_total_energy_joules\":%.17g"
        ",\"maximum_absolute_total_energy_joules\":%.17g"
        ",\"warning_count\":%" PRIu64
        ",\"solver_deadline_miss_count\":%" PRIu64
        ",\"mean_step_microseconds\":%.17g"
        ",\"maximum_step_microseconds\":%.17g}",
        metrics->completed_steps,
        metrics->maximum_equality_residual,
        metrics->maximum_joint_limit_violation,
        metrics->maximum_contact_penetration,
        metrics->maximum_contact_count,
        normalized_minimum_energy(metrics),
        normalized_maximum_energy(metrics),
        metrics->maximum_absolute_total_energy,
        metrics->warning_count,
        metrics->deadline_miss_count,
        mean_step,
        metrics->maximum_step_microseconds);
}

static void print_success(
    const ReachyStabilityRun* run,
    const char* platform,
    uint32_t cycles,
    double simulated_seconds,
    double wall_seconds)
{
    const double realtime_factor = wall_seconds > 0.0 ? simulated_seconds / wall_seconds : 0.0;
    const double median = percentile(run->step_timings, run->timing_count, 0.5);
    const double p95 = percentile(run->step_timings, run->timing_count, 0.95);

    (void)fputs("{\"schema_version\":1,\"status\":\"ok\",\"profile_id\":", stdout);
    print_json_string(REACHY_STABILITY_PROFILE_ID);
    (void)fputs(",\"profile_sha256\":", stdout);
    print_json_string(REACHY_STABILITY_PROFILE_SHA256);
    (void)fputs(",\"platform\":", stdout);
    print_json_string(platform);
    (void)fputs(",\"source_model_sha256\":", stdout);
    print_json_string(REACHY_STABILITY_MODEL_SHA256);
    (void)fputs(",\"mujoco_version\":", stdout);
    print_json_string(mj_versionString());
    (void)fputs(",\"upstream_commit\":", stdout);
    print_json_string(REACHY_STABILITY_UPSTREAM_COMMIT);
    (void)printf(
        ",\"timestep_seconds\":%.17g,\"cycles\":%" PRIu32
        ",\"phase_count_per_cycle\":%u,\"completed_steps\":%" PRIu64
        ",\"simulated_seconds\":%.17g,\"wall_execution_seconds\":%.17g"
        ",\"solver_realtime_factor\":%.17g"
        ",\"timestep_deviation_decision\":\"not_required\""
        ",\"aggregate_metrics\":",
        REACHY_STABILITY_TIMESTEP_SECONDS,
        cycles,
        (unsigned int)REACHY_STABILITY_PHASE_COUNT,
        run->aggregate.completed_steps,
        simulated_seconds,
        wall_seconds,
        realtime_factor);
    print_metrics(&run->aggregate);
    (void)printf(
        ",\"timing_percentiles\":{\"median_step_microseconds\":%.17g"
        ",\"p95_step_microseconds\":%.17g},\"phases\":[",
        median,
        p95);
    for(size_t phase_index = 0U; phase_index < REACHY_STABILITY_PHASE_COUNT; ++phase_index)
    {
        if(phase_index > 0U)
        {
            (void)putchar(',');
        }
        const ReachyStabilityPhase* phase = &REACHY_STABILITY_PHASES[phase_index];
        (void)fputs("{\"name\":", stdout);
        print_json_string(phase->name);
        (void)fputs(",\"category\":", stdout);
        print_json_string(phase->category);
        (void)printf(
            ",\"allowed_out_of_range_mask\":%" PRIu32 ",\"metrics\":",
            phase->allowed_out_of_range_mask);
        print_metrics(&run->phases[phase_index]);
        (void)putchar('}');
    }
    (void)fputs("]}\n", stdout);
}

static void print_failure(const char* error)
{
    (void)fputs(
        "{\"schema_version\":1,\"status\":\"failed\",\"profile_id\":",
        stdout);
    print_json_string(REACHY_STABILITY_PROFILE_ID);
    (void)fputs(",\"error\":", stdout);
    print_json_string(error);
    (void)fputs("}\n", stdout);
}

static int parse_cycles(const char* text, uint32_t* output)
{
    if(text == NULL || output == NULL || *text == '\0')
    {
        return 0;
    }
    errno = 0;
    char* end = NULL;
    const unsigned long long value = strtoull(text, &end, 10);
    if(errno != 0 || end == text || *end != '\0' || value == 0ULL || value > UINT32_MAX)
    {
        return 0;
    }
    *output = (uint32_t)value;
    return 1;
}

