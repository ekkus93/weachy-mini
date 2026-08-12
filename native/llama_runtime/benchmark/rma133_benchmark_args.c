#include "rma133_benchmark_internal.h"

#include <errno.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

bool parse_u32(const char * text, uint32_t * value)
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

bool parse_i32(const char * text, int32_t * value)
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

bool parse_float_value(const char * text, float * value)
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

bool parse_double_value(const char * text, double * value)
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
