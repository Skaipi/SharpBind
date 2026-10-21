#include "adapter.h"
#include <string.h>

/* Export a view of the owned storage; only metadata is copied. */
int memory_getbuffer(Memory *memory, Py_buffer *view, int flags) {
    if ((flags & PyBUF_WRITABLE) && !(memory->descriptor.flags & DESCRIPTOR_WRITABLE)) {
        return buffer_error("Storage is read-only");
    }

    Py_buffer full_view = {0};
    full_view.buf = memory->descriptor.data;
    full_view.len = memory->element_count * memory->item_size;
    full_view.readonly = !(memory->descriptor.flags & DESCRIPTOR_WRITABLE);
    full_view.itemsize = memory->item_size;
    full_view.ndim = memory->descriptor.rank;
    full_view.format = memory->format;
    full_view.shape = memory->descriptor.rank ? memory->buffer_shape : NULL;
    full_view.strides = memory->descriptor.rank ? memory->buffer_strides : NULL;

    /* PyBUF_* layout flags. A consumer without strides also requires C-contiguous storage, even if
     * it did not explicitly request it. */
    int requests_strides = (flags & PyBUF_STRIDES) == PyBUF_STRIDES;
    int requires_c_contiguous = (flags & PyBUF_C_CONTIGUOUS) == PyBUF_C_CONTIGUOUS;
    if ((requires_c_contiguous || !requests_strides) && !PyBuffer_IsContiguous(&full_view, 'C')) {
        return buffer_error("C-contiguous storage required");
    }
    if ((flags & PyBUF_F_CONTIGUOUS) == PyBUF_F_CONTIGUOUS &&
        !PyBuffer_IsContiguous(&full_view, 'F')) {
        return buffer_error("F-contiguous storage required");
    }
    if ((flags & PyBUF_ANY_CONTIGUOUS) == PyBUF_ANY_CONTIGUOUS &&
        !PyBuffer_IsContiguous(&full_view, 'A')) {
        return buffer_error("Contiguous storage required");
    }

    /* Retain the owner only after the request has passed every check. */
    *view = full_view;
    view->obj = Py_NewRef((PyObject *)memory);
    if (!(flags & PyBUF_FORMAT)) {
        view->format = NULL;
    }
    if (!requests_strides) {
        view->strides = NULL;
    }
    if (!(flags & PyBUF_ND)) {
        view->shape = NULL;
        view->ndim = 1;
        view->itemsize = 1;
    }
    return 0;
}

static TensorDtype dtype_from_buffer_format(const char *format, Py_ssize_t item_size) {
    if (!format) {
        return DTYPE_INVALID;
    }

    /* Supported targets are little-endian. We assume host is little-endian
        @ - native endian; C alignment
        = - native endian; no alignment
        < - little-endian
        > - big-endian
        ! - network-byte order
    */
    if (*format == '@' || *format == '=' || *format == '<') {
        format++;
    }
    if (strlen(format) != 1) {
        return DTYPE_INVALID;
    }

    for (int dtype = DTYPE_INT8; dtype <= DTYPE_FLOAT64; ++dtype) {
        if (dtype_info[dtype].buffer_format == *format &&
            dtype_info[dtype].item_size == item_size) {
            return (TensorDtype)dtype;
        }
    }

    /* C long has a platform-dependent width; the descriptor uses fixed-width codes. */
    if (*format == 'l') {
        if (item_size == 4) {
            return DTYPE_INT32;
        }
        if (item_size == 8) {
            return DTYPE_INT64;
        }
    }
    if (*format == 'L') {
        if (item_size == 4) {
            return DTYPE_UINT32;
        }
        if (item_size == 8) {
            return DTYPE_UINT64;
        }
    }
    return DTYPE_INVALID;
}

static int memory_describe_buffer(Memory *memory) {
    const Py_buffer *buffer = &memory->borrowed_buffer;
    Descriptor *descriptor = &memory->descriptor;
    descriptor->data = buffer->buf;
    descriptor->rank = buffer->ndim;
    descriptor->dtype = dtype_from_buffer_format(buffer->format, buffer->itemsize);
    descriptor->flags = buffer->readonly ? 0 : DESCRIPTOR_WRITABLE;

    if (buffer->suboffsets || buffer->ndim < 0 || buffer->ndim > TENSOR_MAX_RANK ||
        (buffer->ndim && !buffer->shape)) {
        return buffer_error("Indirect buffers or invalid rank unsupported");
    }

    /* A buffer without explicit strides is C-contiguous. Synthesize its byte strides from the
     * innermost axis */
    int64_t contiguous_stride = buffer->itemsize;
    for (int axis = buffer->ndim - 1; axis >= 0; --axis) {
        int64_t dimension = buffer->shape[axis];
        memory->shape[axis] = dimension;
        memory->strides[axis] = buffer->strides ? buffer->strides[axis] : contiguous_stride;
        if (dimension < 0 ||
            (!buffer->strides && dimension && contiguous_stride > INT64_MAX / dimension)) {
            return buffer_error("Shape overflows");
        }
        if (!buffer->strides) {
            contiguous_stride *= dimension;
        }
    }
    descriptor->shape = memory->shape;
    descriptor->strides = memory->strides;
    return 0;
}

PyObject *buffer_borrow(PyObject *module, PyObject *args) {
    PyObject *object;
    int require_writable;
    int expected_dtype;
    if (!PyArg_ParseTuple(args, "Oii", &object, &require_writable, &expected_dtype)) {
        return NULL;
    }

    Memory *memory = memory_allocate();
    if (!memory) {
        return NULL;
    }

    int flags = PyBUF_STRIDES | PyBUF_FORMAT;
    if (require_writable) {
        flags |= PyBUF_WRITABLE;
    }
    if (PyObject_GetBuffer(object, &memory->borrowed_buffer, flags) < 0) {
        goto error;
    }
    if (memory_describe_buffer(memory) < 0) {
        goto error;
    }
    if (memory->descriptor.dtype == DTYPE_INVALID ||
        (expected_dtype && memory->descriptor.dtype != expected_dtype)) {
        buffer_error("Exact native dtype required for sharing");
        goto error;
    }
    if (memory_validate_descriptor(memory, require_writable) < 0) {
        goto error;
    }
    return (PyObject *)memory;

error:
    /* Deallocation releases any buffer acquired before fail. */
    Py_DECREF(memory);
    return NULL;
}
