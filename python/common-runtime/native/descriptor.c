#include "adapter.h"

int buffer_error(const char *message) {
    PyErr_SetString(PyExc_BufferError, message);
    return -1;
}

static int validate_descriptor_header(const Descriptor *descriptor, int require_writable) {
    if (descriptor->size != sizeof(Descriptor)) {
        return buffer_error("Invalid descriptor size");
    }
    if (descriptor->device_type != DEVICE_TYPE_CPU || descriptor->device_id != 0) {
        return buffer_error("Only CPU device 0 is supported");
    }
    if (descriptor->dtype < DTYPE_INT8 || descriptor->dtype > DTYPE_FLOAT64 ||
        descriptor->rank < 0 || descriptor->rank > TENSOR_MAX_RANK) {
        return buffer_error("Invalid dtype/rank");
    }
    if ((descriptor->flags & ~DESCRIPTOR_WRITABLE) ||
        (require_writable && !(descriptor->flags & DESCRIPTOR_WRITABLE))) {
        return buffer_error("Read-only or unsupported access flags");
    }
    if (descriptor->rank && (!descriptor->shape || !descriptor->strides)) {
        return buffer_error("Missing shape/strides");
    }
    return 0;
}

static void memory_copy_metadata(Memory *memory, Py_ssize_t element_count) {
    Descriptor *descriptor = &memory->descriptor;
    const DtypeInfo *dtype = &dtype_info[descriptor->dtype];

    for (int axis = 0; axis < descriptor->rank; ++axis) {
        memory->shape[axis] = descriptor->shape[axis];
        memory->strides[axis] = descriptor->strides[axis];
        memory->buffer_shape[axis] = (Py_ssize_t)descriptor->shape[axis];
        memory->buffer_strides[axis] = (Py_ssize_t)descriptor->strides[axis];
    }
    memory->element_count = element_count;
    memory->item_size = dtype->item_size;
    memory->format[0] = dtype->buffer_format;
    memory->format[1] = '\0';

    /* The descriptor and Python views now use metadata held by their shared owner. */
    descriptor->shape = memory->shape;
    descriptor->strides = memory->strides;
}

/* Validate metadata before any consumer can dereference element storage. */
int memory_validate_descriptor(Memory *memory, int require_writable) {
    const Descriptor *descriptor = &memory->descriptor;
    if (validate_descriptor_header(descriptor, require_writable) < 0) {
        return -1;
    }

    /* An empty axis makes the entire tensor empty, regardless of the other dimensions. */
    int is_empty = 0;
    for (int axis = 0; axis < descriptor->rank; ++axis) {
        if (descriptor->shape[axis] < 0) {
            return buffer_error("Negative dimension");
        }
        if (descriptor->shape[axis] == 0) {
            is_empty = 1;
        }
    }

    int item_size = dtype_info[descriptor->dtype].item_size;
    int64_t element_count = is_empty ? 0 : 1;
    int64_t contiguous_stride = item_size;
    uint64_t bytes_before_data = 0;
    uint64_t bytes_after_data = 0;

    /* Walk from the innermost axis to check C-order writable layouts. Read-only views
     * may have negative or zero strides, so track their address span in both directions. */
    for (int axis = descriptor->rank - 1; axis >= 0; --axis) {
        int64_t dimension = descriptor->shape[axis];
        int64_t stride = descriptor->strides[axis];
        if (stride == INT64_MIN || stride % item_size) {
            return buffer_error("Unaligned or overflowing stride");
        }
        if (is_empty) {
            continue;
        }

        if (dimension && element_count > PY_SSIZE_T_MAX / dimension) {
            return buffer_error("Dimension product overflows");
        }
        element_count *= dimension;
        if (require_writable && dimension > 1 && stride != contiguous_stride) {
            return buffer_error("Writable views must be contiguous and non-overlapping");
        }

        uint64_t stride_magnitude = (uint64_t)(stride < 0 ? -stride : stride);
        if (dimension > 1 && stride_magnitude > INT64_MAX / (uint64_t)(dimension - 1)) {
            return buffer_error("Stride span overflows");
        }
        uint64_t axis_span = (uint64_t)(dimension - 1) * stride_magnitude;
        uint64_t *address_span = stride < 0 ? &bytes_before_data : &bytes_after_data;
        if (*address_span > INT64_MAX - axis_span) {
            return buffer_error("Address span overflows");
        }
        *address_span += axis_span;

        if (dimension && contiguous_stride > INT64_MAX / dimension) {
            return buffer_error("Byte size overflows");
        }
        contiguous_stride *= dimension;
    }

    if (element_count > PY_SSIZE_T_MAX / item_size) {
        return buffer_error("Buffer byte size overflows");
    }

    /* data points at logical element zero; include the final element's bytes in the span. */
    uintptr_t data_address = (uintptr_t)descriptor->data;
    if (element_count &&
        (data_address % item_size || !data_address || data_address < bytes_before_data ||
         bytes_after_data > UINTPTR_MAX - data_address - (item_size - 1))) {
        return buffer_error("Missing, unaligned, or overflowing buffer address");
    }

    memory_copy_metadata(memory, (Py_ssize_t)element_count);
    return 0;
}
