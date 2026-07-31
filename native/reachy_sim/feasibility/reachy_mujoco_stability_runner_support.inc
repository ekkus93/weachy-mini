static void print_json_string(const char* text)
{
    (void)putchar('"');
    for(const unsigned char* cursor = (const unsigned char*)text; *cursor != '\0'; ++cursor)
    {
        switch(*cursor)
        {
            case '"':
                (void)fputs("\\\"", stdout);
                break;
            case '\\':
                (void)fputs("\\\\", stdout);
                break;
            case '\n':
                (void)fputs("\\n", stdout);
                break;
            case '\r':
                (void)fputs("\\r", stdout);
                break;
            case '\t':
                (void)fputs("\\t", stdout);
                break;
            default:
                if(*cursor < 0x20U)
                {
                    (void)printf("\\u%04x", (unsigned int)*cursor);
                }
                else
                {
                    (void)putchar((int)*cursor);
                }
                break;
        }
    }
    (void)putchar('"');
}

static void initialize_metrics(ReachyStabilityMetrics* metrics)
{
    memset(metrics, 0, sizeof(*metrics));
    metrics->minimum_total_energy = INFINITY;
    metrics->maximum_total_energy = -INFINITY;
}

static void initialize_run(ReachyStabilityRun* run)
{
    memset(run, 0, sizeof(*run));
    initialize_metrics(&run->aggregate);
    for(size_t index = 0U; index < REACHY_STABILITY_PHASE_COUNT; ++index)
    {
        initialize_metrics(&run->phases[index]);
    }
}

static int checked_total_steps(uint32_t cycles, uint64_t* output)
{
    if(output == NULL || cycles == 0U)
    {
        return 0;
    }
    *output = (uint64_t)cycles * REACHY_STABILITY_STEPS_PER_CYCLE;
    return 1;
}

static int allocate_timings(ReachyStabilityRun* run, uint64_t total_steps)
{
    if(run == NULL || total_steps == 0U || total_steps > (uint64_t)SIZE_MAX)
    {
        return 0;
    }
    const size_t count = (size_t)total_steps;
    if(count > SIZE_MAX / sizeof(*run->step_timings))
    {
        return 0;
    }
    run->step_timings = calloc(count, sizeof(*run->step_timings));
    if(run->step_timings == NULL)
    {
        return 0;
    }
    run->timing_capacity = count;
    return 1;
}

static int model_counts_match(const mjModel* model)
{
    return model->nbody == (mjtSize)REACHY_STABILITY_EXPECTED_BODY_COUNT &&
           model->njnt == (mjtSize)REACHY_STABILITY_EXPECTED_JOINT_COUNT &&
           model->nu == (mjtSize)REACHY_STABILITY_EXPECTED_ACTUATOR_COUNT &&
           model->neq == (mjtSize)REACHY_STABILITY_EXPECTED_EQUALITY_COUNT &&
           model->nq == (mjtSize)REACHY_STABILITY_EXPECTED_NQ &&
           model->nv == (mjtSize)REACHY_STABILITY_EXPECTED_NV;
}

static int resolve_actuators(
    const mjModel* model,
    ReachyStabilityRun* run,
    char* error,
    size_t error_size)
{
    for(size_t index = 0U; index < REACHY_STABILITY_ACTUATOR_COUNT; ++index)
    {
        const int actuator_id =
            mj_name2id(model, mjOBJ_ACTUATOR, REACHY_STABILITY_ACTUATOR_NAMES[index]);
        if(actuator_id < 0)
        {
            (void)snprintf(
                error,
                error_size,
                "missing actuator: %s",
                REACHY_STABILITY_ACTUATOR_NAMES[index]);
            return 0;
        }
        if(actuator_id != (int)index)
        {
            (void)snprintf(
                error,
                error_size,
                "actuator order mismatch for %s: expected %zu, found %d",
                REACHY_STABILITY_ACTUATOR_NAMES[index],
                index,
                actuator_id);
            return 0;
        }
        run->actuator_ids[index] = actuator_id;
    }
    return 1;
}

static int validate_phase_ranges(
    const mjModel* model,
    const ReachyStabilityRun* run,
    char* error,
    size_t error_size)
{
    for(size_t phase_index = 0U; phase_index < REACHY_STABILITY_PHASE_COUNT; ++phase_index)
    {
        const ReachyStabilityPhase* phase = &REACHY_STABILITY_PHASES[phase_index];
        uint32_t actual_mask = 0U;
        for(size_t actuator_index = 0U;
            actuator_index < REACHY_STABILITY_ACTUATOR_COUNT;
            ++actuator_index)
        {
            const int actuator_id = run->actuator_ids[actuator_index];
            if(model->actuator_ctrllimited[actuator_id] == 0U)
            {
                continue;
            }
            const double lower = (double)model->actuator_ctrlrange[(actuator_id * 2) + 0];
            const double upper = (double)model->actuator_ctrlrange[(actuator_id * 2) + 1];
            const double target = phase->targets[actuator_index];
            if(target < lower || target > upper)
            {
                actual_mask |= UINT32_C(1) << (uint32_t)actuator_index;
            }
        }
        if(actual_mask != phase->allowed_out_of_range_mask)
        {
            (void)snprintf(
                error,
                error_size,
                "phase %s range mask mismatch: expected %" PRIu32 ", found %" PRIu32,
                phase->name,
                phase->allowed_out_of_range_mask,
                actual_mask);
            return 0;
        }
    }
    return 1;
}

