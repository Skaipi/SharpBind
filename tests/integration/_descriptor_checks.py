"""Malformed native descriptors must be rejected without leaking their owners.

Run in a subprocess: a validation regression can crash the interpreter.
"""

import ctypes as ct
import gc
import unittest

import numpy as np
from sharpbind_fixture import Tensors, _bindings
from sharpbind_fixture._sharpbind import _buffer
from sharpbind_fixture._sharpbind._abi import Descriptor

INVALID_DESCRIPTORS = (
    ("missing header", "size", 0),
    ("negative rank", "rank", -1),
    ("excessive rank", "rank", 33),
    ("unknown dtype", "dtype", 99),
    ("non-CPU device", "device_type", 2),
    ("nonzero device ID", "device_id", 1),
    ("unknown flags", "flags", 8),
    ("misaligned data", "data", 1),
    ("negative dimension", "shape", -1),
    ("overflowing dimension", "shape", 2**63 - 1),
    ("misaligned stride", "strides", 1),
    ("negative stride overflow", "strides", -(2**63)),
    ("positive stride overflow", "strides", 2**63 - 8),
)


def invalidate(descriptor, field, value):
    if field in ("shape", "strides"):
        getattr(descriptor, field)[0] = value
    else:
        setattr(descriptor, field, value)


class DescriptorValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.provider = _bindings._provider.native
        cls.provider._load()
        tensor_class = next(
            item for item in cls.provider.schema["classes"] if item["name"] == "Tensors"
        )
        cls.entries = {
            method["name"]: method["entry"] for method in tensor_class["methods"]
        }

    def call(self, name, *arguments):
        self.provider._call(getattr(self.provider.lib, self.entries[name]), *arguments)

    def test_native_calls_reject_each_invalid_borrowed_descriptor(self):
        for description, field, value in INVALID_DESCRIPTORS:
            with self.subTest(case=description):
                allocations = Tensors.active()
                array = np.arange(4, dtype=np.float64)
                owner = _buffer.borrow(array, False, 10)
                descriptor = Descriptor.from_address(owner.descriptor_address())
                invalidate(descriptor, field, value)
                result = ct.c_double()

                expected_error = {
                    "non-CPU device": NotImplementedError,
                    "nonzero device ID": NotImplementedError,
                    "overflowing dimension": OverflowError,
                    "positive stride overflow": OverflowError,
                }.get(description, ValueError)
                with self.assertRaises(expected_error):
                    self.call("sum_view", ct.addressof(descriptor), ct.byref(result))

                del descriptor, owner, array
                gc.collect()
                self.assertEqual(Tensors.active(), allocations)

    def test_adapter_rejects_and_releases_each_invalid_owned_descriptor(self):
        for description, field, value in INVALID_DESCRIPTORS:
            with self.subTest(case=description):
                allocations = Tensors.active()
                descriptor = Descriptor()
                self.call("create", 4, ct.byref(descriptor))
                invalidate(descriptor, field, value)

                with self.assertRaises(BufferError):
                    _buffer.adopt(ct.addressof(descriptor), self.provider)

                self.assertIsNone(descriptor.lease)
                self.assertEqual(Tensors.active(), allocations)


if __name__ == "__main__":
    unittest.main(verbosity=2)
