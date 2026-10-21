/* Native ABI shared by the CPU adapter and emitted bindings.h files.
 * Keep layouts aligned with SharpBind.Interop and python/common-runtime/src/sharpbind_runtime/_abi.py.
 * The managed generator embeds this header directly; there is no template copy.
 */
#pragma once
#include <stdint.h>

/* An object token is valid only within the provider that created it. */
typedef struct {
    uint64_t provider;
    uint64_t token;
} sb_handle;

/* The caller initializes size and zeros the other fields.
 * The lease owns both UTF-8 strings until sharpbind_error_release is called.
 */
typedef struct {
    uint32_t size;
    uint32_t category;
    const uint8_t *managed_type;
    const uint8_t *message;
    void *lease;
} sb_error;

/* Shape entries are lengths; strides are signed byte distances.
 * Owned input slots are cleared on successful adoption. Release callbacks must
 * be thread-safe, remain loaded while leases exist, and never throw.
 */
typedef struct {
    uint32_t size;
    void *data;
    int32_t dtype;
    int32_t rank;
    const int64_t *shape;
    const int64_t *strides;
    int32_t device_type;
    int32_t device_id;
    uint64_t flags;
    void *lease;
    void (*release)(void *);
} sb_tensor;
