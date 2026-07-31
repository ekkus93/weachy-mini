#include <mujoco/mujoco.h>

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

typedef struct BenchmarkResult {
    uint64_t steps;
    double simulated_seconds;
    double elapsed_seconds;
    double realtime_factor;
    double median_step_microseconds;
    double p95_step_microseconds;
    double maximum_step_microseconds;
    uint64_t warning_count;
    int maximum_contact_count;
    double maximum_penetration_metres;
} BenchmarkResult;

static void fail(const char* message, const char* detail)
{
    fprintf(stderr, "%s%s%s\n", message, detail != NULL ? ": " : "", detail != NULL ? detail : "");
    exit(1);
}

static double monotonic_seconds(void)
{
    struct timespec now = {0};
    if(clock_gettime(CLOCK_MONOTONIC, &now) != 0)
    {
        fail("clock_gettime failed", NULL);
    }
    return (double)now.tv_sec + (double)now.tv_nsec / 1000000000.0;
}

static int compare_double(const void* left, const void* right)
{
    const double a = *(const double*)left;
    const double b = *(const double*)right;
    return (a > b) - (a < b);
}

static uint64_t total_warning_count(const mjData* data)
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

static BenchmarkResult benchmark_model(const char* path, uint64_t steps)
{
    char error[1024] = {0};
    mjModel* const model = mj_loadXML(path, NULL, error, sizeof(error));
    if(model == NULL)
    {
        fail("MuJoCo model load failed", error);
    }
    mjData* const data = mj_makeData(model);
    if(data == NULL)
    {
        mj_deleteModel(model);
        fail("MuJoCo data allocation failed", path);
    }
    double* const timings = malloc((size_t)steps * sizeof(double));
    if(timings == NULL)
    {
        mj_deleteData(data);
        mj_deleteModel(model);
        fail("timing allocation failed", NULL);
    }

    mj_forward(model, data);
    int maximum_contact_count = data->ncon;
    double maximum_penetration = 0.0;
    const double elapsed_start = monotonic_seconds();
    for(uint64_t step = 0U; step < steps; ++step)
    {
        const double start = monotonic_seconds();
        mj_step(model, data);
        const double elapsed = monotonic_seconds() - start;
        timings[step] = elapsed * 1000000.0;
        if(data->ncon > maximum_contact_count)
        {
            maximum_contact_count = data->ncon;
        }
        for(int contact_index = 0; contact_index < data->ncon; ++contact_index)
        {
            const double penetration = fmax(0.0, -(double)data->contact[contact_index].dist);
            if(penetration > maximum_penetration)
            {
                maximum_penetration = penetration;
            }
        }
    }
    const double elapsed_seconds = monotonic_seconds() - elapsed_start;
    qsort(timings, (size_t)steps, sizeof(double), compare_double);
    const size_t median_index = (size_t)(steps / 2U);
    size_t p95_index = (size_t)ceil(0.95 * (double)steps) - 1U;
    if(p95_index >= (size_t)steps)
    {
        p95_index = (size_t)steps - 1U;
    }
    const double timestep = (double)model->opt.timestep;
    const BenchmarkResult result = {
        steps,
        timestep * (double)steps,
        elapsed_seconds,
        elapsed_seconds > 0.0 ? timestep * (double)steps / elapsed_seconds : 0.0,
        timings[median_index],
        timings[p95_index],
        timings[(size_t)steps - 1U],
        total_warning_count(data),
        maximum_contact_count,
        maximum_penetration};

    free(timings);
    mj_deleteData(data);
    mj_deleteModel(model);
    return result;
}

static void print_result(const char* name, const BenchmarkResult* result)
{
    printf(
        "\"%s\":{\"steps\":%llu,\"simulated_seconds\":%.17g,"
        "\"elapsed_seconds\":%.17g,\"realtime_factor\":%.17g,"
        "\"median_step_microseconds\":%.17g,\"p95_step_microseconds\":%.17g,"
        "\"maximum_step_microseconds\":%.17g,\"warning_count\":%llu,"
        "\"maximum_contact_count\":%d,\"maximum_penetration_metres\":%.17g}",
        name,
        (unsigned long long)result->steps,
        result->simulated_seconds,
        result->elapsed_seconds,
        result->realtime_factor,
        result->median_step_microseconds,
        result->p95_step_microseconds,
        result->maximum_step_microseconds,
        (unsigned long long)result->warning_count,
        result->maximum_contact_count,
        result->maximum_penetration_metres);
}

int main(int argc, char** argv)
{
    if(argc != 4)
    {
        fail("usage", "reachy_mujoco_collision_benchmark_runner SOURCE.xml ENHANCED.xml STEPS");
    }
    char* end = NULL;
    const unsigned long long parsed = strtoull(argv[3], &end, 10);
    if(end == argv[3] || *end != '\0' || parsed < 1000ULL)
    {
        fail("STEPS must be an integer >= 1000", argv[3]);
    }
    const uint64_t steps = (uint64_t)parsed;
    const BenchmarkResult source = benchmark_model(argv[1], steps);
    const BenchmarkResult enhanced = benchmark_model(argv[2], steps);
    const double overhead = source.p95_step_microseconds > 0.0
        ? enhanced.p95_step_microseconds / source.p95_step_microseconds - 1.0
        : HUGE_VAL;
    printf("{\"status\":\"ok\",");
    print_result("source", &source);
    printf(",");
    print_result("enhanced", &enhanced);
    printf(",\"p95_step_overhead_ratio\":%.17g}\n", overhead);
    return 0;
}
