#ifndef RMA133_BENCHMARK_INTERNAL_H
#define RMA133_BENCHMARK_INTERNAL_H

#include "reachy_llama.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

/* rma133_benchmark_platform.c */
uint64_t monotonic_us(void);
void sleep_one_millisecond(void);
char * read_text_file(const char * path);
uint64_t read_status_bytes(const char * key);
double read_battery_temperature_c(void);

/* rma133_benchmark_output.c */
void init_error(reachy_llama_error_info * error);
void print_runtime_error(
    const char * operation, int32_t status, const reachy_llama_error_info * error);
void json_string(FILE * output, const char * text);
void json_hex_bytes(FILE * output, const char * bytes, size_t byte_count);

/* rma133_benchmark_args.c */
bool parse_u32(const char * text, uint32_t * value);
bool parse_i32(const char * text, int32_t * value);
bool parse_float_value(const char * text, float * value);
bool parse_double_value(const char * text, double * value);

/* rma133_benchmark_generation.c */
bool run_case(
    reachy_llama_model_handle model,
    const char * candidate_id,
    const char * case_id,
    const char * system_prompt,
    const char * user_prompt,
    const char * user_suffix,
    const reachy_llama_generation_config * config,
    double thermal_abort_c);

#endif
