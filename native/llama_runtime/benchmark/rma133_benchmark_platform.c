#define _POSIX_C_SOURCE 200809L

#include "rma133_benchmark_internal.h"

#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

uint64_t monotonic_us(void)
{
    struct timespec value;
    if (clock_gettime(CLOCK_MONOTONIC, &value) != 0)
    {
        return 0U;
    }
    return (uint64_t)value.tv_sec * 1000000U + (uint64_t)value.tv_nsec / 1000U;
}

void sleep_one_millisecond(void)
{
    const struct timespec delay = {.tv_sec = 0, .tv_nsec = 1000000L};
    (void)nanosleep(&delay, NULL);
}

char * read_text_file(const char * path)
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

uint64_t read_status_bytes(const char * key)
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

double read_battery_temperature_c(void)
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
