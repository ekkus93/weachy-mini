#define _POSIX_C_SOURCE 200809L

#include "reachy_llama.h"

#include <errno.h>
#include <inttypes.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#define POLL_INITIAL_CAPACITY 512U
#define RESPONSE_INITIAL_CAPACITY 1024U
#define MAX_CASE_LINE_BYTES 16384U

static uint64_t monotonic_us(void)
{
    struct timespec value;
    if (clock_gettime(CLOCK_MONOTONIC, &value) != 0)
    {
        return 0U;
    }
    return (uint64_t)value.tv_sec * 1000000U + (uint64_t)value.tv_nsec / 1000U;
}

static void sleep_one_millisecond(void)
{
    const struct timespec delay = {.tv_sec = 0, .tv_nsec = 1000000L};
    (void)nanosleep(&delay, NULL);
}

static char * read_text_file(const char * path)
{
    FILE * file = fopen(path, "rb");
    if (file == NULL)
    {
        fprintf(stderr, "RMA-133 could not open %s: %s\n", path, strerror(errno));
        return NULL;
    }
    if (fseek(file, 0L, SEEK_END) != 0)
    {
        fclose(file);
        return NULL;
    }
    const long size_long = ftell(file);
    if (size_long < 0L || size_long > 1024L * 1024L)
    {
        fclose(file);
        return NULL;
    }
    if (fseek(file, 0L, SEEK_SET) != 0)
    {
        fclose(file);
        return NULL;
    }
    const size_t size = (size_t)size_long;
    char * buffer = (char *)malloc(size + 1U);
    if (buffer == NULL)
    {
        fclose(file);
        return NULL;
    }
    if (size > 0U && fread(buffer, 1U, size, file) != size)
    {
        free(buffer);
        fclose(file);
        return NULL;
    }
    buffer[size] = '\0';
    fclose(file);
    return buffer;
}

static uint64_t read_status_bytes(const char * key)
{
    FILE * file = fopen("/proc/self/status", "r");
    if (file == NULL)
    {
        return 0U;
    }
    char line[256];
    uint64_t result = 0U;
    while (fgets(line, sizeof(line), file) != NULL)
    {
        if (strncmp(line, key, strlen(key)) == 0)
        {
            unsigned long long kib = 0ULL;
            if (sscanf(line + strlen(key), ": %llu kB", &kib) == 1)
            {
                result = (uint64_t)kib * 1024U;
            }
            break;
        }
    }
    fclose(file);
    return result;
}

static double normalize_temperature(double raw)
{
    if (raw <= 0.0)
    {
        return -1.0;
    }
    if (raw >= 10000.0)
    {
        return raw / 1000.0;
    }
    if (raw >= 100.0)
    {
        return raw / 10.0;
    }
    return raw;
}

static double read_temperature_path(const char * path)
{
    FILE * file = fopen(path, "r");
    if (file == NULL)
    {
        return -1.0;
    }
    double raw = -1.0;
    const int scanned = fscanf(file, "%lf", &raw);
    fclose(file);
    if (scanned != 1)
    {
        return -1.0;
    }
    return normalize_temperature(raw);
}

static double read_battery_temperature_c(void)
{
    static const char * const paths[] = {
        "/sys/class/power_supply/battery/temp",
        "/sys/class/power_supply/battery/batt_temp",
        "/sys/class/power_supply/bms/temp",
    };
    for (size_t index = 0U; index < sizeof(paths) / sizeof(paths[0]); ++index)
    {
        const double value = read_temperature_path(paths[index]);
        if (value > 0.0)
        {
            return value;
        }
    }
    return -1.0;
}

static void json_string(FILE * output, const char * text)
{
    fputc('"', output);
    for (const unsigned char * cursor = (const unsigned char *)text; *cursor != '\0'; ++cursor)
    {
        switch (*cursor)
        {
            case '"':
                fputs("\\\"", output);
                break;
            case '\\':
                fputs("\\\\", output);
                break;
            case '\b':
                fputs("\\b", output);
                break;
            case '\f':
                fputs("\\f", output);
                break;
            case '\n':
                fputs("\\n", output);
                break;
            case '\r':
                fputs("\\r", output);
                break;
            case '\t':
                fputs("\\t", output);
                break;
            default:
                if (*cursor < 0x20U)
                {
                    fprintf(output, "\\u%04x", (unsigned int)*cursor);
                }
                else
                {
                    fputc((int)*cursor, output);
                }
                break;
        }
    }
    fputc('"', output);
}

