"""NuGet packaging safeguards; native compilation is covered by package_smoke.py."""

import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "build/python"))
from sharpbind_pack import release, wheel
from sharpbind_pack._platform import PlatformTarget

WINDOWS = PlatformTarget("win-x64", ".dll", "win_amd64")


def native_dll(machine=0x8664, managed=False):
    data = bytearray(512)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 64)
    struct.pack_into("<4sH", data, 64, b"PE\0\0", machine)
    struct.pack_into("<H", data, 64 + 24, 0x20B)
    if managed:
        struct.pack_into("<I", data, 64 + 24 + 112 + 14 * 8, 1234)
    return bytes(data)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="SharpBind package spaces ")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source_package = self.root / "src/example_api"
        self.source_package.mkdir(parents=True)
        (self.source_package / "__init__.py").write_text(
            "# handwritten\n", encoding="utf-8"
        )
        self.project = self.root / "Bridge.csproj"
        self.project.write_text("<Project />", encoding="utf-8")
        (self.root / "pyproject.toml").write_text(
            """
[project]
name = "example"
version = "0.1.0"
requires-python = ">=3.11"
[tool.hatch.build.targets.wheel]
only-include = ["src"]
sources = ["src"]
exclude = ["**/__pycache__", "**/*.pyc"]
[tool.sharpbind]
module = "example_api"
""",
            encoding="utf-8",
        )
        self.enterContext(patch.object(release, "current_target", return_value=WINDOWS))
        self.enterContext(patch.object(release, "check_prerequisites"))
        self.enterContext(patch.object(wheel.shutil, "which", return_value="dotnet"))
        self.enterContext(
            patch.object(wheel, "build_support", side_effect=self.simulate_support)
        )
        self.numerical = False

    def simulate_support(self, staging, module, target):
        package = staging / module / "_sharpbind"
        package.mkdir()
        (package / "__init__.py").write_text("# private support\n", encoding="utf-8")
        (package / "_buffer.pyd").write_bytes(native_dll())
        return {
            str(path): f"{module}/_sharpbind/{path.name}" for path in package.iterdir()
        }

    def simulate_publish(self, command, **kwargs):
        if "twine" in command:
            return
        self.assertTrue(kwargs["check"])
        self.assertEqual(command[1:3], ["msbuild", str(self.project)])
        self.assertIn("-t:Publish", command)
        self.assertNotIn("-t:SharpBindPackage", command)
        properties = dict(
            arg[3:].split("=", 1) for arg in command if arg.startswith("-p:")
        )
        self.generated_package = Path(properties["SharpBindPythonPackageOutput"])
        generated = Path(properties["SharpBindNativeOutput"])
        native = self.generated_package / "_native/win-x64"
        native.mkdir(parents=True)
        generated.mkdir()
        (generated / "bindings.json").write_text(
            json.dumps(
                {
                    "library": "Bridge",
                    "python_module": "example_api",
                    "classes": (
                        [{"methods": [{"result": {"kind": "tensor"}, "arguments": []}]}]
                        if self.numerical
                        else []
                    ),
                }
            ),
            encoding="utf-8",
        )
        (self.generated_package / "_bindings.py").write_text(
            "# generated\n", encoding="utf-8"
        )
        (self.generated_package / "_bindings.pyi").write_text(
            "# stubs\n", encoding="utf-8"
        )
        (self.generated_package / "py.typed").touch()
        (native / "Bridge.dll").write_bytes(native_dll())
        publish = Path(properties["PublishDir"])
        publish.mkdir()
        (publish / "SharpBind.Generator.exe").write_bytes(b"excluded")
        (publish / "Bridge.pdb").write_bytes(b"excluded")

    def build(self, **kwargs):
        return release.build_package(self.root, self.project, **kwargs)

    def test_wheel_contains_validated_outputs_and_preserves_handwritten_sources(self):
        with patch.object(wheel.subprocess, "run", side_effect=self.simulate_publish):
            artifact = self.build()
        self.assertEqual(artifact.name, "example-0.1.0-cp311-abi3-win_amd64.whl")
        with zipfile.ZipFile(artifact) as archive:
            names = set(archive.namelist())
            self.assertTrue(
                {
                    "example_api/__init__.py",
                    "example_api/_bindings.py",
                    "example_api/_bindings.pyi",
                    "example_api/py.typed",
                    "example_api/_native/win-x64/Bridge.dll",
                    "example_api/_sharpbind/_buffer.pyd",
                }
                <= names
            )
            self.assertFalse(
                any(name.endswith((".exe", ".pdb", ".csproj")) for name in names)
            )
            metadata = archive.read("example-0.1.0.dist-info/WHEEL").decode()
            self.assertIn("Root-Is-Purelib: false", metadata)
            self.assertIn("Tag: cp311-abi3-win_amd64", metadata)
        self.assertEqual(
            (self.source_package / "__init__.py").read_text(), "# handwritten\n"
        )
        self.assertFalse((self.source_package / "_bindings.py").exists())
        self.assertFalse(self.generated_package.exists())

    def test_numpy_is_added_only_for_numerical_apis(self):
        for numerical in (False, True):
            self.numerical = numerical
            with self.subTest(numerical=numerical), patch.object(
                wheel.subprocess, "run", side_effect=self.simulate_publish
            ):
                artifact = self.build()
                with zipfile.ZipFile(artifact) as archive:
                    metadata = archive.read("example-0.1.0.dist-info/METADATA").decode()
                self.assertEqual("Requires-Dist: numpy>=1.26" in metadata, numerical)
                self.assertNotIn("sharpbind-runtime", metadata)

    def test_failed_publish_does_not_reuse_old_native_output(self):
        stale = self.root / "artifacts/sharpbind/old/example_api/_native/win-x64"
        stale.mkdir(parents=True)
        (stale / "Bridge.dll").write_bytes(native_dll())
        with patch.object(
            wheel.subprocess,
            "run",
            side_effect=subprocess.CalledProcessError(1, "dotnet"),
        ):
            with self.assertRaises(subprocess.CalledProcessError):
                self.build()
        self.assertEqual(
            list((self.root / "artifacts/sharpbind").iterdir()), [stale.parents[2]]
        )
        self.assertFalse((self.root / "dist").exists())

    def test_missing_native_output_cleans_staging(self):
        def publish(command, **kwargs):
            self.simulate_publish(command, **kwargs)
            (self.generated_package / "_native/win-x64/Bridge.dll").unlink()

        with patch.object(wheel.subprocess, "run", side_effect=publish):
            with self.assertRaises(FileNotFoundError):
                self.build()
        self.assertFalse(self.generated_package.exists())

    def test_invalid_dependency_never_produces_a_wheel(self):
        def publish(command, **kwargs):
            self.simulate_publish(command, **kwargs)
            (self.generated_package / "_native/win-x64/Invalid.dll").write_bytes(
                native_dll(managed=True)
            )

        with patch.object(wheel.subprocess, "run", side_effect=publish):
            with self.assertRaisesRegex(ValueError, "managed DLL"):
                self.build()
        self.assertFalse((self.root / "dist").exists())
        self.assertFalse(self.generated_package.exists())

    def test_missing_dotnet_reports_installation_hint(self):
        with patch.object(wheel.shutil, "which", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "dotnet was not found"):
                self.build()

    def test_requested_runtime_must_match_build_host(self):
        with patch.object(wheel.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "does not match"):
                self.build(runtime_identifier="linux-x64")
        run.assert_not_called()

    def test_configuration_reaches_inner_native_build(self):
        with patch.object(
            wheel.subprocess, "run", side_effect=self.simulate_publish
        ) as run:
            self.build(configuration="Debug")
        self.assertIn("-p:Configuration=Debug", run.call_args_list[0].args[0])

    def test_invalid_module_names_are_rejected(self):
        for name in ("bad-name", "a.b", "class", "9module", "é", "../escape", None):
            with self.subTest(name=name), self.assertRaises(ValueError):
                wheel.validate_module(name)
        self.assertEqual(wheel.validate_module("close"), "close")

    def test_handwritten_source_cannot_contain_stale_generated_files(self):
        (self.source_package / "_bindings.py").write_text("# stale", encoding="utf-8")
        with patch.object(wheel.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "reserved"):
                self.build()
        run.assert_not_called()


class NativeLibraryValidationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="SharpBind native validation ")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def test_rejects_wrong_architecture_and_managed_dlls(self):
        for description, data in (
            ("ARM64 architecture", native_dll(machine=0xAA64)),
            ("managed assembly", native_dll(managed=True)),
            ("invalid header", b"not a DLL"),
        ):
            path = self.root / "Wrong.dll"
            path.write_bytes(data)
            with self.subTest(case=description), self.assertRaises(ValueError):
                wheel.validate_native_library(path, WINDOWS)

    def test_elf_and_macho_architecture_validation(self):
        linux = PlatformTarget("linux-x64", ".so", "linux_x86_64")
        mac = PlatformTarget("osx-arm64", ".dylib", "macosx_14_0_arm64")
        elf = bytearray(64)
        elf[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<HH", elf, 16, 3, 62)
        macho = struct.pack("<IIII", 0xFEEDFACF, 0x0100000C, 0, 6)
        for target, content in ((linux, elf), (mac, macho)):
            path = self.root / ("Bridge" + target.suffix)
            path.write_bytes(content)
            wheel.validate_native_library(path, target)
            path.write_bytes(content[:8])
            with self.assertRaises(ValueError):
                wheel.validate_native_library(path, target)
        struct.pack_into("<H", elf, 18, 183)
        path.write_bytes(elf)
        with self.assertRaises(ValueError):
            wheel.validate_native_library(path, linux)


if __name__ == "__main__":
    unittest.main()
