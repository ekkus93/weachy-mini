#define _POSIX_C_SOURCE 200809L

#include "reachy_llama.h"

#include <inttypes.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const char * g_v6_grammar = NULL;
static size_t g_v6_grammar_bytes = 0U;
static const char * g_v6_grammar_root = NULL;
static size_t g_v6_grammar_root_bytes = 0U;
static uint32_t g_v6_constrained_start_attempts = 0U;
static uint32_t g_v6_constrained_start_successes = 0U;
static int32_t g_v6_terminal_error_status = REACHY_LLAMA_STATUS_OK;
static uint32_t g_v6_text_event_count = 0U;

static int32_t rma133_v6_generation_start(
    reachy_llama_model_handle model,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    reachy_llama_generation_handle * out_generation,
    reachy_llama_error_info * error)
{
    ++g_v6_constrained_start_attempts;
    reachy_llama_generation_constraint constraint;
    memset(&constraint, 0, sizeof(constraint));
    constraint.struct_size = (uint32_t)sizeof(constraint);
    constraint.abi_version = REACHY_LLAMA_ABI_VERSION;
    constraint.type = REACHY_LLAMA_CONSTRAINT_GBNF;
    constraint.grammar_utf8 = g_v6_grammar;
    constraint.grammar_bytes = g_v6_grammar_bytes;
    constraint.root_utf8 = g_v6_grammar_root;
    constraint.root_bytes = g_v6_grammar_root_bytes;
    const int32_t status = reachy_llama_generation_start_constrained(
        model, prompt_utf8, config, &constraint, out_generation, error);
    if (status == REACHY_LLAMA_STATUS_OK)
    {
        ++g_v6_constrained_start_successes;
    }
    return status;
}

static int32_t rma133_v6_generation_poll(
    reachy_llama_generation_handle generation,
    reachy_llama_generation_event * event,
    char * text_utf8,
    size_t text_capacity,
    size_t * required_bytes,
    reachy_llama_error_info * error)
{
    const int32_t status = reachy_llama_generation_poll(
        generation, event, text_utf8, text_capacity, required_bytes, error);
    if (status == REACHY_LLAMA_STATUS_OK && event != NULL)
    {
        if (event->type == REACHY_LLAMA_GENERATION_EVENT_TEXT)
        {
            ++g_v6_text_event_count;
        }
        else if (event->type == REACHY_LLAMA_GENERATION_EVENT_ERROR)
        {
            g_v6_terminal_error_status = event->status;
        }
    }
    return status;
}

// Reuse the accepted V5 physical benchmark implementation without modifying its
// historical source. Only its generation-start call and main symbol are redirected.
#define reachy_llama_generation_start rma133_v6_generation_start
#define reachy_llama_generation_poll rma133_v6_generation_poll
#define main rma133_v5_main
#include "rma133_benchmark_v5_base.inc"
#undef main
#undef reachy_llama_generation_poll
#undef reachy_llama_generation_start

struct sha256_context
{
    uint8_t data[64];
    uint32_t data_length;
    uint64_t bit_length;
    uint32_t state[8];
};

static const uint32_t sha256_k[64] = {
    0x428a2f98U, 0x71374491U, 0xb5c0fbcfU, 0xe9b5dba5U, 0x3956c25bU, 0x59f111f1U,
    0x923f82a4U, 0xab1c5ed5U, 0xd807aa98U, 0x12835b01U, 0x243185beU, 0x550c7dc3U,
    0x72be5d74U, 0x80deb1feU, 0x9bdc06a7U, 0xc19bf174U, 0xe49b69c1U, 0xefbe4786U,
    0x0fc19dc6U, 0x240ca1ccU, 0x2de92c6fU, 0x4a7484aaU, 0x5cb0a9dcU, 0x76f988daU,
    0x983e5152U, 0xa831c66dU, 0xb00327c8U, 0xbf597fc7U, 0xc6e00bf3U, 0xd5a79147U,
    0x06ca6351U, 0x14292967U, 0x27b70a85U, 0x2e1b2138U, 0x4d2c6dfcU, 0x53380d13U,
    0x650a7354U, 0x766a0abbU, 0x81c2c92eU, 0x92722c85U, 0xa2bfe8a1U, 0xa81a664bU,
    0xc24b8b70U, 0xc76c51a3U, 0xd192e819U, 0xd6990624U, 0xf40e3585U, 0x106aa070U,
    0x19a4c116U, 0x1e376c08U, 0x2748774cU, 0x34b0bcb5U, 0x391c0cb3U, 0x4ed8aa4aU,
    0x5b9cca4fU, 0x682e6ff3U, 0x748f82eeU, 0x78a5636fU, 0x84c87814U, 0x8cc70208U,
    0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U,
};

static uint32_t rotr32(uint32_t value, uint32_t count)
{
    return (value >> count) | (value << (32U - count));
}

