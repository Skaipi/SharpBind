/* DLPack 1.0 ABI layouts used by this CPU adapter.
 * Field layouts and ownership contract: https://dmlc.github.io/dlpack/latest/
 * We accept legacy and versioned capsules; device kernels and the C exchange API are out of scope.
 */
#ifndef SHARPBIND_DLPACK_H
#define SHARPBIND_DLPACK_H
#include <stdint.h>

typedef struct { int32_t device_type; int32_t device_id; } DLDevice;
typedef struct { uint8_t code; uint8_t bits; uint16_t lanes; } DLDataType;
typedef struct {
    void *data;
    DLDevice device;
    int32_t ndim;
    DLDataType dtype;
    int64_t *shape;
    int64_t *strides;
    uint64_t byte_offset;
} DLTensor;

typedef struct DLManagedTensor {
    DLTensor dl_tensor;
    void *manager_ctx;
    void (*deleter)(struct DLManagedTensor *);
} DLManagedTensor;

typedef struct DLManagedTensorVersioned {
    struct { uint32_t major; uint32_t minor; } version;
    void *manager_ctx;
    void (*deleter)(struct DLManagedTensorVersioned *);
    uint64_t flags;
    DLTensor dl_tensor;
} DLManagedTensorVersioned;

#define DLPACK_READ_ONLY UINT64_C(1)
#define DLPACK_IS_COPIED UINT64_C(2)
#endif
