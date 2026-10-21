"""Platform selection rules shared by NuGet build helpers and the runtime."""

import unittest
from unittest.mock import patch
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "build/python"))
from sharpbind_pack import _platform


class PlatformTests(unittest.TestCase):
    platforms = _platform

    def target(
        self, system, machine, pointer_bytes=8, libc=("glibc", "2.28"), mac="14.0"
    ):
        with patch.object(
            self.platforms.platform, "system", return_value=system
        ), patch.object(
            self.platforms.platform, "machine", return_value=machine
        ), patch.object(
            self.platforms.ctypes, "sizeof", return_value=pointer_bytes
        ), patch.object(
            self.platforms.platform, "libc_ver", return_value=libc
        ), patch.object(
            self.platforms.platform, "mac_ver", return_value=(mac, (), "")
        ), patch.object(
            self.platforms.sysconfig, "get_config_var", return_value=0
        ):
            return self.platforms.current_target()

    def test_supported_targets_and_library_names(self):
        for system, machine, rid, suffix in [
            ("Windows", "AMD64", "win-x64", ".dll"),
            ("Linux", "x86_64", "linux-x64", ".so"),
            ("Darwin", "arm64", "osx-arm64", ".dylib"),
        ]:
            with self.subTest(system=system, machine=machine):
                target = self.target(system, machine)
                self.assertEqual(target.rid, rid)
                self.assertEqual(target.library_name("Example"), "Example" + suffix)

    def test_unsupported_architectures_are_actionable(self):
        for system, machine in [
            ("Windows", "ARM64"),
            ("Linux", "aarch64"),
            ("Darwin", "x86_64"),
        ]:
            with self.subTest(system=system, machine=machine), self.assertRaisesRegex(
                ValueError, "supports"
            ):
                self.target(system, machine)

    def test_32_bit_python_is_rejected(self):
        with self.assertRaises(ValueError):
            self.target("Windows", "AMD64", pointer_bytes=4)

    def test_unsupported_linux_libc_is_rejected(self):
        for libc in (("musl", "1.2"), ("glibc", "2.27")):
            with self.subTest(libc=libc), self.assertRaisesRegex(ValueError, "glibc"):
                self.target("Linux", "x86_64", libc=libc)

    def test_old_macos_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "macOS 14"):
            self.target("Darwin", "arm64", mac="13.0")

    def test_free_threaded_python_is_rejected(self):
        with patch.object(self.platforms.sysconfig, "get_config_var", return_value=1):
            with self.assertRaisesRegex(ValueError, "free-threaded"):
                self.platforms.current_target()


if __name__ == "__main__":
    unittest.main()
