"""Checks against the freshly installed fixture wheel."""

import ast
import copy
import importlib.metadata
import inspect
import importlib.util
import json
import tempfile
from pathlib import Path
import typing
import unittest
import sharpbind_fixture as fixture
from sharpbind_fixture import _bindings as bindings
from _support import run_scenario


class GeneratedApiTests(unittest.TestCase):
    def test_public_package_and_metadata(self):
        self.assertEqual(fixture.AccumulatorInt32.__module__, "sharpbind_fixture")
        self.assertIn("AccumulatorInt32", fixture.__all__)
        self.assertNotIn("Library", fixture.__all__)
        self.assertEqual(
            importlib.metadata.metadata("sharpbind-fixture")["Name"],
            "sharpbind-fixture",
        )
        self.assertEqual(fixture.handwritten_marker, "preserved")
        self.assertIsNone(importlib.util.find_spec("sharpbind_runtime"))
        for name in ("Tensor", "TensorInput", "NativeObject", "Library"):
            self.assertFalse(hasattr(fixture, name), name)
        requirements = importlib.metadata.requires("sharpbind-fixture") or []
        self.assertFalse(any("sharpbind-runtime" in item for item in requirements))

    def test_import_is_lazy_and_calls_work_without_dotnet_on_path(self):
        run_scenario("lazy_import")

    def test_scalar_calls_need_neither_numpy_nor_the_tensor_adapter(self):
        with tempfile.TemporaryDirectory() as directory:
            schema = Path(directory) / "bindings.json"
            schema.write_text(
                json.dumps(bindings._provider.native.schema), encoding="utf-8"
            )
            run_scenario("scalar_without_numpy", schema, fixture.__file__)

    def test_instances_facades_and_defaults(self):
        self.assertEqual(fixture.NestedGeneric.echo(1.25), 1.25)
        with fixture.AccumulatorInt32(10) as integers, fixture.AccumulatorDouble(
            1.5
        ) as doubles:
            self.assertEqual(integers.add(3), 13)
            self.assertEqual(doubles.add(amount=0.25), 1.75)
            with self.assertRaises(TypeError):
                copy.copy(integers)
        self.assertTrue(integers.closed)
        integers.close()
        with self.assertRaises(RuntimeError):
            integers.add(1)
        self.assertEqual(fixture.NamedApi.echo_int(value=3), 3)
        self.assertEqual(fixture.NamedApi.echo_double(value=3.25), 3.25)
        with fixture.NamedApi(initial=9) as named:
            self.assertEqual(named.initial(), 9)
        with fixture.NamedApi() as default:
            self.assertEqual(default.initial(), 0)
        self.assertEqual(fixture.ExternalFunctions.identity(user_input=7), 7)

    def test_runtime_signature_and_documentation(self):
        signature = inspect.signature(fixture.NamedApi.echo_int)
        self.assertEqual(list(signature.parameters), ["value"])
        self.assertIs(typing.get_type_hints(fixture.NamedApi.echo_int)["return"], int)
        self.assertIn("Return value as an integer.", fixture.NamedApi.echo_int.__doc__)
        self.assertIn("Parameters", fixture.NamedApi.echo_int.__doc__)
        self.assertIn("value : int", fixture.NamedApi.echo_int.__doc__)
        annotation = typing.get_type_hints(fixture.AccumulatorDouble.echo)["values"]
        self.assertIn(
            "ArrayLike", str(fixture.AccumulatorDouble.echo.__annotations__["values"])
        )
        self.assertIsNotNone(annotation)

    def test_stubs_match_all_generated_signatures(self):
        path = Path(bindings.__file__).with_suffix(".pyi")
        self.assertTrue(path.with_name("py.typed").is_file())
        actual, stub = ast.parse(
            Path(bindings.__file__).read_text(encoding="utf-8")
        ), ast.parse(path.read_text(encoding="utf-8"))
        self.assertNotIn("_sb", path.read_text(encoding="utf-8"))

        def signatures(tree):
            return {
                (exported_class.name, method.name): (
                    ast.dump(method.args),
                    ast.dump(method.returns),
                )
                for exported_class in tree.body
                if isinstance(exported_class, ast.ClassDef)
                for method in exported_class.body
                if isinstance(method, ast.FunctionDef)
                and method.name not in {"close", "closed", "__enter__", "__exit__"}
            }

        self.assertEqual(signatures(actual), signatures(stub))

    def test_static_namespaces_have_no_instance_lifetime(self):
        for namespace in (
            fixture.Functions,
            fixture.Tensors,
            fixture.ExternalFunctions,
        ):
            with self.subTest(namespace=namespace.__name__):
                for name in ("close", "closed", "__enter__", "__exit__"):
                    self.assertFalse(hasattr(namespace, name), name)

    def test_unicode_defaults_and_builtin_class_names(self):
        self.assertEqual(fixture.int.echo(), "🌍")
        self.assertEqual(fixture.int.echo("hello"), "hello")
        self.assertEqual(fixture.int.number(7), 7)
        default = inspect.signature(fixture.int.echo).parameters["value"].default
        self.assertEqual(default, "🌍")
