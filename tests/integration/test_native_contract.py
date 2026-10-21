"""Checks against the freshly installed fixture wheel."""

import copy
import ctypes as ct
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import tempfile
from threading import Barrier
import unittest
from unittest.mock import patch
import sharpbind_fixture as fixture
from sharpbind_fixture import _bindings as bindings
from sharpbind_fixture._sharpbind import _provider as providers
from sharpbind_fixture._sharpbind._binding import Library
from _support import PROVIDER, method, native, run_scenario


class NativeContractTests(unittest.TestCase):
    def test_handle_and_error_native_layouts(self):
        for record, size, offsets in [
            (providers.Handle, 16, (0, 8)),
            (providers.Error, 32, (0, 4, 8, 16, 24)),
        ]:
            with self.subTest(record=record.__name__):
                self.assertEqual(ct.sizeof(record), size)
                self.assertEqual(
                    tuple(getattr(record, name).offset for name, _ in record._fields_),
                    offsets,
                )

    def test_loader_diagnostics(self):
        with patch.object(providers.ct, "sizeof", return_value=4):
            with self.assertRaises(NotImplementedError):
                providers._runtime_path("NativeFixture", bindings.__file__)
        library = providers.NativeProvider(PROVIDER.schema, bindings.__file__)
        with patch.object(providers.Path, "is_file", return_value=False):
            with self.assertRaisesRegex(FileNotFoundError, "NativeFixture"):
                library._load()

    def test_owned_errors_survive_later_calls(self):
        first, second = providers.Error(), providers.Error()
        function = native("Functions", "fail")
        self.assertEqual(function(ct.byref(first)), -1)
        saved = first.message
        self.assertEqual(function(ct.byref(second)), -1)
        self.assertEqual(first.message, saved)
        self.assertNotEqual(first.lease, second.lease)
        self.assertEqual(first.category, 6)
        PROVIDER.error_release(ct.byref(first))
        PROVIDER.error_release(ct.byref(second))
        self.assertIsNone(first.lease)
        PROVIDER.error_release(ct.byref(first))

    def test_native_validation_and_stale_handles(self):
        echo = native("AccumulatorInt32", "echo")
        output, length, error = (
            ct.POINTER(ct.c_int32)(),
            ct.c_int64(),
            providers.Error(),
        )
        for pointer, count in [(None, 1), (None, -1), (None, 2**40)]:
            self.assertEqual(
                echo(
                    pointer, count, ct.byref(output), ct.byref(length), ct.byref(error)
                ),
                -1,
            )
            self.assertFalse(output)
            PROVIDER.error_release(ct.byref(error))
        with fixture.AccumulatorInt32(1) as obj:
            stale = obj._handle
        value = ct.c_int32()
        self.assertEqual(
            native("AccumulatorInt32", "add")(
                stale, 1, ct.byref(value), ct.byref(error)
            ),
            -1,
        )
        PROVIDER.error_release(ct.byref(error))

    def test_unsupported_schema_version(self):
        for version in (None, -1, 9999):
            with self.subTest(version=version), self.assertRaisesRegex(
                RuntimeError, "Unsupported binding schema version"
            ):
                providers.NativeProvider(
                    dict(PROVIDER.schema, schema_version=version), bindings.__file__
                )

    def test_missing_export_fails_loading(self):
        broken = copy.deepcopy(PROVIDER.schema)
        broken["classes"][0]["methods"][0]["entry"] = "missing_native_symbol"
        provider = providers.NativeProvider(broken, bindings.__file__)
        with self.assertRaisesRegex(RuntimeError, "missing_native_symbol"):
            provider._load()
        self.assertIsNone(provider.lib)

    def test_concurrent_first_calls(self):
        library = Library(PROVIDER.schema, bindings.__file__)
        entry = method("Functions", "identity_int")["entry"]
        barrier = Barrier(8)

        def invoke(value):
            barrier.wait(timeout=10)
            return library.invoke(entry, None, (value,))

        with ThreadPoolExecutor(max_workers=8) as executor:
            self.assertEqual(list(executor.map(invoke, range(8))), list(range(8)))
        self.assertEqual(library.invoke(entry, None, (42,)), 42)

    def test_two_native_providers_reject_foreign_handles(self):
        suffix = providers._runtime_path(
            PROVIDER.schema["library"], bindings.__file__
        ).suffix
        with tempfile.TemporaryDirectory() as directory:
            run_scenario("foreign_handles", Path(directory) / ("Second" + suffix))
