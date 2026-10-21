"""Publish and validate native bindings, then assemble them using Hatchling."""

import ast
from copy import deepcopy
import json
import keyword
import os
import re
import shutil
import subprocess
from pathlib import Path

from .native import validate_native_library
from .adapter import build_support


def validate_module(module):
    """Return a valid top-level import name, rejecting paths and Python keywords."""
    if (
        not isinstance(module, str)
        or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", module)
        or keyword.iskeyword(module)
    ):
        raise ValueError(
            "SharpBind module must be a top-level ASCII Python identifier."
        )
    return module


def _validate_source(source: Path):
    """Keep generated files out of the handwritten package tree."""
    if not (source / "__init__.py").is_file():
        raise ValueError(
            f"Python package entry point is missing: {source / '__init__.py'}"
        )
    for reserved in (
        "_bindings.py",
        "_bindings.pyi",
        "py.typed",
        "_native",
        "_sharpbind",
    ):
        if (source / reserved).exists():
            raise ValueError(
                f"{source / reserved} is reserved for generated files. Remove stale generated outputs from src before building."
            )


def _publish_native(
    root: Path, project: Path, module: str, staging: Path, target, configuration: str
):
    """Build the bridge and direct generated bindings and native outputs into this build's staging."""
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        raise RuntimeError(
            "Install the .NET 9.0 SDK or higher and native compiler prerequisites; dotnet was not found on PATH."
        )
    subprocess.run(
        [
            dotnet,
            "msbuild",
            str(project),
            "-restore",
            # Publish is the inner build. Calling SharpBindPackage would recurse.
            "-t:Publish",
            f"-p:Configuration={configuration}",
            f"-p:RuntimeIdentifier={target.rid}",
            f"-p:PublishDir={staging / 'publish'}/",
            f"-p:SharpBindPythonModuleName={module}",
            f"-p:SharpBindPythonPackageOutput={staging / module}",
            f"-p:SharpBindNativeOutput={staging / 'generated'}",
        ],
        cwd=root,
        check=True,
        env=(
            dict(os.environ, MACOSX_DEPLOYMENT_TARGET="14.0")
            if target.rid == "osx-arm64"
            else None
        ),
    )


def _package_files(staging: Path, module: str, target):
    """Validate all generated files before returning their destinations inside the wheel."""
    schema = json.loads(
        (staging / "generated" / "bindings.json").read_text(encoding="utf-8")
    )

    if schema.get("python_module") != module:
        raise ValueError("Generated bindings have the wrong Python module name.")

    library = schema.get("library")
    if not isinstance(library, str) or not re.fullmatch(
        r"[A-Za-z][A-Za-z0-9_]*", library
    ):
        raise ValueError("Generated bindings have an invalid native library name.")

    package = staging / module
    bindings = package / "_bindings.py"
    if not bindings.is_file():
        raise FileNotFoundError(f"Generated bindings are missing: {bindings}")

    ast.parse(bindings.read_text(encoding="utf-8"), filename=str(bindings))
    native = package / "_native" / target.rid
    validate_native_library(native / target.library_name(library), target)

    files = {str(bindings): f"{module}/_bindings.py"}
    for name in ("_bindings.pyi", "py.typed"):
        path = package / name
        if not path.is_file():
            raise FileNotFoundError(f"Generated typing file is missing: {path}")
        files[str(path)] = f"{module}/{name}"

    # Package explicit native staging only; the publish directory also contains managed and build-time files.
    for dependency in native.iterdir():
        if not dependency.is_file() or not (
            dependency.name.endswith(target.suffix)
            or target.suffix == ".so"
            and ".so." in dependency.name
        ):
            raise ValueError(
                f"Only native shared-library dependencies can be staged: {dependency}"
            )
        validate_native_library(dependency, target)
        files[str(dependency)] = f"{module}/_native/{target.rid}/{dependency.name}"
    numerical = any(
        specification is not None and specification["kind"] in ("array", "tensor")
        for exported_class in schema["classes"]
        for method in exported_class["methods"]
        for specification in [
            method["result"],
            *(argument["type"] for argument in method["arguments"]),
        ]
    )
    return files, numerical


def build_wheel(root, config, native_project, module, staging, target, configuration):
    """Keep handwritten sources intact and include only validated native outputs."""
    from hatchling.builders.wheel import WheelBuilder

    _validate_source(root / "src" / module)
    _publish_native(root, native_project, module, staging, target, configuration)
    files, numerical = _package_files(staging, module, target)
    files.update(build_support(staging, module, target))
    config = deepcopy(config)
    if numerical:
        from packaging.requirements import Requirement
        from packaging.utils import canonicalize_name

        dependencies = config["project"].setdefault("dependencies", [])
        if not any(
            canonicalize_name(Requirement(value).name) == "numpy"
            and Requirement(value).marker is None
            for value in dependencies
        ):
            dependencies.append("numpy>=1.26")

    class SharpBindWheelBuilder(WheelBuilder):
        def get_default_build_data(self):
            data = super().get_default_build_data()
            data["force_include"] = files
            data["pure_python"] = False
            data["tag"] = "cp311-abi3-" + target.wheel_platform
            data["infer_tag"] = False
            return data

    builder = SharpBindWheelBuilder(str(root), config=config)
    (wheel,) = builder.build(directory=str(staging / "raw"), versions=["standard"])
    return Path(wheel)
