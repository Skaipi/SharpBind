/* Stable tensor element IDs and metadata used by the buffer and DLPack adapters.
 * Keep IDs aligned with TensorElement.cs and python/common-runtime/src/sharpbind_runtime/_abi.py.
 */
#pragma once

enum {
    TENSOR_MAX_RANK = 32,
    DEVICE_TYPE_CPU = 1
};

typedef enum {
    DTYPE_INVALID = 0,
    DTYPE_INT8 = 1,
    DTYPE_UINT8 = 2,
    DTYPE_INT16 = 3,
    DTYPE_UINT16 = 4,
    DTYPE_INT32 = 5,
    DTYPE_UINT32 = 6,
    DTYPE_INT64 = 7,
    DTYPE_UINT64 = 8,
    DTYPE_FLOAT32 = 9,
    DTYPE_FLOAT64 = 10,
} TensorDtype;

typedef struct {
    int item_size;
    char buffer_format;
    int dlpack_code; /* DLPack: signed integer = 0, unsigned integer = 1, float = 2. */
} DtypeInfo;

/* Indexed by TensorDtype. Entry zero is invalid; existing IDs must not move. */
static const DtypeInfo dtype_info[] = {
    {0, '\0', -1}, /* DTYPE_INVALID */
    {1, 'b', 0},   /* DTYPE_INT8 */
    {1, 'B', 1},   /* DTYPE_UINT8 */
    {2, 'h', 0},   /* DTYPE_INT16 */
    {2, 'H', 1},   /* DTYPE_UINT16 */
    {4, 'i', 0},   /* DTYPE_INT32 */
    {4, 'I', 1},   /* DTYPE_UINT32 */
    {8, 'q', 0},   /* DTYPE_INT64 */
    {8, 'Q', 1},   /* DTYPE_UINT64 */
    {4, 'f', 2},   /* DTYPE_FLOAT32 */
    {8, 'd', 2},   /* DTYPE_FLOAT64 */
};