static void sha256_transform(struct sha256_context * ctx, const uint8_t data[64])
{
    uint32_t m[64];
    for (uint32_t index = 0U; index < 16U; ++index)
    {
        const uint32_t offset = index * 4U;
        m[index] = ((uint32_t)data[offset] << 24U) |
                   ((uint32_t)data[offset + 1U] << 16U) |
                   ((uint32_t)data[offset + 2U] << 8U) |
                   (uint32_t)data[offset + 3U];
    }
    for (uint32_t index = 16U; index < 64U; ++index)
    {
        const uint32_t s0 = rotr32(m[index - 15U], 7U) ^ rotr32(m[index - 15U], 18U) ^
                            (m[index - 15U] >> 3U);
        const uint32_t s1 = rotr32(m[index - 2U], 17U) ^ rotr32(m[index - 2U], 19U) ^
                            (m[index - 2U] >> 10U);
        m[index] = m[index - 16U] + s0 + m[index - 7U] + s1;
    }

    uint32_t a = ctx->state[0];
    uint32_t b = ctx->state[1];
    uint32_t c = ctx->state[2];
    uint32_t d = ctx->state[3];
    uint32_t e = ctx->state[4];
    uint32_t f = ctx->state[5];
    uint32_t g = ctx->state[6];
    uint32_t h = ctx->state[7];

    for (uint32_t index = 0U; index < 64U; ++index)
    {
        const uint32_t s1 = rotr32(e, 6U) ^ rotr32(e, 11U) ^ rotr32(e, 25U);
        const uint32_t choice = (e & f) ^ ((~e) & g);
        const uint32_t temp1 = h + s1 + choice + sha256_k[index] + m[index];
        const uint32_t s0 = rotr32(a, 2U) ^ rotr32(a, 13U) ^ rotr32(a, 22U);
        const uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
        const uint32_t temp2 = s0 + majority;
        h = g;
        g = f;
        f = e;
        e = d + temp1;
        d = c;
        c = b;
        b = a;
        a = temp1 + temp2;
    }

    ctx->state[0] += a;
    ctx->state[1] += b;
    ctx->state[2] += c;
    ctx->state[3] += d;
    ctx->state[4] += e;
    ctx->state[5] += f;
    ctx->state[6] += g;
    ctx->state[7] += h;
}

static void sha256_init(struct sha256_context * ctx)
{
    ctx->data_length = 0U;
    ctx->bit_length = 0U;
    ctx->state[0] = 0x6a09e667U;
    ctx->state[1] = 0xbb67ae85U;
    ctx->state[2] = 0x3c6ef372U;
    ctx->state[3] = 0xa54ff53aU;
    ctx->state[4] = 0x510e527fU;
    ctx->state[5] = 0x9b05688cU;
    ctx->state[6] = 0x1f83d9abU;
    ctx->state[7] = 0x5be0cd19U;
}

static void sha256_update(struct sha256_context * ctx, const uint8_t * data, size_t length)
{
    for (size_t index = 0U; index < length; ++index)
    {
        ctx->data[ctx->data_length] = data[index];
        ++ctx->data_length;
        if (ctx->data_length == 64U)
        {
            sha256_transform(ctx, ctx->data);
            ctx->bit_length += 512U;
            ctx->data_length = 0U;
        }
    }
}

static void sha256_final(struct sha256_context * ctx, uint8_t hash[32])
{
    uint32_t index = ctx->data_length;
    ctx->data[index++] = 0x80U;
    if (index > 56U)
    {
        while (index < 64U)
        {
            ctx->data[index++] = 0U;
        }
        sha256_transform(ctx, ctx->data);
        index = 0U;
    }
    while (index < 56U)
    {
        ctx->data[index++] = 0U;
    }

    ctx->bit_length += (uint64_t)ctx->data_length * 8U;
    for (uint32_t byte = 0U; byte < 8U; ++byte)
    {
        ctx->data[63U - byte] = (uint8_t)(ctx->bit_length >> (byte * 8U));
    }
    sha256_transform(ctx, ctx->data);

    for (uint32_t word = 0U; word < 8U; ++word)
    {
        hash[word * 4U] = (uint8_t)(ctx->state[word] >> 24U);
        hash[word * 4U + 1U] = (uint8_t)(ctx->state[word] >> 16U);
        hash[word * 4U + 2U] = (uint8_t)(ctx->state[word] >> 8U);
        hash[word * 4U + 3U] = (uint8_t)ctx->state[word];
    }
}

static bool sha256_file_hex(const char * path, char output[65])
{
    FILE * file = fopen(path, "rb");
    if (file == NULL)
    {
        return false;
    }
    struct sha256_context ctx;
    sha256_init(&ctx);
    uint8_t buffer[8192];
    for (;;)
    {
        const size_t read_count = fread(buffer, 1U, sizeof(buffer), file);
        if (read_count > 0U)
        {
            sha256_update(&ctx, buffer, read_count);
        }
        if (read_count < sizeof(buffer))
        {
            if (ferror(file) != 0)
            {
                fclose(file);
                return false;
            }
            break;
        }
    }
    fclose(file);

    uint8_t hash[32];
    sha256_final(&ctx, hash);
    static const char hex[] = "0123456789abcdef";
    for (size_t index = 0U; index < 32U; ++index)
    {
        output[index * 2U] = hex[hash[index] >> 4U];
        output[index * 2U + 1U] = hex[hash[index] & 0x0fU];
    }
    output[64] = '\0';
    return true;
}

