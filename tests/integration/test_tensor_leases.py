"""Retained zero-copy inputs through the installed native binding."""

import array
import ctypes as ct
import gc
import unittest
import weakref

import numpy as np
import sharpbind_fixture as fixture
from sharpbind_fixture._sharpbind import _buffer
from sharpbind_fixture._sharpbind._abi import Descriptor
from _support import PROVIDER, native, run_scenario


class TensorLeaseTests(unittest.TestCase):
    def setUp(self):
        fixture.LeaseTests.collect()
        self.owners = _buffer.counters()
        self.leases = _buffer.lease_counters()

    def tearDown(self):
        fixture.LeaseTests.collect()
        gc.collect()
        _buffer.collect_leases()
        self.assertEqual(_buffer.lease_counters(), self.leases)
        self.assertEqual(_buffer.counters(), self.owners)

    def test_retained_owner_survives_python_array_collection(self):
        active, released = fixture.Tensors.active(), fixture.Tensors.released()
        tensor = fixture.Tensors.create(4)
        address = fixture.Tensors.address(tensor)
        first = fixture.RetainedDataset(tensor)
        second = fixture.RetainedDataset(tensor)
        try:
            del tensor
            self.assertEqual(first.address(), address)
            self.assertEqual(first.sum(), 6)
            first.increment()
            self.assertEqual(second.sum_contiguous(), 10)
            first.close()
            first.close()
            self.assertEqual(second.sum(), 10)
            self.assertEqual(fixture.Tensors.active(), active + 1)
        finally:
            first.close()
            second.close()
        self.assertEqual(fixture.Tensors.active(), active)
        self.assertEqual(fixture.Tensors.released(), released + 1)

    def test_numpy_owner_is_kept_alive_without_copy(self):
        values = np.arange(6, dtype=np.float64)
        owner = weakref.ref(values)
        with fixture.RetainedDataset(values) as dataset:
            self.assertEqual(dataset.address(), values.ctypes.data)
            values[0] = 10
            self.assertEqual(dataset.sum(), 25)
            dataset.increment()
            self.assertEqual(values[0], 11)
            del values
            gc.collect()
            self.assertIsNotNone(owner())
            self.assertEqual(dataset.sum(), 31)
        gc.collect()
        self.assertIsNone(owner())

    def test_strided_reversed_scalar_empty_and_readonly_inputs(self):
        source = np.arange(12, dtype=np.float64).reshape(3, 4)
        for values in (
            source[:, ::2],
            source[::-1, ::-1],
            source.T,
            source[:0],
            np.array(7.0),
        ):
            with self.subTest(shape=values.shape, strides=values.strides):
                with fixture.RetainedDataset(values) as dataset:
                    self.assertEqual(dataset.sum(), values.sum())
                    np.testing.assert_array_equal(dataset.shape(), values.shape)
                    np.testing.assert_array_equal(dataset.strides(), values.strides)
                    if values.size and not values.flags.c_contiguous:
                        with self.assertRaises(RuntimeError):
                            dataset.sum_contiguous()
                        with self.assertRaises(RuntimeError):
                            dataset.increment()
        source.flags.writeable = False
        with fixture.RetainedDataset(source) as dataset:
            self.assertEqual(dataset.sum(), source.sum())
            with self.assertRaisesRegex(RuntimeError, "Read-only"):
                dataset.increment()

    def test_export_is_held_until_disposal(self):
        values = array.array("d", [1, 2, 3])
        with fixture.RetainedDataset(values) as dataset:
            with self.assertRaises(BufferError):
                values.append(4)
            self.assertEqual(dataset.sum(), 6)
        values.append(4)
        self.assertEqual(list(values), [1, 2, 3, 4])

    def test_call_handle_is_disposed_unless_retained(self):
        fixture.LeaseTests.save_without_retain(np.arange(4, dtype=np.float64))
        self.assertTrue(fixture.LeaseTests.saved_is_disposed())
        self.assertEqual(_buffer.lease_counters(), self.leases)

    def test_exception_and_later_argument_conversion_release_inputs(self):
        retained_errors = []
        values = np.arange(4, dtype=np.float64)
        for second in (values, values.astype(np.float32)):
            try:
                fixture.LeaseTests.fail(values, second)
            except (RuntimeError, BufferError) as error:
                retained_errors.append(error)
            else:
                self.fail("Expected argument or managed failure")
            self.assertEqual(_buffer.lease_counters(), self.leases)
            self.assertEqual(_buffer.counters(), self.owners)

    def test_later_native_validation_failure_releases_first_acquisition(self):
        values = np.arange(4, dtype=np.float64)
        memory = _buffer.borrow(values, False, 10)
        capsule = _buffer.transfer(memory)
        descriptor = Descriptor.from_address(_buffer.transfer_address(capsule))
        invalid = Descriptor.from_buffer_copy(descriptor)
        invalid.dtype = 1
        with self.assertRaises(ValueError):
            PROVIDER._call(
                native("LeaseTests", "fail"), ct.byref(descriptor), ct.byref(invalid)
            )
        del descriptor, invalid, capsule, memory
        _buffer.collect_leases()
        self.assertEqual(_buffer.lease_counters(), self.leases)

    def test_disposal_on_managed_worker_and_finalizer_threads(self):
        values = array.array("d", [1, 2, 3])
        with fixture.RetainedDataset(values) as dataset:
            dataset.release_on_worker()
            values.append(4)
            with self.assertRaises(RuntimeError):
                dataset.sum()
        fixture.LeaseTests.abandon(values)
        fixture.LeaseTests.collect()
        values.append(5)

    def test_wrong_dtype_and_invalid_inputs_are_rejected(self):
        for value in (None, np.arange(4, dtype=np.float32), np.arange(4, dtype=">f8")):
            with self.assertRaises((TypeError, BufferError)):
                fixture.RetainedDataset(value)

    def test_shutdown_with_live_leases(self):
        run_scenario("shutdown_with_live_leases")
