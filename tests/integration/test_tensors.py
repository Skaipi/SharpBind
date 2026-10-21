"""Checks against the freshly installed fixture wheel."""

import ctypes as ct
import gc
from pathlib import Path
import subprocess
import sys
import unittest
import numpy as np
import sharpbind_fixture as fixture
from sharpbind_fixture._sharpbind import _buffer
from sharpbind_fixture._sharpbind._abi import Descriptor
from _support import run_scenario


class TensorTests(unittest.TestCase):
    def tearDown(self):
        gc.collect()

    def test_descriptor_layout_agrees_with_the_c_adapter(self):
        layout = (
            ct.sizeof(Descriptor),
            *(getattr(Descriptor, name).offset for name, _ in Descriptor._fields_),
        )
        self.assertEqual(layout, (72, 0, 8, 16, 20, 24, 32, 40, 44, 48, 56, 64))
        self.assertEqual(_buffer.descriptor_layout(), layout)

    def test_rank_zero_buffer_metadata(self):
        class Buffer(ct.Structure):
            _fields_ = [
                ("buf", ct.c_void_p),
                ("obj", ct.c_void_p),
                ("length", ct.c_ssize_t),
                ("itemsize", ct.c_ssize_t),
                ("readonly", ct.c_int),
                ("ndim", ct.c_int),
                ("format", ct.c_char_p),
                ("shape", ct.c_void_p),
                ("strides", ct.c_void_p),
                ("suboffsets", ct.c_void_p),
                ("internal", ct.c_void_p),
            ]

        acquire = ct.pythonapi.PyObject_GetBuffer
        acquire.argtypes, acquire.restype = [
            ct.py_object,
            ct.POINTER(Buffer),
            ct.c_int,
        ], ct.c_int

        release = ct.pythonapi.PyBuffer_Release
        release.argtypes, release.restype = [ct.POINTER(Buffer)], None
        owner = _buffer.borrow(np.array(2.0), False, 10)
        buffer = Buffer()
        acquire(owner, ct.byref(buffer), 0x11C)

        try:
            self.assertEqual(buffer.ndim, 0)
            self.assertIsNone(buffer.shape)
            self.assertIsNone(buffer.strides)
            self.assertIsNone(buffer.suboffsets)
            self.assertEqual(buffer.length, 8)
        finally:
            release(ct.byref(buffer))

    def test_tensor_dtype_ids_match_the_native_adapter(self):
        dtypes = (
            "int8",
            "uint8",
            "int16",
            "uint16",
            "int32",
            "uint32",
            "int64",
            "uint64",
            "float32",
            "float64",
        )
        for code, dtype in enumerate(dtypes, 1):
            with self.subTest(dtype=dtype):
                values = np.arange(4, dtype=dtype)
                memory = _buffer.borrow(values, False, code)
                descriptor = Descriptor.from_address(memory.descriptor_address())
                self.assertEqual(descriptor.dtype, code)

    def test_borrowed_pointer_identity_and_strides(self):
        a = np.arange(12, dtype=np.float64).reshape(3, 4)
        for view in (a, a[:, ::2], a[::-1, ::-1], a[:0], a.T, np.array(2.0)):
            self.assertEqual(fixture.Tensors.sum_view(view), view.sum())
            self.assertEqual(
                fixture.Tensors.address(view), view.ctypes.data if view.size else 0
            )

        readonly = a.copy()
        readonly.flags.writeable = False
        self.assertEqual(fixture.Tensors.sum_view(readonly), readonly.sum())
        with self.assertRaises(BufferError):
            fixture.Tensors.increment(readonly)
        with self.assertRaises(BufferError):
            fixture.Tensors.sum_view(a.astype(np.float32))
        with self.assertRaises(BufferError):
            fixture.Tensors.increment(a[:, ::2])
        with self.assertRaises(BufferError):
            fixture.Tensors.increment(np.broadcast_to(np.array(1.0), (4,)))
        with self.assertRaises(BufferError):
            fixture.Tensors.sum_view(a.astype(">f8"))

        fixture.Tensors.increment(a)
        np.testing.assert_array_equal(a.ravel(), np.arange(12) + 1)

    def test_contiguous_views_can_feed_managed_span_algorithms(self):
        values = np.arange(6, dtype=np.float64).reshape(2, 3)
        self.assertEqual(fixture.Tensors.sum_contiguous(values), 15)
        with self.assertRaisesRegex(RuntimeError, "strided view"):
            fixture.Tensors.sum_contiguous(values[:, ::2])

    def test_array_views_keep_native_storage_alive_and_release_once(self):
        before, released = fixture.Tensors.active(), fixture.Tensors.released()
        array = fixture.Tensors.create(4)
        self.assertIs(type(array), np.ndarray)
        first, second = array[:], array[::-1]
        self.assertEqual(first.ctypes.data, array.ctypes.data)
        self.assertEqual(fixture.Tensors.address(first), first.ctypes.data)
        first[0] = 77
        self.assertEqual(second[-1], 77)
        del array, first
        gc.collect()
        self.assertEqual(fixture.Tensors.active(), before + 1)
        self.assertEqual(second[-1], 77)
        del second
        gc.collect()
        self.assertEqual(fixture.Tensors.active(), before)
        self.assertEqual(fixture.Tensors.released(), released + 1)

    def test_readonly_and_copy_policy(self):
        view = fixture.Tensors.read_only()
        self.assertIs(type(view), np.ndarray)
        self.assertFalse(view.flags.writeable)
        with self.assertRaises(ValueError):
            view.flags.writeable = True
        independent = view.copy()
        independent[0] = 99
        self.assertEqual(view[0], 1)
        self.assertNotEqual(independent.ctypes.data, view.ctypes.data)

    def test_empty_array_releases_its_allocation(self):
        before = fixture.Tensors.active()
        array = fixture.Tensors.create(0)
        self.assertEqual(array.size, 0)
        del array
        gc.collect()
        self.assertEqual(fixture.Tensors.active(), before)

    def test_buffer_owner_cannot_resize_while_retained(self):
        values = np.arange(4, dtype=np.float64)
        with fixture.RetainedDataset(values):
            with self.assertRaises(ValueError):
                values.resize(8)

    def test_shutdown_with_live_views_in_subprocess(self):
        run_scenario("shutdown_with_live_views")

    def test_invalid_descriptors_in_isolated_process(self):
        script = Path(__file__).with_name("_descriptor_checks.py")
        subprocess.run([sys.executable, "-I", str(script)], check=True, timeout=30)
