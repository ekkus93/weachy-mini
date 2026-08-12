#include "rma133_benchmark_internal.h"

#include <inttypes.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define POLL_INITIAL_CAPACITY 512U
#define RESPONSE_INITIAL_CAPACITY 1024U

static bool append_bytes(
    char ** buffer, size_t * length, size_t * capacity, const char * text, size_t text_bytes)
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

bool run_case(
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
