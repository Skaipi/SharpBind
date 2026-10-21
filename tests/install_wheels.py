"""Test exactly the wheels supplied by a build, in a fresh consumer environment."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def run(*args, **kwargs):
    subprocess.run([str(a) for a in args], check=True, **kwargs)


def install_packages(root, fixture, peer):
    run(sys.executable, "-m", "venv", root / "venv")
    executable = (
        root / "venv" / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    )
    # Resolve the ordinary NumPy dependency first, then install the actual packages offline.
    dependencies = root / "dependencies"
    run(
        executable,
        "-m",
        "pip",
        "download",
        "--only-binary=:all:",
        "--dest",
        dependencies,
        "numpy>=1.26",
    )
    run(
        executable,
        "-m",
        "pip",
        "install",
        "--no-index",
        "--find-links",
        dependencies,
        fixture,
        peer,
    )
    return executable


def test_scalar_package(root, wheel):
    environment = root / "scalar-venv"
    run(sys.executable, "-m", "venv", environment)
    executable = environment / (
        "Scripts/python.exe" if os.name == "nt" else "bin/python"
    )
    run(executable, "-m", "pip", "install", "--no-index", wheel)
    run(
        executable,
        "-I",
        "-c",
        "import importlib.util; import sys; "
        "from sharpbind_scalar import ScalarApi; "
        "assert ScalarApi.echo(42) == 42; "
        'assert importlib.util.find_spec("numpy") is None; '
        'assert importlib.util.find_spec("sharpbind_runtime") is None; '
        'assert "sharpbind_scalar._sharpbind._buffer" not in sys.modules',
        env=dict(os.environ, PATH="", PYTHONPATH="", DOTNET_ROOT="", PIP_NO_INDEX="1"),
    )


def run_integration_tests(root, executable):
    tests = root / "tests"
    shutil.copytree(
        ROOT / "tests/integration",
        tests,
        ignore=shutil.ignore_patterns("__pycache__"),
        dirs_exist_ok=True,
    )
    environment = dict(
        os.environ,
        PATH="",
        PYTHONPATH="",
        DOTNET_ROOT="",
        PIP_NO_INDEX="1",
    )
    run(
        executable,
        "-I",
        "-m",
        "unittest",
        "discover",
        "-s",
        tests,
        "-v",
        cwd=tests,
        env=environment,
    )
    example_source = ROOT / "examples/example.py"
    if example_source.exists():
        example = tests / "example.py"
        example.write_text(
            example_source.read_text(encoding="utf-8").replace(
                "from example_lib import ", "from sharpbind_fixture import "
            ),
            encoding="utf-8",
        )
        run(executable, "-I", example, cwd=tests, env=environment)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wheels", type=Path, required=True)
    parser.add_argument("--output", type=Path)

    args = parser.parse_args()
    root = (
        args.output or Path(tempfile.mkdtemp(prefix="sharpbind-consumer-"))
    ).resolve()
    root.mkdir(parents=True, exist_ok=True)

    wheels = args.wheels.resolve()
    (peer,) = wheels.glob("sharpbind_peer-*.whl")
    (fixture,) = wheels.glob("sharpbind_fixture-*.whl")
    (scalar,) = wheels.glob("sharpbind_scalar-*.whl")

    test_scalar_package(root, scalar)
    executable = install_packages(root, fixture, peer)
    run_integration_tests(root, executable)
    print(
        json.dumps(
            {
                "python": sys.version.split()[0],
                "peer": peer.name,
                "fixture": fixture.name,
                "scalar": scalar.name,
                "passed": True,
            }
        ),
        flush=True,
    )


if __name__ == "__main__":
    main()
