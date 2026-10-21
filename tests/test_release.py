"""Release failures must never replace a previously working distribution."""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "build/python"))
from sharpbind_pack import release
from sharpbind_pack._platform import PlatformTarget


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="SharpBind release spaces ")
        self.addCleanup(temporary.cleanup)
        self.project = Path(temporary.name)
        (self.project / "pyproject.toml").write_text(
            '[tool.sharpbind]\nmodule = "example"\n', encoding="utf-8"
        )
        self.native_project = self.project / "Bridge.csproj"
        self.native_project.touch()
        self.output = self.project / "dist"
        self.output.mkdir()
        self.name = "example-0.1.0-py3-none-win_amd64.whl"
        (self.output / self.name).write_bytes(b"previous successful wheel")
        self.enterContext(
            patch.object(
                release,
                "current_target",
                return_value=PlatformTarget("win-x64", ".dll", "win_amd64"),
            )
        )
        self.enterContext(patch.object(release, "check_prerequisites"))
        self.enterContext(
            patch.object(release, "build_wheel", side_effect=self.simulate_build)
        )

    def simulate_build(
        self, project, config, native_project, module, staging, target, configuration
    ):
        raw = staging / "raw"
        raw.mkdir()
        result = raw / self.name
        result.write_bytes(b"new wheel")
        return result

    def build(self, **kwargs):
        return release.build_package(self.project, self.native_project, **kwargs)

    def test_success_replaces_only_the_validated_wheel(self):
        with patch.object(release.subprocess, "run") as run:
            result = self.build()
        self.assertEqual(result.read_bytes(), b"new wheel")
        self.assertTrue(any("twine" in call.args[0] for call in run.call_args_list))
        self.assertEqual(list(self.output.iterdir()), [result])

    def test_failed_release_preserves_previous_output(self):
        for target, step in (
            (release, "build_wheel"),
            (release, "repair_wheel"),
            (release.subprocess, "run"),
        ):
            with self.subTest(step=step), patch.object(
                release.subprocess, "run"
            ), patch.object(
                target, step, side_effect=subprocess.CalledProcessError(1, step)
            ):
                with self.assertRaises(subprocess.CalledProcessError):
                    self.build()
                self.assertEqual(
                    (self.output / self.name).read_bytes(), b"previous successful wheel"
                )

    def test_local_build_skips_repair(self):
        with patch.object(release.subprocess, "run"), patch.object(
            release, "repair_wheel", side_effect=AssertionError("unexpected repair")
        ):
            result = self.build(release=False)
        self.assertEqual(result.read_bytes(), b"new wheel")

    def test_missing_prerequisites_skip_build_and_preserve_previous_output(self):
        with patch.object(
            release,
            "check_prerequisites",
            side_effect=RuntimeError("Install Hatchling"),
        ), patch.object(release, "build_wheel") as build:
            with self.assertRaisesRegex(RuntimeError, "Install Hatchling"):
                self.build()
        build.assert_not_called()
        self.assertEqual(
            (self.output / self.name).read_bytes(), b"previous successful wheel"
        )

    def test_linux_repair_finds_tools_in_selected_python_environment(self):
        target = PlatformTarget("linux-x64", ".so", "linux_x86_64")
        with patch.object(release.subprocess, "run") as run, patch.object(
            release.sysconfig, "get_path", return_value="/chosen/venv/bin"
        ):
            release.repair_wheel(
                self.output / self.name, self.project / "repaired", target
            )
        command = run.call_args.args[0]
        self.assertEqual(command[:4], [sys.executable, "-m", "auditwheel", "repair"])
        self.assertTrue(
            run.call_args.kwargs["env"]["PATH"].startswith("/chosen/venv/bin")
        )


class PrerequisiteTests(unittest.TestCase):
    def test_missing_tools_report_install_command_for_selected_interpreter(self):
        target = PlatformTarget("win-x64", ".dll", "win_amd64")
        with patch.object(
            release.metadata,
            "version",
            side_effect=release.metadata.PackageNotFoundError,
        ):
            with self.assertRaisesRegex(RuntimeError, "-m pip install") as error:
                release.check_prerequisites(target, True)
        self.assertIn(sys.executable, str(error.exception))
        self.assertNotIn("numpy", str(error.exception))

    def test_unsupported_build_tool_version_is_rejected(self):
        target = PlatformTarget("win-x64", ".dll", "win_amd64")
        for name, version in (("hatchling", "2.0"), ("setuptools", "76.0")):
            versions = {"hatchling": "1.27", "twine": "6.0", "setuptools": "77.0"}
            versions[name] = version
            with self.subTest(tool=name), patch.object(
                release.metadata, "version", side_effect=versions.__getitem__
            ):
                with self.assertRaisesRegex(RuntimeError, name):
                    release.check_prerequisites(target, False)

    def test_missing_setuptools_reports_build_install_command(self):
        target = PlatformTarget("win-x64", ".dll", "win_amd64")

        def installed_version(name):
            if name == "setuptools":
                raise release.metadata.PackageNotFoundError(name)
            return {"hatchling": "1.27", "twine": "6.0"}[name]

        with patch.object(release.metadata, "version", side_effect=installed_version):
            with self.assertRaisesRegex(RuntimeError, "Missing.*setuptools") as error:
                release.check_prerequisites(target, False)
        self.assertIn('"setuptools>=77"', str(error.exception))

    def test_local_linux_build_does_not_require_release_repair_tools(self):
        target = PlatformTarget("linux-x64", ".so", "linux_x86_64")
        versions = {"hatchling": "1.27", "twine": "6.0", "setuptools": "77.0"}
        with patch.object(
            release.metadata, "version", side_effect=lambda name: versions[name]
        ):
            release.check_prerequisites(target, False)

    def test_linux_release_checks_repair_prerequisites_before_building(self):
        target = PlatformTarget("linux-x64", ".so", "linux_x86_64")
        with patch.object(
            release.metadata,
            "version",
            side_effect=release.metadata.PackageNotFoundError,
        ):
            with self.assertRaisesRegex(RuntimeError, "auditwheel.*patchelf"):
                release.check_prerequisites(target, True)


if __name__ == "__main__":
    unittest.main()
