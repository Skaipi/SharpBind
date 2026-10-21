#include "adapter.h"
#include <stddef.h>

static const char legacy_name[] = "dltensor";
static const char versioned_name[] = "dltensor_versioned";
static const char used_legacy_name[] = "used_dltensor";
static const char used_versioned_name[] = "used_dltensor_versioned";

static int describe_dlpack(Memory *memory, DLTensor *tensor, uint64_t flags, int writable,
                           int expected_dtype) {
    Descriptor *descriptor = &memory->descriptor;
    if (tensor->device.device_type != DEVICE_TYPE_CPU || tensor->device.device_id != 0)
        return buffer_error("Only CPU device 0 is supported");
    if (tensor->ndim < 0 || tensor->ndim > TENSOR_MAX_RANK || (tensor->ndim && !tensor->shape))
        return buffer_error("Invalid DLPack rank or shape");
    if (tensor->dtype.lanes != 1 || (flags & ~(DLPACK_READ_ONLY | DLPACK_IS_COPIED)))
        return buffer_error("Unsupported DLPack dtype or flags");
    if (flags & DLPACK_IS_COPIED)
        return buffer_error("DLPack producer copied despite a no-copy request");

    for (int code = DTYPE_INT8; code <= DTYPE_FLOAT64; code++) {
        if (dtype_info[code].dlpack_code == tensor->dtype.code &&
            dtype_info[code].item_size * 8 == tensor->dtype.bits) {
            descriptor->dtype = code;
            break;
        }
    }

    if (!descriptor->dtype || (expected_dtype && descriptor->dtype != expected_dtype))
        return buffer_error("Exact native dtype required for sharing");

    uintptr_t address = (uintptr_t)tensor->data;
    if (tensor->byte_offset > UINTPTR_MAX - address)
        return buffer_error("DLPack byte offset overflows");

    descriptor->data = (void *)(address + tensor->byte_offset);
    descriptor->rank = tensor->ndim;
    descriptor->flags = flags & DLPACK_READ_ONLY ? 0 : DESCRIPTOR_WRITABLE;

    int item_size = dtype_info[descriptor->dtype].item_size;
    int empty = 0;
    for (int axis = 0; axis < tensor->ndim; axis++) {
        if (tensor->shape[axis] < 0)
            return buffer_error("Negative DLPack dimension");
        empty |= tensor->shape[axis] == 0;
    }

    int64_t stride = empty ? 0 : item_size;
    /* DLPack strides count elements; the shared descriptor and Python buffer use
     * bytes. Normalize once here, including the implicit compact layout. */
    for (int axis = tensor->ndim - 1; axis >= 0; axis--) {
        int64_t dimension = tensor->shape[axis];
        memory->shape[axis] = dimension;
        if (tensor->strides) {
            int64_t element_stride = tensor->strides[axis];
            if (element_stride > INT64_MAX / item_size || element_stride < INT64_MIN / item_size)
                return buffer_error("DLPack stride overflows");
            memory->strides[axis] = element_stride * item_size;
        } else {
            memory->strides[axis] = stride;
            if (dimension && stride > INT64_MAX / dimension)
                return buffer_error("DLPack shape overflows");
            stride *= dimension;
        }
    }
    descriptor->shape = memory->shape;
    descriptor->strides = memory->strides;
    return memory_validate_descriptor(memory, writable);
}

PyObject *dlpack_import(PyObject *module, PyObject *args) {
    PyObject *capsule;
    int writable, expected_dtype;
    if (!PyArg_ParseTuple(args, "Oii", &capsule, &writable, &expected_dtype))
        return NULL;

    int versioned = PyCapsule_IsValid(capsule, versioned_name);
    if (!versioned && !PyCapsule_IsValid(capsule, legacy_name)) {
        PyErr_SetString(PyExc_ValueError, "Expected an unconsumed DLPack capsule");
        return NULL;
    }

    void *managed = PyCapsule_GetPointer(capsule, versioned ? versioned_name : legacy_name);
    if (!managed)
        return NULL;

    Memory *memory = memory_allocate();
    if (!memory)
        return NULL;

    /* Renaming commits ownership to memory: the capsule's destructor must no longer
     * call the producer deleter. Every subsequent failure releases through memory. */
    if (PyCapsule_SetName(capsule, versioned ? used_versioned_name : used_legacy_name) < 0) {
        Py_DECREF(memory);
        return NULL;
    }
    memory->dlpack = managed;
    memory->dlpack_versioned = versioned;

    DLTensor *tensor;
    uint64_t flags = 0;
    if (versioned) {
        DLManagedTensorVersioned *owner = managed;
        if (owner->version.major != 1) {
            buffer_error("Unsupported DLPack major version");
            Py_DECREF(memory);
            return NULL;
        }
        tensor = &owner->dl_tensor;
        flags = owner->flags;
    } else {
        tensor = &((DLManagedTensor *)managed)->dl_tensor;
    }
    if (describe_dlpack(memory, tensor, flags, writable, expected_dtype) < 0) {
        Py_DECREF(memory);
        return NULL;
    }
    return (PyObject *)memory;
}
