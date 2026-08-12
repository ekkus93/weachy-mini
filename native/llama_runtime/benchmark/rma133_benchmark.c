#define _POSIX_C_SOURCE 200809L

#include "rma133_benchmark_internal.h"

#include <errno.h>
#include <inttypes.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define MAX_CASE_LINE_BYTES 16384U

int main(int argc, char ** argv)
{
    if (argc != 17)
    {
        fprintf(
            stderr,
            "usage: %s MODEL CANDIDATE CASES_TSV SYSTEM_PROMPT SUFFIX CONTEXT BATCH UBATCH MAX_GEN "
            "THREADS BATCH_THREADS TEMP MIN_P SEED QUEUE THERMAL_ABORT_C\n",
            argv[0]);
        return 2;
    }

    reachy_llama_generation_config generation_config;
    if (reachy_llama_default_generation_config(&generation_config) != REACHY_LLAMA_STATUS_OK)
    {
        return 1;
    }
    double thermal_abort_c = 0.0;
    if (
        !parse_u32(argv[6], &generation_config.context_tokens) ||
        !parse_u32(argv[7], &generation_config.batch_tokens) ||
        !parse_u32(argv[8], &generation_config.micro_batch_tokens) ||
        !parse_u32(argv[9], &generation_config.max_generated_tokens) ||
        !parse_i32(argv[10], &generation_config.threads) ||
        !parse_i32(argv[11], &generation_config.batch_threads) ||
        !parse_float_value(argv[12], &generation_config.temperature) ||
        !parse_float_value(argv[13], &generation_config.min_p) ||
        !parse_u32(argv[14], &generation_config.seed) ||
        !parse_u32(argv[15], &generation_config.stream_queue_capacity) ||
        !parse_double_value(argv[16], &thermal_abort_c) || thermal_abort_c <= 0.0)
    {
        fprintf(stderr, "RMA-133 benchmark arguments are invalid.\n");
        return 2;
    }

    char * system_prompt = read_text_file(argv[4]);
    if (system_prompt == NULL || system_prompt[0] == '\0')
    {
        free(system_prompt);
        return 1;
    }

    reachy_llama_model_config model_config;
    if (reachy_llama_default_model_config(&model_config) != REACHY_LLAMA_STATUS_OK)
    {
        free(system_prompt);
        return 1;
    }
    const uint64_t rss_before = read_status_bytes("VmRSS");
    const double battery_before = read_battery_temperature_c();
    if (battery_before <= 0.0)
    {
        fprintf(stderr, "RMA-133 battery temperature telemetry is unavailable before model load.\n");
        free(system_prompt);
        return 1;
    }
    if (battery_before >= thermal_abort_c)
    {
        fprintf(
            stderr,
            "RMA-133 thermal safety stop before model load: %.1f C >= %.1f C\n",
            battery_before,
            thermal_abort_c);
        free(system_prompt);
        return 1;
    }

    reachy_llama_error_info error;
    init_error(&error);
    reachy_llama_model_handle model = 0U;
    const uint64_t load_started = monotonic_us();
    int32_t status = reachy_llama_model_load(argv[1], &model_config, &model, &error);
    const uint64_t load_finished = monotonic_us();
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("model load", status, &error);
        free(system_prompt);
        return 1;
    }

    reachy_llama_model_metrics model_metrics;
    memset(&model_metrics, 0, sizeof(model_metrics));
    model_metrics.struct_size = (uint32_t)sizeof(model_metrics);
    init_error(&error);
    status = reachy_llama_model_get_metrics(model, &model_metrics, &error);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("model metrics", status, &error);
        init_error(&error);
        (void)reachy_llama_model_unload(model, &error);
        free(system_prompt);
        return 1;
    }
    const uint64_t rss_after = read_status_bytes("VmRSS");
    const uint64_t peak_after_load = read_status_bytes("VmHWM");
    const double load_time_ms =
        load_finished > load_started ? (double)(load_finished - load_started) / 1000.0 : 0.0;

    fputs("{\"record\":\"model\",\"candidate_id\":", stdout);
    json_string(stdout, argv[2]);
    fprintf(
        stdout,
        ",\"load_time_ms\":%.3f,\"tensor_bytes\":%" PRIu64
        ",\"parameter_count\":%" PRIu64
        ",\"training_context_tokens\":%" PRId32
        ",\"rss_before_load_bytes\":%" PRIu64
        ",\"rss_after_load_bytes\":%" PRIu64
        ",\"peak_rss_bytes\":%" PRIu64
        ",\"battery_temp_before_c\":%.3f,\"thermal_zone_max_before_c\":null}\n",
        load_time_ms,
        model_metrics.tensor_bytes,
        model_metrics.parameter_count,
        model_metrics.training_context_tokens,
        rss_before,
        rss_after,
        peak_after_load,
        battery_before);
    fflush(stdout);

    FILE * cases = fopen(argv[3], "r");
    if (cases == NULL)
    {
        fprintf(stderr, "RMA-133 could not open behavior cases: %s\n", strerror(errno));
        init_error(&error);
        (void)reachy_llama_model_unload(model, &error);
        free(system_prompt);
        return 1;
    }
    char * line = NULL;
    size_t line_capacity = 0U;
    ssize_t line_length = getline(&line, &line_capacity, cases);
    if (line_length <= 0 || strncmp(line, "case_id\t", 8U) != 0)
    {
        fprintf(stderr, "RMA-133 behavior case header is invalid.\n");
        free(line);
        fclose(cases);
        init_error(&error);
        (void)reachy_llama_model_unload(model, &error);
        free(system_prompt);
        return 1;
    }

    size_t case_count = 0U;
    bool all_completed = true;
    while ((line_length = getline(&line, &line_capacity, cases)) > 0)
    {
        if ((size_t)line_length > MAX_CASE_LINE_BYTES)
        {
            fprintf(stderr, "RMA-133 behavior case line exceeds the bounded size.\n");
            all_completed = false;
            break;
        }
        while (line_length > 0 && (line[line_length - 1] == '\n' || line[line_length - 1] == '\r'))
        {
            line[--line_length] = '\0';
        }
        if (line_length == 0)
        {
            continue;
        }
        char * first_tab = strchr(line, '\t');
        if (first_tab == NULL)
        {
            all_completed = false;
            break;
        }
        *first_tab = '\0';
        char * prompt = first_tab + 1;
        char * second_tab = strchr(prompt, '\t');
        if (second_tab == NULL)
        {
            all_completed = false;
            break;
        }
        *second_tab = '\0';
        ++case_count;
        if (!run_case(
                model,
                argv[2],
                line,
                system_prompt,
                prompt,
                argv[5],
                &generation_config,
                thermal_abort_c))
        {
            all_completed = false;
            break;
        }
    }
    free(line);
    fclose(cases);

    const uint64_t final_peak = read_status_bytes("VmHWM");
    const double battery_after = read_battery_temperature_c();
    if (battery_after <= 0.0)
    {
        fprintf(stderr, "RMA-133 battery temperature telemetry disappeared before summary.\n");
        all_completed = false;
    }
    fputs("{\"record\":\"summary\",\"candidate_id\":", stdout);
    json_string(stdout, argv[2]);
    fprintf(
        stdout,
        ",\"case_count\":%zu,\"all_completed\":%s,\"peak_rss_bytes\":%" PRIu64
        ",\"battery_temp_after_c\":%.3f,\"thermal_zone_max_after_c\":null}\n",
        case_count,
        all_completed ? "true" : "false",
        final_peak,
        battery_after);
    fflush(stdout);

    init_error(&error);
    status = reachy_llama_model_unload(model, &error);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("model unload", status, &error);
        all_completed = false;
    }
    free(system_prompt);
    return all_completed ? 0 : 1;
}