static void json_hex_bytes(FILE * output, const char * bytes, size_t byte_count)
{
    static const char hex_digits[] = "0123456789abcdef";
    fputc('"', output);
    for (size_t index = 0U; index < byte_count; ++index)
    {
        const unsigned int value = (unsigned int)(unsigned char)bytes[index];
        fputc((int)hex_digits[value >> 4U], output);
        fputc((int)hex_digits[value & 0x0fU], output);
    }
    fputc('"', output);
}

static void init_error(reachy_llama_error_info * error)
{
    memset(error, 0, sizeof(*error));
    error->struct_size = (uint32_t)sizeof(*error);
}

static void print_runtime_error(const char * operation, int32_t status, const reachy_llama_error_info * error)
{
    fprintf(
        stderr,
        "RMA-133 %s failed: status=%s (%" PRId32 ") detail=%s\n",
        operation,
        reachy_llama_status_string(status),
        status,
        error->message);
}

static bool append_bytes(char ** buffer, size_t * length, size_t * capacity, const char * text, size_t text_bytes)
{
    if (text_bytes > SIZE_MAX - *length - 1U)
    {
        return false;
    }
    const size_t needed = *length + text_bytes + 1U;
    if (needed > *capacity)
    {
        size_t new_capacity = *capacity;
        while (new_capacity < needed)
        {
            if (new_capacity > SIZE_MAX / 2U)
            {
                new_capacity = needed;
                break;
            }
            new_capacity *= 2U;
        }
        char * resized = (char *)realloc(*buffer, new_capacity);
        if (resized == NULL)
        {
            return false;
        }
        *buffer = resized;
        *capacity = new_capacity;
    }
    memcpy(*buffer + *length, text, text_bytes);
    *length += text_bytes;
    (*buffer)[*length] = '\0';
    return true;
}

static char * render_prompt(
    reachy_llama_model_handle model,
    const char * system_prompt,
    const char * user_prompt,
    const char * user_suffix)
{
    const bool has_suffix = strcmp(user_suffix, "-") != 0 && user_suffix[0] != '\0';
    const size_t user_length = strlen(user_prompt);
    const size_t suffix_length = has_suffix ? strlen(user_suffix) : 0U;
    if (user_length > SIZE_MAX - suffix_length - 2U)
    {
        return NULL;
    }
    char * final_user = (char *)malloc(user_length + suffix_length + 2U);
    if (final_user == NULL)
    {
        return NULL;
    }
    memcpy(final_user, user_prompt, user_length);
    size_t final_length = user_length;
    if (has_suffix)
    {
        final_user[final_length++] = '\n';
        memcpy(final_user + final_length, user_suffix, suffix_length);
        final_length += suffix_length;
    }
    final_user[final_length] = '\0';

    const reachy_llama_chat_message messages[] = {
        {.role_utf8 = "system", .content_utf8 = system_prompt},
        {.role_utf8 = "user", .content_utf8 = final_user},
    };
    reachy_llama_error_info error;
    init_error(&error);
    size_t required = 0U;
    int32_t status = reachy_llama_apply_chat_template(
        model, NULL, messages, 2U, 1U, NULL, 0U, &required, &error);
    if (status != REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL || required == 0U)
    {
        print_runtime_error("chat-template sizing", status, &error);
        free(final_user);
        return NULL;
    }
    char * rendered = (char *)malloc(required);
    if (rendered == NULL)
    {
        free(final_user);
        return NULL;
    }
    init_error(&error);
    status = reachy_llama_apply_chat_template(
        model, NULL, messages, 2U, 1U, rendered, required, &required, &error);
    free(final_user);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("chat-template render", status, &error);
        free(rendered);
        return NULL;
    }
    return rendered;
}

static bool parse_u32(const char * text, uint32_t * value)
{
    errno = 0;
    char * end = NULL;
    const unsigned long parsed = strtoul(text, &end, 10);
    if (errno != 0 || end == text || *end != '\0' || parsed > UINT32_MAX)
    {
        return false;
    }
    *value = (uint32_t)parsed;
    return true;
}

static bool parse_i32(const char * text, int32_t * value)
{
    errno = 0;
    char * end = NULL;
    const long parsed = strtol(text, &end, 10);
    if (errno != 0 || end == text || *end != '\0' || parsed < INT32_MIN || parsed > INT32_MAX)
    {
        return false;
    }
    *value = (int32_t)parsed;
    return true;
}

