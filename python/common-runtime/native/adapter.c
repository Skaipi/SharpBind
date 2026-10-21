#include "adapter.h"
#include <stddef.h>

PyObject *memory_type;

static PyObject *adapter_counters(PyObject *module, PyObject *unused) {
    return PyLong_FromLong(live_memory_count);
}

static PyObject *adapter_descriptor_layout(PyObject *module, PyObject *unused) {
    /* Build tuple of integers */
    return Py_BuildValue("(iiiiiiiiiiii)", (int)sizeof(Descriptor), (int)offsetof(Descriptor, size),
                         (int)offsetof(Descriptor, data), (int)offsetof(Descriptor, dtype),
                         (int)offsetof(Descriptor, rank), (int)offsetof(Descriptor, shape),
                         (int)offsetof(Descriptor, strides), (int)offsetof(Descriptor, device_type),
                         (int)offsetof(Descriptor, device_id), (int)offsetof(Descriptor, flags),
                         (int)offsetof(Descriptor, lease), (int)offsetof(Descriptor, release));
}

static PyMethodDef memory_methods[] = {
    {"descriptor_address", (PyCFunction)memory_descriptor_address, METH_NOARGS,
     "Address of a call-scoped descriptor."},
    {NULL},
};

static PyType_Slot memory_slots[] = {
    {Py_tp_new, memory_reject_new},
    {Py_tp_dealloc, memory_dealloc},
    {Py_tp_traverse, memory_traverse},
    {Py_tp_clear, memory_clear},
    {Py_tp_methods, memory_methods},
    {Py_bf_getbuffer, memory_getbuffer},
    {0, NULL}, /* List sentinel */
};

static PyType_Spec memory_spec = {
    SHARPBIND_MODULE ".Memory",      sizeof(Memory), 0,
    Py_TPFLAGS_DEFAULT | Py_TPFLAGS_HAVE_GC, memory_slots,
};

/* Methods available in module */
static PyMethodDef adapter_methods[] = {
    {"descriptor_layout", adapter_descriptor_layout, METH_NOARGS,
     "Native descriptor size and field offsets for ABI conformance checks."},
    {"borrow", buffer_borrow, METH_VARARGS, "Acquire a Python buffer for a synchronous call."},
    {"adopt", memory_adopt, METH_VARARGS, "Consume one native ownership lease."},
    {"counters", adapter_counters, METH_NOARGS, "Live buffer and native allocation owners."},
    {"transfer", buffer_transfer, METH_O, "Transfer one acquired reference into managed code."},
    {"transfer_address", transfer_address, METH_O, "Address of an owned input descriptor."},
    {"from_dlpack", dlpack_import, METH_VARARGS, "Consume one CPU DLPack capsule."},
    {"collect_leases", lease_collect, METH_NOARGS, "Release retired buffers with the GIL held."},
    {"shutdown_leases", lease_shutdown, METH_NOARGS,
     "Disable callbacks before interpreter shutdown."},
    {"lease_counters", lease_counters, METH_NOARGS,
     "Live references transferred to managed code."},
    {NULL},
};

/* Module construction */
static struct PyModuleDef adapter_module = {
    PyModuleDef_HEAD_INIT, SHARPBIND_MODULE, "CPU tensor ownership and DLPack adapter", -1,
    adapter_methods,
};

PyMODINIT_FUNC PyInit__buffer(void) {
    if (leases_initialize() < 0) {
        return NULL;
    }

    PyObject *module = PyModule_Create(&adapter_module);
    if (!module) {
        return NULL;
    }

    memory_type = PyType_FromSpec(&memory_spec);
    if (!memory_type || PyModule_AddObject(module, "Memory", memory_type) < 0) {
        Py_XDECREF(memory_type);
        Py_DECREF(module);
        return NULL;
    }

    return module;
}
