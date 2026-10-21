"""Checks against the freshly installed fixture wheel."""

import threading
import ctypes as ct
import unittest
from unittest.mock import patch
import numpy as np
import sharpbind_fixture as fixture
from sharpbind_fixture._sharpbind import _buffer
from concurrent.futures import ThreadPoolExecutor
from _support import PROVIDER, native, run_scenario, wait_until


class ObjectLifetimeTests(unittest.TestCase):
    def test_failed_array_adoption_releases_storage_even_with_a_retained_traceback(
        self,
    ):
        before = fixture.Tensors.active()

        def reject(value):
            # Model a failed C conversion without a mock call log retaining its input.
            del value
            raise ValueError("view failed")

        retained = None
        with patch.object(np, "asarray", new=reject):
            try:
                fixture.Tensors.create(4)
            except ValueError as error:
                retained = error
        self.assertIsNotNone(retained)
        self.assertEqual(fixture.Tensors.active(), before)

    def test_conversion_failure_releases_native_result(self):
        PROVIDER._load()
        with patch.object(
            PROVIDER, "release_buffer", wraps=PROVIDER.release_buffer
        ) as free:
            with patch.object(
                np.ctypeslib, "as_array", side_effect=ValueError("copy failed")
            ):
                with self.assertRaisesRegex(ValueError, "copy failed"):
                    fixture.AccumulatorInt32.echo([1, 2])
            free.assert_called_once()
            pointer = free.call_args.args[0]
        with self.assertRaises(ValueError):
            PROVIDER._call(PROVIDER.free, pointer)

    def test_disposal_exactly_once(self):
        before = fixture.DisposableCounter.disposed()
        with fixture.DisposableCounter() as counter:
            self.assertFalse(counter.closed)
        counter.close()
        self.assertEqual(fixture.DisposableCounter.disposed(), before + 1)

    def test_close_rejects_new_calls_and_waits_for_active_call(self):
        instance = fixture.BlockingCall()
        before = fixture.BlockingCall.disposed()
        results = []
        caller = threading.Thread(target=lambda: results.append(instance.wait()))
        closer = threading.Thread(target=instance.close)
        caller.start()
        try:
            wait_until(lambda: fixture.BlockingCall.entered() == 1, "native call entry")
            closer.start()
            wait_until(lambda: instance.closed, "instance close")
            self.assertTrue(instance.closed)
            self.assertTrue(closer.is_alive())
            with self.assertRaises(RuntimeError):
                instance.wait()
            self.assertEqual(fixture.BlockingCall.disposed(), before)
        finally:
            fixture.BlockingCall.resume()
            caller.join(10)
            if closer.ident is not None:
                closer.join(10)
        self.assertFalse(caller.is_alive())
        self.assertFalse(closer.is_alive())
        self.assertEqual(results, [before])
        self.assertEqual(fixture.BlockingCall.disposed(), before + 1)

    def test_opted_in_calls_overlap_and_close_waits_for_all(self):
        instance = fixture.ConcurrentBlockingCall()
        before = fixture.ConcurrentBlockingCall.disposed()
        results, errors = [], []

        def call():
            try:
                results.append(instance.wait())
            except BaseException as error:
                errors.append(error)

        callers = [threading.Thread(target=call) for _ in range(3)]
        closer = threading.Thread(target=instance.close)
        for caller in callers:
            caller.start()
        try:
            wait_until(
                lambda: fixture.ConcurrentBlockingCall.entered() == 3,
                "concurrent native call entry",
            )
            closer.start()
            wait_until(lambda: instance.closed, "concurrent instance close")
            self.assertTrue(closer.is_alive())
            with self.assertRaises(RuntimeError):
                instance.wait()
            self.assertEqual(fixture.ConcurrentBlockingCall.disposed(), before)
        finally:
            fixture.ConcurrentBlockingCall.resume()
            for caller in callers:
                caller.join(10)
            if closer.ident is not None:
                closer.join(10)
            instance.close()
        self.assertFalse(any(caller.is_alive() for caller in callers))
        self.assertFalse(closer.is_alive())
        self.assertEqual(errors, [])
        self.assertEqual(results, [before] * 3)
        self.assertEqual(fixture.ConcurrentBlockingCall.disposed(), before + 1)

    def test_native_calls_overlap_and_release_waits_without_python_leases(self):
        instance = fixture.ConcurrentBlockingCall()
        handle = instance._handle
        function = native("ConcurrentBlockingCall", "wait")
        before = fixture.ConcurrentBlockingCall.disposed()
        results, errors = [], []

        def call():
            try:
                value = ct.c_int32()
                PROVIDER._call(function, handle, ct.byref(value))
                results.append(value.value)
            except BaseException as error:
                errors.append(error)

        callers = [threading.Thread(target=call) for _ in range(3)]
        closer = threading.Thread(target=instance.close)
        for caller in callers:
            caller.start()
        try:
            wait_until(
                lambda: fixture.ConcurrentBlockingCall.entered() == 3,
                "native concurrent handle calls",
            )
            closer.start()
            wait_until(lambda: instance.closed, "native release entry")
            self.assertTrue(closer.is_alive())
            self.assertEqual(fixture.ConcurrentBlockingCall.disposed(), before)
        finally:
            fixture.ConcurrentBlockingCall.resume()
            for caller in callers:
                caller.join(10)
            if closer.ident is not None:
                closer.join(10)
            instance.close()
        self.assertFalse(any(caller.is_alive() for caller in callers))
        self.assertFalse(closer.is_alive())
        self.assertEqual(errors, [])
        self.assertEqual(results, [before] * 3)
        self.assertEqual(fixture.ConcurrentBlockingCall.disposed(), before + 1)
        with self.assertRaises(ValueError):
            PROVIDER._call(function, handle, ct.byref(ct.c_int32()))

    def test_default_native_calls_are_serialized(self):
        with fixture.AccumulatorInt32(0) as counter:
            function = native("AccumulatorInt32", "add")
            handle = counter._handle

            def increment():
                value = ct.c_int32()
                for _ in range(100):
                    PROVIDER._call(function, handle, 1, ct.byref(value))

            with ThreadPoolExecutor(max_workers=4) as executor:
                calls = [executor.submit(increment) for _ in range(4)]
                for call in calls:
                    call.result(timeout=10)
            self.assertEqual(counter.add(0), 400)

    def test_default_calls_are_serialized(self):
        with fixture.AccumulatorInt32(0) as counter:

            def increment():
                for _ in range(100):
                    counter.add(1)

            with ThreadPoolExecutor(max_workers=4) as executor:
                calls = [executor.submit(increment) for _ in range(4)]
                for call in calls:
                    call.result(timeout=10)
            self.assertEqual(counter.add(0), 400)

    def test_error_traceback_does_not_retain_borrowed_leases(self):
        before = _buffer.counters()
        retained = None
        with patch.object(
            PROVIDER,
            "_call",
            side_effect=RuntimeError("simulated managed failure"),
        ):
            try:
                fixture.Tensors.sum_view(np.arange(4, dtype=np.float64))
            except RuntimeError as error:
                retained = error
        self.assertIsNotNone(retained)
        self.assertEqual(_buffer.counters(), before)

    def test_conversion_reentry(self):
        run_scenario("conversion_reentry")

    def test_close_during_conversion(self):
        run_scenario("close_during_conversion")

    def test_close_during_construction(self):
        run_scenario("close_during_construction")

    def test_native_reentry(self):
        run_scenario("native_reentry")
