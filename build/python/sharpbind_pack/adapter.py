"""Build and stage a package's private CPython Stable ABI adapter."""

import os
from pathlib import Path
import shutil
import subprocess
import sys

# Repository sources and NuGet assets retain the same relative layout.
COMMON_RUNTIME = Path(__file__).resolve().parents[3] / "python/common-runtime"


def build_support(staging, module, target):
    from .native import validate_native_library

    package = staging / module / "_sharpbind"
    package.mkdir()
    for source in (COMMON_RUNTIME / "src/sharpbind_runtime").glob("*.py"):
        shutil.copy2(source, package / source.name)
    # Build-time and installed host checks share one maintained source.
    shutil.copy2(Path(__file__).with_name("_platform.py"), package / "_platform.py")
    shutil.copy2(COMMON_RUNTIME / "LICENSE", package / "LICENSE")

    # Relative source filenames keep MSVC object paths short inside a NuGet cache.
    subprocess.run(
        [
            sys.executable,
            str(Path(__file__).resolve()),
            module,
            str(staging),
            target.rid,
        ],
        cwd=COMMON_RUNTIME / "native",
        check=True,
    )
    (binary,) = [
        path for path in package.glob("_buffer*") if path.suffix in (".pyd", ".so")
    ]
    validate_native_library(binary, target)
    return {
        str(source): f"{module}/_sharpbind/{source.name}"
        for source in package.iterdir()
    }


def compile_adapter(module, staging, rid):
    """Let Setuptools manage the host compiler, SDK, and Stable ABI linkage."""
    from setuptools import Distribution, Extension

    arguments = [] if os.name == "nt" else ["-fvisibility=hidden"]
    deployment = ["-mmacosx-version-min=14.0"] if rid == "osx-arm64" else []
    extension = Extension(
        f"{module}._sharpbind._buffer",
        [source.name for source in sorted(Path.cwd().glob("*.c"))],
        define_macros=[("SHARPBIND_MODULE", f'"{module}._sharpbind._buffer"')],
        py_limited_api=True,
        extra_compile_args=arguments + deployment,
        extra_link_args=deployment,
    )
    distribution = Distribution({"name": module, "ext_modules": [extension]})
    command = distribution.get_command_obj("build_ext")
    command.build_lib = str(staging)
    command.build_temp = str(staging / "adapter-build")
    command.ensure_finalized()
    command.run()


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("module")
    parser.add_argument("staging", type=Path)
    parser.add_argument("rid")
    options = parser.parse_args()
    compile_adapter(options.module, options.staging, options.rid)
