"""DLPack ownership, actual producer/consumer interchange, and failed adoption cleanup."""

import ctypes as ct
import gc
import unittest
import weakref

import numpy as np
import sharpbind_fixture as fixture
from sharpbind_fixture._sharpbind import _buffer


class Producer:
    """Supports DLPack without implementing Python's buffer protocol."""

    def __init__(self, values):
        self.values = values

    def __dlpack_device__(self):
        return (1, 0)

    def __dlpack__(self, **kwargs):
        return self.values.__dlpack__(**kwargs)


class LegacyProducer(Producer):
    def __dlpack__(self):
        return self.values.__dlpack__()


class Device(ct.Structure):
    _fields_ = [("type", ct.c_int32), ("id", ct.c_int32)]


class Dtype(ct.Structure):
    _fields_ = [("code", ct.c_uint8), ("bits", ct.c_uint8), ("lanes", ct.c_uint16)]


class DLTensor(ct.Structure):
    _fields_ = [
        ("data", ct.c_void_p),
        ("device", Device),
        ("ndim", ct.c_int32),
        ("dtype", Dtype),
        ("shape", ct.POINTER(ct.c_int64)),
        ("strides", ct.POINTER(ct.c_int64)),
        ("byte_offset", ct.c_uint64),
    ]


class Version(ct.Structure):
    _fields_ = [("major", ct.c_uint32), ("minor", ct.c_uint32)]


class VersionedTensor(ct.Structure):
    _fields_ = [
        ("version", Version),
        ("context", ct.c_void_p),
        ("deleter", ct.c_void_p),
        ("flags", ct.c_uint64),
        ("tensor", DLTensor),
    ]


def exported_header(capsule):
    pointer = ct.pythonapi.PyCapsule_GetPointer
    pointer.argtypes = [ct.py_object, ct.c_char_p]
    pointer.restype = ct.c_void_p
    return VersionedTensor.from_address(pointer(capsule, b"dltensor_versioned"))


class DLPackTests(unittest.TestCase):
    def setUp(self):
        fixture.LeaseTests.collect()
        _buffer.collect_leases()
        self.memory = _buffer.counters()
        self.owners = _buffer.lease_counters()

    def tearDown(self):
        fixture.LeaseTests.collect()
        gc.collect()
        _buffer.collect_leases()
        self.assertEqual(_buffer.counters(), self.memory)
        self.assertEqual(_buffer.lease_counters(), self.owners)

    def test_non_buffer_producers_share_strided_storage(self):
        values = np.arange(12, dtype=np.float64).reshape(3, 4)[:, ::-1]
        for producer in (Producer(values), LegacyProducer(values)):
            with self.subTest(producer=type(producer).__name__):
                with fixture.RetainedDataset(producer) as dataset:
                    self.assertEqual(dataset.address(), values.ctypes.data)
                    self.assertEqual(dataset.sum(), values.sum())
                    values[0, 0] += 10
                    self.assertEqual(dataset.sum(), values.sum())

    def test_capsule_is_consumed_once(self):
        values = np.arange(4, dtype=np.float64)
        capsule = values.__dlpack__()
        owner = _buffer.from_dlpack(capsule, False, 10)
        with self.assertRaisesRegex(ValueError, "unconsumed"):
            _buffer.from_dlpack(capsule, False, 10)
        np.testing.assert_array_equal(np.asarray(owner), values)
        del owner, capsule

    def test_non_buffer_producers_can_be_copied_into_managed_arrays(self):
        for values in (
            np.arange(8, dtype=np.float64),
            np.arange(8, dtype=np.float64)[::-2],
        ):
            for producer in (Producer(values), LegacyProducer(values)):
                expected = values.copy()
                result = fixture.AccumulatorDouble.echo(producer)
                values[:] = -1
                np.testing.assert_array_equal(result, expected)
                self.assertTrue(result.flags.owndata)
                del result, producer

    def test_call_scoped_heap_computation_needs_no_explicit_lease(self):
        values = np.arange(8, dtype=np.float64)
        self.assertEqual(fixture.LeaseTests.sum_memory(Producer(values)), values.sum())
        reference = weakref.ref(values)
        fixture.LeaseTests.save_memory(Producer(values))
        del values
        _buffer.collect_leases()
        self.assertIsNone(reference())
        self.assertTrue(fixture.LeaseTests.saved_memory_is_invalid())

    def test_rejected_capsules_release_the_exported_handle(self):
        for failure in ("version", "device", "dtype", "flags", "offset"):
            with self.subTest(failure=failure):
                values = np.arange(4, dtype=np.float64)
                reference = weakref.ref(values)
                capsule = values.__dlpack__(max_version=(1, 0))
                header = exported_header(capsule)
                if failure == "version":
                    header.version.major = 99
                elif failure == "device":
                    header.tensor.device.type = 2
                elif failure == "dtype":
                    header.tensor.dtype.lanes = 2
                elif failure == "flags":
                    header.flags = 8
                else:
                    header.tensor.byte_offset = 2**64 - 1
                del header, values
                with self.assertRaises(BufferError):
                    _buffer.from_dlpack(capsule, False, 10)
                self.assertIsNone(reference())
                with self.assertRaises(ValueError):
                    _buffer.from_dlpack(capsule, False, 10)
                del capsule

    def test_byte_offsets_are_applied_before_access(self):
        values = np.arange(5, dtype=np.float64)
        expected = values[1:].copy()
        capsule = values.__dlpack__(max_version=(1, 0))
        header = exported_header(capsule)
        header.tensor.byte_offset = values.itemsize
        header.tensor.shape[0] = 4
        owner = _buffer.from_dlpack(capsule, False, 10)
        np.testing.assert_array_equal(np.asarray(owner), expected)
        del owner, header, capsule

    def test_dlpack_views_outlive_the_source_array(self):
        active = fixture.Tensors.active()
        tensor = fixture.Tensors.create(4)
        self.assertEqual(tensor.__dlpack_device__(), (1, 0))
        first = np.from_dlpack(tensor)
        second = np.from_dlpack(tensor)
        self.assertEqual(first.ctypes.data, fixture.Tensors.address(tensor))
        del tensor
        np.testing.assert_array_equal(first, [0, 1, 2, 3])
        del first
        _buffer.collect_leases()
        self.assertEqual(fixture.Tensors.active(), active + 1)
        del second
        _buffer.collect_leases()
        self.assertEqual(fixture.Tensors.active(), active)

    def test_imported_owner_can_be_returned_without_copying(self):
        values = np.arange(4, dtype=np.float64)
        reference = weakref.ref(values)
        tensor = fixture.LeaseTests.echo(Producer(values))
        self.assertEqual(fixture.Tensors.address(tensor), values.ctypes.data)
        array = np.from_dlpack(tensor)
        values[0] = 42
        del tensor
        del values
        self.assertIsNotNone(reference())
        self.assertEqual(array[0], 42)
        del array
        _buffer.collect_leases()
        self.assertIsNone(reference())

    def test_unsupported_devices_are_rejected_before_export(self):
        class DeviceProducer:
            def __dlpack_device__(self):
                return (2, 0)

            def __dlpack__(self, **kwargs):
                raise AssertionError("GPU pointer was requested")

        with self.assertRaisesRegex(BufferError, "CPU"):
            fixture.RetainedDataset(DeviceProducer())

    def test_readonly_results_remain_readonly_through_dlpack(self):
        source = fixture.Tensors.read_only()
        array = np.from_dlpack(source)
        self.assertFalse(array.flags.writeable)
        np.testing.assert_array_equal(array, [1, 2, 3])
        del array, source
        _buffer.collect_leases()
