"""MSBuild wheel orchestration using explicitly installed Python prerequisites."""

import argparse
from importlib import metadata
import os
from pathlib import Path
import shutil
import subprocess
import sys
import sysconfig
import tempfile
import tomllib

from ._platform import current_target
from .wheel import build_wheel, validate_module


def check_prerequisites(target, release):
    requirements = {"hatchling": ">=1.27,<2", "twine": ">=6", "setuptools": ">=77"}
    if release and target.rid == "linux-x64":
        requirements.update(auditwheel=">=6", patchelf=">=0.14")
    elif release and target.rid == "osx-arm64":
        requirements.update(delocate=">=0.12")

    versions = {}
    for name in requirements:
        try:
            versions[name] = metadata.version(name)
        except metadata.PackageNotFoundError:
            versions[name] = None

    missing = [name for name, version in versions.items() if version is None]
    if not missing:
        from packaging.specifiers import SpecifierSet

        missing = [
            name
            for name, version in versions.items()
            if version not in SpecifierSet(requirements[name])
        ]
    if missing:
        packages = " ".join(
            f'"{name}{specifier}"' for name, specifier in requirements.items()
        )
        raise RuntimeError(
            f"Missing or unsupported Python build prerequisites: {', '.join(missing)}. "
            f'Install them in the selected environment: "{sys.executable}" -m pip install {packages}'
        )


def repair_wheel(wheel, output, target):
    """Repair native dependencies and platform tags into a fresh output directory."""
    wheel, output = Path(wheel).resolve(), Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Repair tools may launch scripts such as patchelf from the selected venv.
    environment = dict(os.environ)
    environment["PATH"] = (
        sysconfig.get_path("scripts") + os.pathsep + environment.get("PATH", "")
    )
    if wheel.name.endswith("-none-any.whl") or target.rid == "win-x64":
        shutil.copy2(wheel, output / wheel.name)
    elif target.rid == "linux-x64":
        subprocess.run(
            [
                sys.executable,
                "-m",
                "auditwheel",
                "repair",
                str(wheel),
                "--plat",
                "manylinux_2_28_x86_64",
                "-w",
                str(output),
            ],
            check=True,
            env=environment,
        )
    elif target.rid == "osx-arm64":
        environment["MACOSX_DEPLOYMENT_TARGET"] = "14.0"
        subprocess.run(
            [
                sys.executable,
                "-m",
                "delocate.cmd.delocate_wheel",
                "--require-archs",
                "arm64",
                "-w",
                str(output),
                str(wheel),
            ],
            check=True,
            env=environment,
        )
    else:
        raise ValueError(f"Unsupported release target: {target.rid}")


def build_package(
    project,
    native_project,
    output=None,
    *,
    release=True,
    configuration="Release",
    runtime_identifier=None,
    module=None,
):
    """Build a wheel, prepare it for distribution, and publish only validated output."""
    project = Path(project).resolve()
    if not (project / "pyproject.toml").is_file():
        raise FileNotFoundError(
            f"Python project is missing: {project / 'pyproject.toml'}"
        )
    native_project = Path(native_project).resolve()
    if not native_project.is_file():
        raise FileNotFoundError(f"Native project is missing: {native_project}")
    with (project / "pyproject.toml").open("rb") as source:
        config = tomllib.load(source)
    module = validate_module(
        module or config.get("tool", {}).get("sharpbind", {}).get("module")
    )
    target = current_target()
    if runtime_identifier and runtime_identifier != target.rid:
        raise ValueError(
            f"RuntimeIdentifier {runtime_identifier} does not match the Python build host {target.rid}. "
            "Build each wheel on its target OS and architecture."
        )
    check_prerequisites(target, release)
    output = Path(output).resolve() if output else project / "dist"
    # Failed builds and repairs never select or publish a wheel from an older run.
    staging_root = project / "artifacts" / "sharpbind"
    staging_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="wheel-", dir=staging_root) as temporary:
        staging = Path(temporary)
        prepared = staging / "prepared"
        wheel = build_wheel(
            project, config, native_project, module, staging, target, configuration
        )
        if release:
            repair_wheel(wheel, prepared, target)
        else:
            prepared.mkdir()
            shutil.copy2(wheel, prepared / wheel.name)
        artifacts = list(prepared.glob("*.whl"))
        if len(artifacts) != 1:
            raise ValueError("Wheel preparation must produce exactly one wheel.")
        subprocess.run(
            [sys.executable, "-m", "twine", "check", "--strict", str(artifacts[0])],
            check=True,
        )
        output.mkdir(parents=True, exist_ok=True)
        destination = output / artifacts[0].name
        with tempfile.NamedTemporaryFile(
            prefix=".sharpbind-", dir=output, delete=False
        ) as temporary_output:
            temporary_path = Path(temporary_output.name)
        try:
            shutil.copy2(artifacts[0], temporary_path)
            temporary_path.replace(destination)
        finally:
            temporary_path.unlink(missing_ok=True)
        return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--native-project", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--runtime-identifier")
    parser.add_argument("--module")
    parser.add_argument(
        "--local",
        action="store_true",
        help="Skip platform repair for local development",
    )
    options = parser.parse_args()
    print(
        build_package(
            options.project,
            options.native_project,
            options.output,
            release=not options.local,
            configuration=options.configuration,
            runtime_identifier=options.runtime_identifier,
            module=options.module,
        )
    )
