"""Host checks shared by the NuGet build helpers and bundled package support."""

import ctypes
import platform
import sys
import sysconfig
from dataclasses import dataclass


@dataclass(frozen=True)
class PlatformTarget:
    rid: str
    suffix: str
    wheel_platform: str

    def library_name(self, name):
        return name + self.suffix


_TARGETS = {
    ("Windows", "x86_64"): PlatformTarget("win-x64", ".dll", "win_amd64"),
    ("Linux", "x86_64"): PlatformTarget("linux-x64", ".so", "linux_x86_64"),
    ("Darwin", "arm64"): PlatformTarget("osx-arm64", ".dylib", "macosx_14_0_arm64"),
}


def current_target():
    """Resolve a supported native host for building or loading a wheel."""
    if sys.implementation.name != "cpython" or sysconfig.get_config_var(
        "Py_GIL_DISABLED"
    ):
        raise ValueError(
            "SharpBind requires conventional CPython; free-threaded Python is unsupported."
        )
    machine = platform.machine().lower()
    machine = {"amd64": "x86_64", "aarch64": "arm64"}.get(machine, machine)
    target = _TARGETS.get((platform.system(), machine))

    if target is None or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise ValueError(
            "SharpBind supports Windows x64, glibc Linux x64, and macOS ARM64."
        )

    if target.rid == "linux-x64":
        libc, version = platform.libc_ver()
        if libc != "glibc" or tuple(map(int, version.split(".")[:2])) < (2, 28):
            raise ValueError(
                "SharpBind Linux wheels require glibc 2.28 or later; musl is unsupported."
            )

    if target.rid == "osx-arm64":
        version = platform.mac_ver()[0]
        if not version or int(version.split(".")[0]) < 14:
            raise ValueError("SharpBind requires macOS 14 or later.")
    return target
