#include "rma133_benchmark_internal.h"

#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

void init_error(reachy_llama_error_info * error)
{
    memset(error, 0, sizeof(*error));
    error->struct_size = (uint32_t)sizeof(*error);
}

void print_runtime_error(
    const char * operation, int32_t status, const reachy_llama_error_info * error)
{
    fprintf(
        stderr,
        "RMA-133 %s failed: status=%s (%" PRId32 ") detail=%s\n",
        operation,
        reachy_llama_status_string(status),
        status,
        error->message);
}

void json_string(FILE * output, const char * text)
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

void json_hex_bytes(FILE * output, const char * bytes, size_t byte_count)
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
