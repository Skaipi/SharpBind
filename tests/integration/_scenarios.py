"""Isolated integration scenarios, invoked by the unittest suites."""

import argparse
import ctypes as ct
import shutil
from unittest.mock import patch


def lazy_import():
    with patch("ctypes.CDLL", side_effect=AssertionError("eager load")):
        import sharpbind_fixture as fixture
    assert fixture.Functions.identity_int(42) == 42


def scalar_without_numpy(schema_file, package_file):
    import json
    import sys

    class RejectTensorImports:
        def find_spec(self, fullname, path=None, target=None):
            if (
                fullname == "numpy"
                or fullname.startswith("numpy.")
                or fullname == "_scalar_support._buffer"
            ):
                raise AssertionError(f"Scalar call tried to import {fullname}")

    sys.meta_path.insert(0, RejectTensorImports())
    from pathlib import Path
    import importlib.util

    support = Path(package_file).parent / "_sharpbind"
    spec = importlib.util.spec_from_file_location(
        "_scalar_support", support / "__init__.py"
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    Library = module.Library

    library = Library(
        json.loads(Path(schema_file).read_text(encoding="utf-8")), package_file
    )
    functions = next(
        item for item in library.native.schema["classes"] if item["name"] == "Functions"
    )
    methods = {method["name"]: method["entry"] for method in functions["methods"]}
    assert library.invoke(methods["identity_int"], None, (42,)) == 42
    assert library.invoke(methods["echo_text"], None, ("hello",)) == "hello"
    assert library.invoke(methods["negate"], None, (True,)) is False
    assert "numpy" not in sys.modules
    assert "_scalar_support._buffer" not in sys.modules


def foreign_handles(destination):
    from sharpbind_fixture import _bindings
    from sharpbind_fixture._sharpbind._binding import Library
    from sharpbind_fixture._sharpbind._provider import _runtime_path

    first = _bindings._provider
    first.native._load()
    schema = first.native.schema
    shutil.copy2(_runtime_path(schema["library"], _bindings.__file__), destination)
    second = Library(schema, _bindings.__file__)
    second.native.lib = ct.CDLL(destination)
    second.native._configure_exports(second.native.lib)
    accumulator = next(
        item for item in schema["classes"] if item["name"] == "AccumulatorInt32"
    )
    methods = {method["name"]: method["entry"] for method in accumulator["methods"]}
    first_handle = first.invoke(methods["__init__"], None, (1,))
    second_handle = second.invoke(methods["__init__"], None, (2,))
    try:
        assert first_handle.provider != second_handle.provider
        try:
            second.invoke(methods["add"], first_handle, (1,))
        except ValueError:
            pass
        else:
            raise AssertionError("Foreign handle was accepted")
        assert second.invoke(methods["add"], second_handle, (3,)) == 5
    finally:
        first.release(accumulator["release"], first_handle)
        second.release(accumulator["release"], second_handle)


def conversion_reentry():
    from sharpbind_fixture import AccumulatorInt32

    with AccumulatorInt32(5) as counter:

        class ReentrantValue:
            def __index__(self):
                return counter.add(0)

        assert counter.add(ReentrantValue()) == 10


def close_during_conversion():
    from sharpbind_fixture import AccumulatorInt32

    with AccumulatorInt32(5) as counter:

        class ClosingValue:
            def __index__(self):
                counter.close()
                return 1

        try:
            counter.add(ClosingValue())
        except RuntimeError:
            pass
        else:
            raise AssertionError("Call entered a closed instance")
        assert counter.closed


def close_during_construction():
    from sharpbind_fixture import AccumulatorInt32

    counter = AccumulatorInt32.__new__(AccumulatorInt32)

    class ClosingInitial:
        def __index__(self):
            counter.close()
            return 1

    try:
        counter.__init__(ClosingInitial())
    except RuntimeError:
        pass
    else:
        raise AssertionError("Constructor created an instance after close")
    assert counter.closed


def native_reentry():
    from sharpbind_fixture import AccumulatorInt32, _bindings

    with AccumulatorInt32(1) as counter:

        def reenter(*arguments):
            for action in (lambda: counter.add(0), counter.close):
                try:
                    action()
                except RuntimeError:
                    pass
                else:
                    raise AssertionError("Native call reentry was accepted")
            raise RuntimeError("stop before native invocation")

        with patch.object(_bindings._provider.native, "call", side_effect=reenter):
            try:
                counter.add(1)
            except RuntimeError as error:
                assert str(error) == "stop before native invocation"
            else:
                raise AssertionError("Mock did not run")
        assert counter.add(0) == 1


def shutdown_with_live_views():
    from sharpbind_fixture import Tensors

    tensor = Tensors.create()
    first, second = tensor[:], tensor[::-1]
    del tensor
    assert first[3] == second[0] == 3
    # Keep the views alive during interpreter shutdown.
    globals()["live_views"] = (first, second)


def shutdown_with_live_leases():
    from sharpbind_fixture import LeaseTests, RetainedDataset, Tensors

    tensor = Tensors.create()
    dataset = RetainedDataset(tensor)
    LeaseTests.abandon(tensor)
    del tensor
    assert dataset.sum() == 6
    globals()["live_dataset"] = dataset


SCENARIOS = {
    "scalar_without_numpy": scalar_without_numpy,
    "lazy_import": lazy_import,
    "foreign_handles": foreign_handles,
    "conversion_reentry": conversion_reentry,
    "close_during_conversion": close_during_conversion,
    "close_during_construction": close_during_construction,
    "native_reentry": native_reentry,
    "shutdown_with_live_views": shutdown_with_live_views,
    "shutdown_with_live_leases": shutdown_with_live_leases,
}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scenario", choices=SCENARIOS)
    parser.add_argument("arguments", nargs="*")
    options = parser.parse_args()
    SCENARIOS[options.scenario](*options.arguments)
