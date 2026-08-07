#ifndef REACHY_LLAMA_H
#define REACHY_LLAMA_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define REACHY_LLAMA_ABI_VERSION 1u
#define REACHY_LLAMA_ERROR_MESSAGE_CAPACITY 384u

#if defined(_WIN32)
#define REACHY_LLAMA_API __declspec(dllexport)
#elif defined(__GNUC__) || defined(__clang__)
#define REACHY_LLAMA_API __attribute__((visibility("default")))
#else
#define REACHY_LLAMA_API
#endif

typedef uint64_t reachy_llama_model_handle;
typedef uint64_t reachy_llama_generation_handle;

typedef enum reachy_llama_status {
    REACHY_LLAMA_STATUS_OK = 0,
    REACHY_LLAMA_STATUS_INVALID_ARGUMENT = 1,
    REACHY_LLAMA_STATUS_ABI_MISMATCH = 2,
    REACHY_LLAMA_STATUS_NOT_FOUND = 3,
    REACHY_LLAMA_STATUS_BUSY = 4,
    REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL = 5,
    REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED = 6,
    REACHY_LLAMA_STATUS_TOKENIZE_FAILED = 7,
    REACHY_LLAMA_STATUS_TEMPLATE_FAILED = 8,
    REACHY_LLAMA_STATUS_CONTEXT_CREATE_FAILED = 9,
    REACHY_LLAMA_STATUS_CONTEXT_LIMIT = 10,
    REACHY_LLAMA_STATUS_DECODE_FAILED = 11,
    REACHY_LLAMA_STATUS_UNSUPPORTED_MODEL = 12,
    REACHY_LLAMA_STATUS_CANCELLED = 13,
    REACHY_LLAMA_STATUS_INTERNAL_ERROR = 14
} reachy_llama_status;

typedef enum reachy_llama_generation_event_type {
    REACHY_LLAMA_GENERATION_EVENT_NONE = 0,
    REACHY_LLAMA_GENERATION_EVENT_TEXT = 1,
    REACHY_LLAMA_GENERATION_EVENT_COMPLETED = 2,
    REACHY_LLAMA_GENERATION_EVENT_CANCELLED = 3,
    REACHY_LLAMA_GENERATION_EVENT_ERROR = 4
} reachy_llama_generation_event_type;

typedef enum reachy_llama_generation_state {
    REACHY_LLAMA_GENERATION_STATE_RUNNING = 0,
    REACHY_LLAMA_GENERATION_STATE_COMPLETED = 1,
    REACHY_LLAMA_GENERATION_STATE_CANCELLED = 2,
    REACHY_LLAMA_GENERATION_STATE_ERROR = 3
} reachy_llama_generation_state;

typedef struct reachy_llama_error_info {
    uint32_t struct_size;
    int32_t status;
    char message[REACHY_LLAMA_ERROR_MESSAGE_CAPACITY];
} reachy_llama_error_info;

typedef struct reachy_llama_model_config {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t check_tensors;
    uint32_t reserved;
} reachy_llama_model_config;

typedef struct reachy_llama_generation_config {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t context_tokens;
    uint32_t batch_tokens;
    uint32_t micro_batch_tokens;
    uint32_t max_generated_tokens;
    int32_t threads;
    int32_t batch_threads;
    float temperature;
    float min_p;
    uint32_t seed;
    uint32_t stream_queue_capacity;
} reachy_llama_generation_config;

typedef struct reachy_llama_chat_message {
    const char * role_utf8;
    const char * content_utf8;
} reachy_llama_chat_message;

typedef struct reachy_llama_generation_event {
    uint32_t struct_size;
    uint32_t type;
    int32_t status;
    uint32_t reserved;
    uint64_t sequence;
} reachy_llama_generation_event;

typedef struct reachy_llama_model_metrics {
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t tensor_bytes;
    uint64_t parameter_count;
    int32_t training_context_tokens;
    uint32_t reserved;
} reachy_llama_model_metrics;

typedef struct reachy_llama_generation_metrics {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t state;
    uint32_t queue_depth;
    uint64_t prompt_tokens;
    uint64_t generated_tokens;
    uint64_t started_monotonic_us;
    uint64_t first_text_monotonic_us;
    uint64_t finished_monotonic_us;
    uint32_t context_tokens;
    uint32_t batch_tokens;
    int32_t threads;
    int32_t batch_threads;
} reachy_llama_generation_metrics;

REACHY_LLAMA_API uint32_t reachy_llama_abi_version(void);
REACHY_LLAMA_API const char * reachy_llama_version_string(void);
REACHY_LLAMA_API const char * reachy_llama_upstream_revision(void);
REACHY_LLAMA_API const char * reachy_llama_status_string(int32_t status);

REACHY_LLAMA_API int32_t reachy_llama_default_model_config(reachy_llama_model_config * config);
REACHY_LLAMA_API int32_t reachy_llama_default_generation_config(reachy_llama_generation_config * config);

REACHY_LLAMA_API int32_t reachy_llama_model_load(
    const char * model_path_utf8,
    const reachy_llama_model_config * config,
    reachy_llama_model_handle * out_model,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_model_unload(
    reachy_llama_model_handle model,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_model_get_metrics(
    reachy_llama_model_handle model,
    reachy_llama_model_metrics * metrics,
    reachy_llama_error_info * error);

REACHY_LLAMA_API int32_t reachy_llama_tokenize(
    reachy_llama_model_handle model,
    const char * text_utf8,
    uint32_t add_special,
    uint32_t parse_special,
    int32_t * tokens,
    size_t token_capacity,
    size_t * required_tokens,
    reachy_llama_error_info * error);

REACHY_LLAMA_API int32_t reachy_llama_apply_chat_template(
    reachy_llama_model_handle model,
    const char * template_utf8,
    const reachy_llama_chat_message * messages,
    size_t message_count,
    uint32_t add_assistant,
    char * output_utf8,
    size_t output_capacity,
    size_t * required_bytes,
    reachy_llama_error_info * error);

REACHY_LLAMA_API int32_t reachy_llama_generation_start(
    reachy_llama_model_handle model,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    reachy_llama_generation_handle * out_generation,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_generation_poll(
    reachy_llama_generation_handle generation,
    reachy_llama_generation_event * event,
    char * text_utf8,
    size_t text_capacity,
    size_t * required_bytes,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_generation_cancel(
    reachy_llama_generation_handle generation,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_generation_get_metrics(
    reachy_llama_generation_handle generation,
    reachy_llama_generation_metrics * metrics,
    reachy_llama_error_info * error);
REACHY_LLAMA_API int32_t reachy_llama_generation_release(
    reachy_llama_generation_handle generation,
    reachy_llama_error_info * error);

#ifdef __cplusplus
}
#endif

#endif
