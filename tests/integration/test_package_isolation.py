"""Independent bundled adapters interoperate through standard array protocols."""

import gc
import unittest

import numpy as np
import sharpbind_fixture as first
import sharpbind_peer as second
from sharpbind_fixture._sharpbind import _buffer as first_adapter
from sharpbind_peer._sharpbind import _buffer as second_adapter


class PackageIsolationTests(unittest.TestCase):
    def tearDown(self):
        gc.collect()
        first_adapter.collect_leases()
        second_adapter.collect_leases()

    def test_private_adapters_and_native_types_are_independent(self):
        self.assertIsNot(first_adapter, second_adapter)
        self.assertIsNot(first_adapter.Memory, second_adapter.Memory)
        self.assertEqual(
            first_adapter.Memory.__module__, "sharpbind_fixture._sharpbind._buffer"
        )
        self.assertEqual(
            second_adapter.Memory.__module__, "sharpbind_peer._sharpbind._buffer"
        )

    def test_array_from_one_package_can_be_retained_by_another(self):
        active, released = first.Tensors.active(), first.Tensors.released()
        array = first.Tensors.create(4)
        address = array.ctypes.data
        with second.RetainedDataset(array) as dataset:
            self.assertEqual(dataset.address(), address)
            dataset.increment()
            np.testing.assert_array_equal(array, [1, 2, 3, 4])
            del array
            gc.collect()
            self.assertEqual(first.Tensors.active(), active + 1)
            self.assertEqual(dataset.sum(), 10)
        first_adapter.collect_leases()
        second_adapter.collect_leases()
        gc.collect()
        self.assertEqual(first.Tensors.active(), active)
        self.assertEqual(first.Tensors.released(), released + 1)

    def test_each_adapter_releases_only_its_own_allocations(self):
        baseline = first_adapter.counters(), second_adapter.counters()
        left = first.Tensors.create(3)
        right = second.Tensors.create(5)
        self.assertEqual(first_adapter.counters(), baseline[0] + 1)
        self.assertEqual(second_adapter.counters(), baseline[1] + 1)
        del left
        gc.collect()
        self.assertEqual(first_adapter.counters(), baseline[0])
        self.assertEqual(second_adapter.counters(), baseline[1] + 1)
        np.testing.assert_array_equal(right, np.arange(5))
        del right
