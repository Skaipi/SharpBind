#include "adapter.h"

long live_memory_count = 0;

static void memory_release(Memory *memory) {
    Descriptor *descriptor = &memory->descriptor;
    if (descriptor->lease && descriptor->release) {
        /* Detach first so reentrant cleanup cannot release the same lease twice. */
        void *lease = descriptor->lease;
        descriptor->lease = NULL;
        descriptor->release(lease);
    }
    if (memory->borrowed_buffer.obj) {
        PyBuffer_Release(&memory->borrowed_buffer);
    }
    if (memory->dlpack) {
        void *tensor = memory->dlpack;
        memory->dlpack = NULL;
        if (memory->dlpack_versioned) {
            DLManagedTensorVersioned *managed = tensor;
            if (managed->deleter) managed->deleter(managed);
        } else {
            DLManagedTensor *managed = tensor;
            if (managed->deleter) managed->deleter(managed);
        }
    }

    /* The provider must outlive the call to its native release function. */
    Py_CLEAR(memory->native_owner);
}

int memory_traverse(Memory *memory, visitproc visit, void *arg) {
    Py_VISIT(memory->native_owner);
    Py_VISIT(memory->borrowed_buffer.obj);
    return 0;
}

int memory_clear(Memory *memory) {
    memory_release(memory);
    return 0;
}

void memory_dealloc(Memory *memory) {
    PyObject_GC_UnTrack((PyObject *)memory);
    memory_release(memory);
    live_memory_count--;

    PyTypeObject *type = Py_TYPE((PyObject *)memory);
    freefunc free_memory = (freefunc)PyType_GetSlot(type, Py_tp_free);
    free_memory((PyObject *)memory);
    Py_DECREF(type);
}

Memory *memory_allocate(void) {
    Memory *memory = (Memory *)PyType_GenericAlloc((PyTypeObject *)memory_type, 0);
    if (!memory) {
        return NULL;
    }

    memory->descriptor.size = sizeof(Descriptor);
    memory->descriptor.device_type = DEVICE_TYPE_CPU;
    live_memory_count++;
    return memory;
}

PyObject *memory_reject_new(PyTypeObject *type, PyObject *args, PyObject *kwargs) {
    PyErr_SetString(PyExc_TypeError,
                    "Adapter owners are created only by buffer or native-lease acquisition");
    return NULL;
}

PyObject *memory_descriptor_address(Memory *memory, PyObject *unused) {
    return PyLong_FromVoidPtr(&memory->descriptor);
}

PyObject *memory_adopt(PyObject *module, PyObject *args) {
    PyObject *address;
    PyObject *native_owner;
    if (!PyArg_ParseTuple(args, "OO", &address, &native_owner)) {
        return NULL;
    }

    Descriptor *source = PyLong_AsVoidPtr(address);
    if (PyErr_Occurred()) {
        return NULL;
    }
    if (!source) {
        buffer_error("Missing descriptor");
        return NULL;
    }

    Memory *memory = memory_allocate();
    if (!memory) {
        /* Adoption consumes the lease even when no Python owner can be allocated. */
        if (source->lease && source->release) {
            source->release(source->lease);
        }
        source->lease = NULL;
        source->release = NULL;
        return NULL;
    }

    /* Transfer before validation: all later failures release through memory_dealloc. */
    memory->descriptor = *source;
    source->lease = NULL;
    source->release = NULL;
    memory->native_owner = Py_NewRef(native_owner);

    if (!memory->descriptor.lease || !memory->descriptor.release) {
        buffer_error("Owned result requires a native lease and release function");
        goto error;
    }
    if (memory_validate_descriptor(memory, 0) < 0) {
        goto error;
    }
    return (PyObject *)memory;

error:
    Py_DECREF(memory);
    return NULL;
}