static bool parse_float_value(const char * text, float * value)
{
    errno = 0;
    char * end = NULL;
    const float parsed = strtof(text, &end);
    if (errno != 0 || end == text || *end != '\0' || !isfinite(parsed))
    {
        return false;
    }
    *value = parsed;
    return true;
}

static bool parse_double_value(const char * text, double * value)
{
    errno = 0;
    char * end = NULL;
    const double parsed = strtod(text, &end);
    if (errno != 0 || end == text || *end != '\0' || !isfinite(parsed))
    {
        return false;
    }
    *value = parsed;
    return true;
}

static bool finalize_generation(
    reachy_llama_generation_handle generation,
    bool terminal_observed,
    reachy_llama_generation_metrics * metrics)
{
    reachy_llama_error_info error;
    int32_t status = REACHY_LLAMA_STATUS_OK;
    if (!terminal_observed)
    {
        init_error(&error);
        status = reachy_llama_generation_cancel(generation, &error);
        if (status != REACHY_LLAMA_STATUS_OK)
        {
            print_runtime_error("generation cancel", status, &error);
            return false;
        }

        const uint64_t started = monotonic_us();
        while (!terminal_observed)
        {
            reachy_llama_generation_event event;
            memset(&event, 0, sizeof(event));
            event.struct_size = (uint32_t)sizeof(event);
            char stack_buffer[POLL_INITIAL_CAPACITY];
            size_t required = 0U;
            init_error(&error);
            status = reachy_llama_generation_poll(
                generation, &event, stack_buffer, sizeof(stack_buffer), &required, &error);
            if (status == REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL)
            {
                char * larger = (char *)malloc(required);
                if (larger == NULL)
                {
                    return false;
                }
                memset(&event, 0, sizeof(event));
                event.struct_size = (uint32_t)sizeof(event);
                init_error(&error);
                status = reachy_llama_generation_poll(
                    generation, &event, larger, required, &required, &error);
                free(larger);
            }
            if (status != REACHY_LLAMA_STATUS_OK)
            {
                print_runtime_error("generation cancellation drain", status, &error);
                return false;
            }
            terminal_observed = event.type == REACHY_LLAMA_GENERATION_EVENT_CANCELLED ||
                                event.type == REACHY_LLAMA_GENERATION_EVENT_COMPLETED ||
                                event.type == REACHY_LLAMA_GENERATION_EVENT_ERROR;
            if (!terminal_observed)
            {
                const uint64_t now = monotonic_us();
                if (started != 0U && now > started && now - started > 30000000U)
                {
                    fprintf(stderr, "RMA-133 timed out while draining a cancelled generation.\n");
                    return false;
                }
                sleep_one_millisecond();
            }
        }
    }

    bool success = true;
    memset(metrics, 0, sizeof(*metrics));
    metrics->struct_size = (uint32_t)sizeof(*metrics);
    init_error(&error);
    status = reachy_llama_generation_get_metrics(generation, metrics, &error);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("generation metrics", status, &error);
        success = false;
    }

    init_error(&error);
    status = reachy_llama_generation_release(generation, &error);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("generation release", status, &error);
        success = false;
    }
    return success;
}

