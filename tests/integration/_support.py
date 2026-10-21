"""Small helpers shared by tests of the installed fixture."""

import os
from pathlib import Path
import subprocess
import sys
import time

from sharpbind_fixture import _bindings

PROVIDER = _bindings._provider.native


def method(class_name, method_name):
    return next(
        method
        for exported_class in PROVIDER.schema["classes"]
        if exported_class["name"] == class_name
        for method in exported_class["methods"]
        if method["name"] == method_name
    )


def native(class_name, method_name):
    PROVIDER._load()
    return getattr(PROVIDER.lib, method(class_name, method_name)["entry"])


def run_scenario(name, *arguments):
    """Bound scenarios that could deadlock or retain a native library until exit."""
    subprocess.run(
        [
            sys.executable,
            "-I",
            str(Path(__file__).with_name("_scenarios.py")),
            name,
            *map(str, arguments),
        ],
        env=dict(os.environ, PATH="", PYTHONPATH="", DOTNET_ROOT=""),
        check=True,
        timeout=30,
    )


def wait_until(predicate, description, timeout=10):
    deadline = time.monotonic() + timeout
    while not predicate():
        if time.monotonic() >= deadline:
            raise AssertionError(f"Timed out waiting for {description}")
        time.sleep(0.001)
