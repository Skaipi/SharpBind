/* Internal CPU buffer adapter. All strides are measured in bytes. */
#ifndef SHARPBIND_ADAPTER_H
#define SHARPBIND_ADAPTER_H

#define Py_LIMITED_API 0x030B0000
#define PY_SSIZE_T_CLEAN
#include <Python.h>
#include <stdint.h>
#include "dlpack.h"

#include "abi.h"
#include "dtypes.h"

typedef sb_tensor Descriptor;
#define DESCRIPTOR_WRITABLE UINT64_C(1)

/* Owns either one acquired Python buffer or one native lease. */
/* Native owner keeps the provider loaded until the native lease is released. */
typedef struct {
    PyObject_HEAD
    Descriptor descriptor;
    int64_t shape[TENSOR_MAX_RANK];
    int64_t strides[TENSOR_MAX_RANK];
    Py_ssize_t buffer_shape[TENSOR_MAX_RANK];
    Py_ssize_t buffer_strides[TENSOR_MAX_RANK];
    Py_ssize_t element_count;
    Py_ssize_t item_size;
    char format[2];
    PyObject *native_owner;
    Py_buffer borrowed_buffer;
    void *dlpack;
    int dlpack_versioned;
} Memory;

/* A transferred reference, retired without touching Python on native threads. */
typedef struct PendingOwner {
    PyObject *memory;
    struct PendingOwner *next;
} PendingOwner;

PendingOwner *owner_allocate(PyObject *memory, size_t size);
void owner_release(void *owner);
void owners_collect(void);

extern PyObject *memory_type;
extern long live_memory_count;

/* Validation returns 0 on success, or -1 with a Python exception set. */
int buffer_error(const char *message);
int memory_validate_descriptor(Memory *memory, int require_writable);

/* Python owner allocation and garbage-collector callbacks. */
Memory *memory_allocate(void);
int memory_traverse(Memory *memory, visitproc visit, void *arg);
int memory_clear(Memory *memory);
void memory_dealloc(Memory *memory);
PyObject *memory_reject_new(PyTypeObject *type, PyObject *args, PyObject *kwargs);
PyObject *memory_descriptor_address(Memory *memory, PyObject *unused);
int memory_getbuffer(Memory *memory, Py_buffer *view, int flags);

/* Module entry points. Adoption consumes the source lease even on failure. */
PyObject *buffer_borrow(PyObject *module, PyObject *args);
PyObject *memory_adopt(PyObject *module, PyObject *args);

int leases_initialize(void);
PyObject *buffer_transfer(PyObject *module, PyObject *memory);
PyObject *transfer_address(PyObject *module, PyObject *capsule);
PyObject *dlpack_import(PyObject *module, PyObject *args);
PyObject *lease_collect(PyObject *module, PyObject *unused);
PyObject *lease_shutdown(PyObject *module, PyObject *unused);
PyObject *lease_counters(PyObject *module, PyObject *unused);

#endif