static bool run_case(
    reachy_llama_model_handle model,
    const char * candidate_id,
    const char * case_id,
    const char * system_prompt,
    const char * user_prompt,
    const char * user_suffix,
    const reachy_llama_generation_config * config,
    double thermal_abort_c)
{
    const double battery_before = read_battery_temperature_c();
    if (battery_before <= 0.0)
    {
        fprintf(stderr, "RMA-133 battery temperature telemetry is unavailable before %s.\n", case_id);
        return false;
    }
    if (battery_before >= thermal_abort_c)
    {
        fprintf(
            stderr,
            "RMA-133 thermal safety stop before %s: %.1f C >= %.1f C\n",
            case_id,
            battery_before,
            thermal_abort_c);
        return false;
    }

    char * prompt = render_prompt(model, system_prompt, user_prompt, user_suffix);
    if (prompt == NULL)
    {
        return false;
    }

    reachy_llama_error_info error;
    init_error(&error);
    reachy_llama_generation_handle generation = 0U;
    int32_t status = reachy_llama_generation_start(model, prompt, config, &generation, &error);
    free(prompt);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        print_runtime_error("generation start", status, &error);
        return false;
    }

    size_t response_capacity = RESPONSE_INITIAL_CAPACITY;
    size_t response_length = 0U;
    char * response = (char *)malloc(response_capacity);
    if (response == NULL)
    {
        reachy_llama_generation_metrics discarded_metrics;
        (void)finalize_generation(generation, false, &discarded_metrics);
        return false;
    }
    response[0] = '\0';
    bool terminal = false;
    bool completed = false;
    while (!terminal)
    {
        reachy_llama_generation_event event;
        memset(&event, 0, sizeof(event));
        event.struct_size = (uint32_t)sizeof(event);
        char stack_buffer[POLL_INITIAL_CAPACITY];
        size_t required = 0U;
        init_error(&error);
        status = reachy_llama_generation_poll(
            generation, &event, stack_buffer, sizeof(stack_buffer), &required, &error);
        char * heap_buffer = NULL;
        char * text = stack_buffer;
        if (status == REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL)
        {
            heap_buffer = (char *)malloc(required);
            if (heap_buffer == NULL)
            {
                break;
            }
            memset(&event, 0, sizeof(event));
            event.struct_size = (uint32_t)sizeof(event);
            init_error(&error);
            status = reachy_llama_generation_poll(
                generation, &event, heap_buffer, required, &required, &error);
            text = heap_buffer;
        }
        if (status != REACHY_LLAMA_STATUS_OK)
        {
            print_runtime_error("generation poll", status, &error);
            free(heap_buffer);
            break;
        }

        if (event.type == REACHY_LLAMA_GENERATION_EVENT_TEXT)
        {
            const size_t text_length = strlen(text);
            if (!append_bytes(&response, &response_length, &response_capacity, text, text_length))
            {
                free(heap_buffer);
                break;
            }
        }
        else if (event.type == REACHY_LLAMA_GENERATION_EVENT_COMPLETED)
        {
            completed = true;
            terminal = true;
        }
        else if (
            event.type == REACHY_LLAMA_GENERATION_EVENT_CANCELLED ||
            event.type == REACHY_LLAMA_GENERATION_EVENT_ERROR)
        {
            terminal = true;
        }
        else
        {
            sleep_one_millisecond();
        }
        free(heap_buffer);
    }

    reachy_llama_generation_metrics metrics;
    if (!finalize_generation(generation, terminal, &metrics))
    {
        free(response);
        return false;
    }

    const double battery_after = read_battery_temperature_c();
    if (battery_after <= 0.0)
    {
        fprintf(stderr, "RMA-133 battery temperature telemetry disappeared after %s.\n", case_id);
        free(response);
        return false;
    }
    const uint64_t peak_rss = read_status_bytes("VmHWM");
    double ttft_ms = 0.0;
    if (metrics.first_text_monotonic_us > metrics.started_monotonic_us)
    {
        ttft_ms = (double)(metrics.first_text_monotonic_us - metrics.started_monotonic_us) / 1000.0;
    }
    double total_ms = 0.0;
    if (metrics.finished_monotonic_us > metrics.started_monotonic_us)
    {
        total_ms = (double)(metrics.finished_monotonic_us - metrics.started_monotonic_us) / 1000.0;
    }
    double decode_rate = 0.0;
    if (
        metrics.generated_tokens > 1U &&
        metrics.finished_monotonic_us > metrics.first_text_monotonic_us)
    {
        const double seconds =
            (double)(metrics.finished_monotonic_us - metrics.first_text_monotonic_us) / 1000000.0;
        decode_rate = (double)(metrics.generated_tokens - 1U) / seconds;
    }

    fputs("{\"record\":\"case\",\"candidate_id\":", stdout);
    json_string(stdout, candidate_id);
    fputs(",\"case_id\":", stdout);
    json_string(stdout, case_id);
    fprintf(
        stdout,
        ",\"completed\":%s,\"prompt_tokens\":%" PRIu64
        ",\"generated_tokens\":%" PRIu64
        ",\"time_to_first_text_ms\":%.3f,\"total_time_ms\":%.3f"
        ",\"decode_tokens_per_second\":%.6f,\"peak_rss_bytes\":%" PRIu64
        ",\"battery_temp_before_c\":%.3f,\"battery_temp_c\":%.3f,\"response_bytes_hex\":",
        completed ? "true" : "false",
        metrics.prompt_tokens,
        metrics.generated_tokens,
        ttft_ms,
        total_ms,
        decode_rate,
        peak_rss,
        battery_before,
        battery_after);
    json_hex_bytes(stdout, response, response_length);
    fputs("}\n", stdout);
    fflush(stdout);
    free(response);
    return completed;
}

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