static bool valid_sha256_hex(const char * value)
{
    if (value == NULL || strlen(value) != 64U)
    {
        return false;
    }
    for (size_t index = 0U; index < 64U; ++index)
    {
        if (!((value[index] >= '0' && value[index] <= '9') ||
              (value[index] >= 'a' && value[index] <= 'f')))
        {
            return false;
        }
    }
    return true;
}

int main(int argc, char ** argv)
{
    if (argc != 22)
    {
        fprintf(
            stderr,
            "usage: %s MODEL CANDIDATE CASES_TSV SYSTEM_PROMPT SUFFIX CONTEXT BATCH UBATCH MAX_GEN "
            "THREADS BATCH_THREADS TEMP MIN_P SEED QUEUE THERMAL_ABORT_C GRAMMAR_FILE "
            "GRAMMAR_CONTRACT_PATH GRAMMAR_SHA256 GRAMMAR_ROOT CONSTRAINT_TYPE\n",
            argv[0]);
        return 2;
    }
    if (reachy_llama_abi_version() != 2U || REACHY_LLAMA_ABI_VERSION != 2U)
    {
        fprintf(stderr, "RMA-133 V6 requires reachy_llama ABI 2.\n");
        return 1;
    }
    if (strcmp(argv[21], "GBNF") != 0 || !valid_sha256_hex(argv[19]) ||
        argv[18][0] == '\0' || argv[20][0] == '\0')
    {
        fprintf(stderr, "RMA-133 V6 constrained-generation arguments are invalid.\n");
        return 2;
    }

    char actual_sha256[65];
    if (!sha256_file_hex(argv[17], actual_sha256))
    {
        fprintf(stderr, "RMA-133 V6 could not hash the grammar file.\n");
        return 1;
    }
    if (strcmp(actual_sha256, argv[19]) != 0)
    {
        fprintf(
            stderr,
            "RMA-133 V6 grammar integrity failure: expected=%s actual=%s\n",
            argv[19],
            actual_sha256);
        return 1;
    }

    char * grammar = read_text_file(argv[17]);
    if (grammar == NULL || grammar[0] == '\0')
    {
        fprintf(stderr, "RMA-133 V6 grammar file is missing or empty.\n");
        free(grammar);
        return 1;
    }
    const size_t grammar_bytes = strlen(grammar);
    const size_t root_bytes = strlen(argv[20]);
    if (grammar_bytes > REACHY_LLAMA_MAX_GRAMMAR_BYTES ||
        root_bytes == 0U || root_bytes > REACHY_LLAMA_MAX_GRAMMAR_ROOT_BYTES)
    {
        fprintf(stderr, "RMA-133 V6 grammar exceeds the ABI-2 bounded constraint size.\n");
        free(grammar);
        return 1;
    }

    g_v6_grammar = grammar;
    g_v6_grammar_bytes = grammar_bytes;
    g_v6_grammar_root = argv[20];
    g_v6_grammar_root_bytes = root_bytes;
    g_v6_constrained_start_attempts = 0U;
    g_v6_constrained_start_successes = 0U;
    g_v6_terminal_error_status = REACHY_LLAMA_STATUS_OK;
    g_v6_text_event_count = 0U;

    char * legacy_argv[17];
    for (int index = 0; index < 17; ++index)
    {
        legacy_argv[index] = argv[index];
    }
    const int base_exit_code = rma133_v5_main(17, legacy_argv);
    const bool constrained_mode_active =
        base_exit_code == 0 && g_v6_constrained_start_attempts == 12U &&
        g_v6_constrained_start_successes == 12U;

    fputs("{\"record\":\"constraint\",\"candidate_id\":", stdout);
    json_string(stdout, argv[2]);
    fprintf(stdout, ",\"runtime_abi_version\":%u,\"constraint_type\":", reachy_llama_abi_version());
    json_string(stdout, argv[21]);
    fputs(",\"grammar_path\":", stdout);
    json_string(stdout, argv[18]);
    fputs(",\"grammar_sha256\":", stdout);
    json_string(stdout, actual_sha256);
    fputs(",\"grammar_root\":", stdout);
    json_string(stdout, argv[20]);
    fprintf(
        stdout,
        ",\"constrained_start_attempts\":%" PRIu32
        ",\"constrained_start_successes\":%" PRIu32
        ",\"terminal_error_status\":%" PRId32
        ",\"text_event_count\":%" PRIu32
        ",\"constrained_mode_active\":%s,\"base_exit_code\":%d}\n",
        g_v6_constrained_start_attempts,
        g_v6_constrained_start_successes,
        g_v6_terminal_error_status,
        g_v6_text_event_count,
        constrained_mode_active ? "true" : "false",
        base_exit_code);
    fflush(stdout);

    g_v6_grammar = NULL;
    g_v6_grammar_bytes = 0U;
    g_v6_grammar_root = NULL;
    g_v6_grammar_root_bytes = 0U;
    free(grammar);
    return base_exit_code;
}