static int finite_values(const mjtNum* values, mjtSize count)
{
    if(count < 0 || (count > 0 && values == NULL))
    {
        return 0;
    }
    for(mjtSize index = 0; index < count; ++index)
    {
        if(!isfinite((double)values[index]))
        {
            return 0;
        }
    }
    return 1;
}

static int finite_state(const mjModel* model, const mjData* data)
{
    return finite_values(data->qpos, model->nq) &&
           finite_values(data->qvel, model->nv) &&
           finite_values(data->qacc, model->nv) &&
           finite_values(data->ctrl, model->nu) &&
           finite_values(data->actuator_force, model->nu) &&
           finite_values(data->xpos, model->nbody * 3) &&
           finite_values(data->xquat, model->nbody * 4) &&
           finite_values(data->efc_pos, data->nefc);
}

static int maximum_equality_residual(const mjData* data, double* output)
{
    if(data == NULL || output == NULL || data->nefc < 0 ||
       (data->nefc > 0 && (data->efc_pos == NULL || data->efc_type == NULL)))
    {
        return 0;
    }
    double maximum = 0.0;
    for(mjtSize index = 0; index < data->nefc; ++index)
    {
        if(data->efc_type[index] != mjCNSTR_EQUALITY)
        {
            continue;
        }
        const double residual = fabs((double)data->efc_pos[index]);
        if(!isfinite(residual))
        {
            return 0;
        }
        if(residual > maximum)
        {
            maximum = residual;
        }
    }
    *output = maximum;
    return 1;
}

static int maximum_joint_limit_violation(
    const mjModel* model,
    const mjData* data,
    double* output)
{
    if(model == NULL || data == NULL || output == NULL)
    {
        return 0;
    }
    double maximum = 0.0;
    for(mjtSize joint_id = 0; joint_id < model->njnt; ++joint_id)
    {
        if(model->jnt_limited[joint_id] == 0U)
        {
            continue;
        }
        const int joint_type = model->jnt_type[joint_id];
        if(joint_type != mjJNT_HINGE && joint_type != mjJNT_SLIDE)
        {
            continue;
        }
        const int qpos_address = model->jnt_qposadr[joint_id];
        if(qpos_address < 0 || qpos_address >= model->nq)
        {
            return 0;
        }
        const double position = (double)data->qpos[qpos_address];
        const double lower = (double)model->jnt_range[(joint_id * 2) + 0];
        const double upper = (double)model->jnt_range[(joint_id * 2) + 1];
        const double violation = fmax(fmax(lower - position, position - upper), 0.0);
        if(!isfinite(violation))
        {
            return 0;
        }
        if(violation > maximum)
        {
            maximum = violation;
        }
    }
    *output = maximum;
    return 1;
}

static int maximum_contact_penetration(const mjData* data, double* output)
{
    if(data == NULL || output == NULL || data->ncon < 0)
    {
        return 0;
    }
    double maximum = 0.0;
    for(int contact_index = 0; contact_index < data->ncon; ++contact_index)
    {
        const double penetration = fmax(-(double)data->contact[contact_index].dist, 0.0);
        if(!isfinite(penetration))
        {
            return 0;
        }
        if(penetration > maximum)
        {
            maximum = penetration;
        }
    }
    *output = maximum;
    return 1;
}

static uint64_t warning_count(const mjData* data)
{
    uint64_t total = 0U;
    for(int index = 0; index < mjNWARNING; ++index)
    {
        if(data->warning[index].number > 0)
        {
            total += (uint64_t)data->warning[index].number;
        }
    }
    return total;
}

static double minimum_jerk(double progress)
{
    const double value = fmin(fmax(progress, 0.0), 1.0);
    return value * value * value * (10.0 + value * (-15.0 + (6.0 * value)));
}

static int elapsed_microseconds(
    const struct timespec* start,
    const struct timespec* end,
    double* output)
{
    if(start == NULL || end == NULL || output == NULL)
    {
        return 0;
    }
    const time_t seconds = end->tv_sec - start->tv_sec;
    const long nanoseconds = end->tv_nsec - start->tv_nsec;
    const double elapsed = ((double)seconds * 1000000.0) + ((double)nanoseconds / 1000.0);
    if(!isfinite(elapsed) || elapsed < 0.0)
    {
        return 0;
    }
    *output = elapsed;
    return 1;
}

static void update_metrics(
    ReachyStabilityMetrics* metrics,
    double residual,
    double joint_violation,
    double contact_penetration,
    uint64_t contact_count,
    double total_energy,
    uint64_t warnings,
    double step_microseconds)
{
    metrics->completed_steps += 1U;
    metrics->maximum_equality_residual = fmax(metrics->maximum_equality_residual, residual);
    metrics->maximum_joint_limit_violation =
        fmax(metrics->maximum_joint_limit_violation, joint_violation);
    metrics->maximum_contact_penetration =
        fmax(metrics->maximum_contact_penetration, contact_penetration);
    if(contact_count > metrics->maximum_contact_count)
    {
        metrics->maximum_contact_count = contact_count;
    }
    metrics->minimum_total_energy = fmin(metrics->minimum_total_energy, total_energy);
    metrics->maximum_total_energy = fmax(metrics->maximum_total_energy, total_energy);
    metrics->maximum_absolute_total_energy =
        fmax(metrics->maximum_absolute_total_energy, fabs(total_energy));
    metrics->warning_count += warnings;
    if(step_microseconds > REACHY_STABILITY_TIMESTEP_SECONDS * 1000000.0)
    {
        metrics->deadline_miss_count += 1U;
    }
    metrics->total_step_microseconds += step_microseconds;
    metrics->maximum_step_microseconds =
        fmax(metrics->maximum_step_microseconds, step_microseconds);
}

