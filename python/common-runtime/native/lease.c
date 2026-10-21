#include "adapter.h"
#include <pythread.h>
#include <stdlib.h>

static const char capsule_name[] = "sharpbind.tensor_transfer";
/* Native finalizers only enqueue. Python cleanup always runs with the GIL held. */
static PyThread_type_lock owner_lock;
static PendingOwner *pending;
static int stopping, scheduled;
static long live_owners;

int leases_initialize(void) {
    if (!owner_lock) {
        owner_lock = PyThread_allocate_lock();
        if (!owner_lock) {
            PyErr_NoMemory();
            return -1;
        }
    }
    return 0;
}

void owners_collect(void) {
    PyObject *error_type, *error_value, *traceback;
    /* Collection can run while a conversion error is unwinding. Preserve that error
     * across buffer releases and producer deleters invoked by Py_DECREF. */
    PyErr_Fetch(&error_type, &error_value, &traceback);
    for (;;) {
        PyThread_acquire_lock(owner_lock, WAIT_LOCK);
        PendingOwner *owners = pending;
        pending = NULL;
        PyThread_release_lock(owner_lock);
        /* Destructors may enqueue more owners. Run them outside owner_lock and keep
         * draining until no newly retired owners remain. */
        if (!owners) break;

        while (owners) {
            PendingOwner *next = owners->next;
            Py_DECREF(owners->memory);
            free(owners);
            live_owners--;
            owners = next;
        }
    }
    PyErr_Restore(error_type, error_value, traceback);
}

static int pending_collect(void *unused) {
    PyThread_acquire_lock(owner_lock, WAIT_LOCK);
    scheduled = 0;
    PyThread_release_lock(owner_lock);
    owners_collect();
    return 0;
}

PendingOwner *owner_allocate(PyObject *memory, size_t size) {
    if (stopping) {
        PyErr_SetString(PyExc_RuntimeError, "Tensor owners are shutting down");
        return NULL;
    }

    PendingOwner *owner = calloc(1, size);
    if (!owner) {
        PyErr_NoMemory();
        return NULL;
    }

    owner->memory = Py_NewRef(memory);
    live_owners++;
    return owner;
}

void owner_release(void *context) {
    PendingOwner *owner = context;
    PyThread_acquire_lock(owner_lock, WAIT_LOCK);
    owner->next = pending;
    pending = owner;

    /* Scheduling is best effort: if CPython's pending-call queue is full, keep the
     * owner queued for an explicit collect at a Python call boundary. Never wait for the GIL. */
    if (!stopping && !scheduled && Py_AddPendingCall(pending_collect, NULL) == 0) scheduled = 1;
    PyThread_release_lock(owner_lock);
}

static void capsule_destroy(PyObject *capsule) {
    Descriptor *descriptor = PyCapsule_GetPointer(capsule, capsule_name);
    if (descriptor) {
        if (descriptor->lease) descriptor->release(descriptor->lease);
        free(descriptor);
        owners_collect();
    }
}

PyObject *buffer_transfer(PyObject *module, PyObject *object) {
    int valid = PyObject_IsInstance(object, memory_type);

    if (valid < 0) return NULL;
    if (!valid) {
        PyErr_SetString(PyExc_TypeError, "A tensor transfer requires an acquired owner");
        return NULL;
    }

    Descriptor *descriptor = malloc(sizeof(Descriptor));
    if (!descriptor) return PyErr_NoMemory();

    PendingOwner *owner = owner_allocate(object, sizeof(PendingOwner));
    if (!owner) {
        free(descriptor);
        return NULL;
    }

    *descriptor = ((Memory *)object)->descriptor;
    descriptor->lease = owner;
    descriptor->release = owner_release;
    PyObject *capsule = PyCapsule_New(descriptor, capsule_name, capsule_destroy);
    if (!capsule) {
        owner_release(owner);
        owners_collect();
        free(descriptor);
    }
    return capsule;
}

PyObject *transfer_address(PyObject *module, PyObject *capsule) {
    Descriptor *descriptor = PyCapsule_GetPointer(capsule, capsule_name);
    return descriptor ? PyLong_FromVoidPtr(descriptor) : NULL;
}

PyObject *lease_collect(PyObject *module, PyObject *unused) {
    owners_collect();
    Py_RETURN_NONE;
}

PyObject *lease_shutdown(PyObject *module, PyObject *unused) {
    PyThread_acquire_lock(owner_lock, WAIT_LOCK);
    stopping = 1;
    PyThread_release_lock(owner_lock);
    owners_collect();
    /* The lock and queue outlive late managed finalizers. Never reenter a finalized interpreter. */
    Py_RETURN_NONE;
}

PyObject *lease_counters(PyObject *module, PyObject *unused) {
    owners_collect();
    return PyLong_FromLong(live_owners);
}
