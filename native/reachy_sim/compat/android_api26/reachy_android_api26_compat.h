#ifndef REACHY_ANDROID_API26_COMPAT_H
#define REACHY_ANDROID_API26_COMPAT_H

#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

void* reachy_android_api26_aligned_alloc(size_t alignment, size_t size);

#ifdef __cplusplus
}
#endif

#ifndef REACHY_ANDROID_API26_COMPAT_IMPLEMENTATION
#define aligned_alloc reachy_android_api26_aligned_alloc
#endif

#endif
