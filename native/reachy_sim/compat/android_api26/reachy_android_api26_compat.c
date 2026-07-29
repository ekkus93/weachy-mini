#define REACHY_ANDROID_API26_COMPAT_IMPLEMENTATION
#include "reachy_android_api26_compat.h"

#include <errno.h>
#include <stdint.h>
#include <stdlib.h>

void* reachy_android_api26_aligned_alloc(size_t alignment, size_t size)
{
    if(alignment < sizeof(void*) ||
       (alignment & (alignment - 1U)) != 0U ||
       (alignment % sizeof(void*)) != 0U ||
       (size % alignment) != 0U)
    {
        errno = EINVAL;
        return NULL;
    }

    void* pointer = NULL;
    const int result = posix_memalign(&pointer, alignment, size);
    if(result != 0)
    {
        errno = result;
        return NULL;
    }
    return pointer;
}
