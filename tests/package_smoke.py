"""Build and test the source fixture, or validate the complete release workflow."""

import argparse
from email.parser import BytesParser
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile
import tomllib
import xml.etree.ElementTree as ET
import zipfile

from packaging.requirements import Requirement
from packaging.specifiers import SpecifierSet
from packaging.utils import canonicalize_name
from packaging.version import Version

ROOT = Path(__file__).resolve().parents[1]


def run(*args, cwd=ROOT, env=None):
    print("Running:", *map(str, args), flush=True)
    subprocess.run(list(map(str, args)), cwd=cwd, env=env, check=True)


def project_metadata(project):
    with (project / "pyproject.toml").open("rb") as source:
        return tomllib.load(source)["project"]


def inspect_wheel(wheel, target, project, temporary_export=False):
    expected = project_metadata(project)
    with zipfile.ZipFile(wheel) as archive:
        names = set(archive.namelist())

        native_library = f"sharpbind_fixture/_native/{target.rid}/{target.library_name('NativeFixture')}"
        required = {
            "sharpbind_fixture/__init__.py",
            "sharpbind_fixture/_bindings.py",
            "sharpbind_fixture/_bindings.pyi",
            "sharpbind_fixture/py.typed",
            native_library,
        }

        assert required <= names
        (metadata_root,) = {
            name.rsplit("/", 1)[0]
            for name in names
            if name.endswith(".dist-info/METADATA")
        }
        parser = BytesParser()
        wheel_metadata = parser.parsebytes(archive.read(metadata_root + "/WHEEL"))
        metadata = parser.parsebytes(archive.read(metadata_root + "/METADATA"))

        assert "cp311-abi3-" + target.wheel_platform in wheel_metadata.get_all(
            "Tag", []
        )
        assert wheel_metadata["Root-Is-Purelib"] == "false"
        assert canonicalize_name(metadata["Name"]) == canonicalize_name(
            expected["name"]
        )
        assert Version(metadata["Version"]) == Version(expected["version"])
        assert SpecifierSet(metadata["Requires-Python"]) == SpecifierSet(
            expected["requires-python"]
        )
        requirements = {
            Requirement(value) for value in metadata.get_all("Requires-Dist", [])
        }
        expected_requirements = {
            Requirement(value) for value in expected.get("dependencies", [])
        }
        assert (
            expected_requirements <= requirements
        ), f"Missing declared dependencies: {expected_requirements - requirements}"
        dependency_names = {canonicalize_name(value.name) for value in requirements}
        assert not dependency_names & {"sharpbind-build", "hatchling", "setuptools"}
        assert "sharpbind-runtime" not in dependency_names
        assert any(
            canonicalize_name(value.name) == "numpy" and value.marker is None
            for value in requirements
        ), "Numerical packages must declare NumPy."
        assert "sharpbind_fixture/_sharpbind/_result.py" in names
        assert any(
            name.startswith("sharpbind_fixture/_sharpbind/_buffer")
            and name.endswith((".so", ".pyd"))
            for name in names
        ), "The CPython adapter must be bundled."

        bindings = archive.read("sharpbind_fixture/_bindings.py").decode()
        assert ("temporary_value" in bindings) == temporary_export
        assert not any(
            name.endswith((".exe", ".pdb", ".cs", ".csproj", ".pyc")) for name in names
        )
        assert not any(
            "SharpBind.Generator" in name or "hatch_build" in name for name in names
        )
        assert all(
            name.startswith(("sharpbind_fixture/", metadata_root + "/"))
            for name in names
        )


def install_template(workspace):
    packages = workspace / "packages"
    run(
        "dotnet",
        "pack",
        ROOT / "src/SharpBind/SharpBind.csproj",
        "-c",
        "Release",
        "-o",
        packages,
    )
    (package,) = packages.glob("SharpBind.*.nupkg")
    with zipfile.ZipFile(package) as archive:
        assert {
            "tools/net9.0/SharpBind.Generator.dll",
            "tools/net9.0/SharpBind.Generator.deps.json",
            "tools/net9.0/SharpBind.Generator.runtimeconfig.json",
            "build/python/package.py",
            "build/python/sharpbind_pack/release.py",
            "build/python/sharpbind_pack/wheel.py",
            "python/common-runtime/native/adapter.c",
            "python/common-runtime/src/sharpbind_runtime/_result.py",
        } <= set(archive.namelist())
        nuspec = ET.fromstring(archive.read("SharpBind.nuspec"))
        version = nuspec.findtext("{*}metadata/{*}version")
        assert version is not None
    hive = workspace / "template-hive"
    run("dotnet", "new", "install", package, "--debug:custom-hive", hive)
    return packages, hive, version


def create_consumer(workspace, packages, hive, version):
    managed = workspace / "Managed"
    managed.mkdir()
    (managed / "Managed.csproj").write_text(
        f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Fixture</AssemblyName><TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile><NoWarn>CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="SharpBind" Version="[{version}]" /></ItemGroup>
</Project>
""",
        encoding="utf-8",
    )
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="local", value=str(packages))
    ET.SubElement(
        sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json"
    )
    mapping = ET.SubElement(config, "packageSourceMapping")
    ET.SubElement(mapping, "clear")
    ET.SubElement(
        ET.SubElement(mapping, "packageSource", key="local"),
        "package",
        pattern="SharpBind",
    )
    ET.SubElement(
        ET.SubElement(mapping, "packageSource", key="nuget.org"), "package", pattern="*"
    )
    settings = ET.SubElement(config, "config")
    ET.SubElement(
        settings,
        "add",
        key="globalPackagesFolder",
        value=str(workspace / "nuget-cache"),
    )
    ET.ElementTree(config).write(
        workspace / "NuGet.Config", encoding="utf-8", xml_declaration=True
    )
    project = workspace / "python"
    run(
        "dotnet",
        "new",
        "sharpbind-python",
        "-n",
        "NativeFixture",
        "-o",
        project,
        "--library",
        "../../Managed/Managed.csproj",
        "--module",
        "sharpbind_fixture",
        "--debug:custom-hive",
        hive,
    )
    bridge = project / "native/NativeFixture.csproj"
    properties = json.loads(
        subprocess.check_output(
            [
                "dotnet",
                "msbuild",
                str(bridge),
                "-getProperty:TargetFramework,NETCoreSdkVersion",
            ],
            cwd=project,
            text=True,
        )
    )["Properties"]
    expected_framework = f"net{properties['NETCoreSdkVersion'].split('.')[0]}.0"
    assert properties["TargetFramework"] == expected_framework, properties
    print(f"Native AOT target framework: {expected_framework}", flush=True)
    tree = ET.parse(bridge)
    ET.SubElement(
        tree.getroot().find("PropertyGroup"), "SharpBindStrictDocumentation"
    ).text = "true"
    tree.write(bridge, encoding="utf-8", xml_declaration=True)
    return managed, project


def create_source_project(workspace):
    """Use project references and skip NuGet packing and template installation."""
    run("dotnet", "build", ROOT / "src/SharpBind.Generator", "-c", "Release")
    project = workspace / "python"
    shutil.copytree(
        ROOT / "tests/python",
        project,
        ignore=shutil.ignore_patterns("artifacts", "dist", "__pycache__"),
    )
    return project


def build_fixture(project, environment, *, temporary_export=False):
    native_project = project / "native/NativeFixture.csproj"
    project_arguments = []
    if not native_project.exists():
        native_project = ROOT / "tests/NativeFixture/NativeFixture.csproj"
        project_arguments.append(f"-p:SharpBindPythonProject={project}")
    run(
        "dotnet",
        "msbuild",
        native_project,
        "-restore",
        "-t:SharpBindPackage",
        "-p:Configuration=Release",
        f"-p:SharpBindPythonExecutable={sys.executable}",
        "-p:SharpBindLocalWheel=true",
        *project_arguments,
        cwd=project,
        env=environment,
    )
    (wheel,) = (project / "dist").glob("sharpbind_fixture-*.whl")
    inspect_wheel(wheel, current_target(), project, temporary_export=temporary_export)
    return wheel


def build_template_fixture(workspace, environment):
    packages, hive, version = install_template(workspace)
    managed, project = create_consumer(workspace, packages, hive, version)
    exports = (ROOT / "tests/Fixture/Exports.cs").read_text(encoding="utf-8")
    for source in (ROOT / "tests/Fixture").glob("*.cs"):
        shutil.copy2(source, managed / source.name)
    example_source = ROOT / "examples/Exports.cs"
    if example_source.exists():
        shutil.copy2(example_source, managed / "Example.cs")
    temporary_export = """
/// <summary>Temporary exported class.</summary>
public static class TemporaryExports {
    /// <summary>Return one.</summary>
    [SharpBind.PythonExport] public static int TemporaryValue() => 1;
}
"""
    facade = project / "src/sharpbind_fixture/__init__.py"
    facade.write_text(
        facade.read_text(encoding="utf-8") + '\nhandwritten_marker = "preserved"\n',
        encoding="utf-8",
    )
    handwritten = facade.read_bytes()
    (managed / "Exports.cs").write_text(exports + temporary_export, encoding="utf-8")
    build_fixture(project, environment, temporary_export=True)
    (managed / "Exports.cs").write_text(exports, encoding="utf-8")
    wheel = build_fixture(project, environment)
    assert facade.read_bytes() == handwritten
    assert not (facade.parent / "_bindings.py").exists()
    return wheel


def create_python_project(project, module):
    source = project / "src" / module
    source.mkdir(parents=True)
    (source / "__init__.py").write_text("from ._bindings import *\n", encoding="utf-8")
    (project / "README.md").write_text(
        f"# {module} packaging fixture\n", encoding="utf-8"
    )
    (project / "pyproject.toml").write_text(
        f'[project]\nname = "{module.replace("_", "-")}"\nversion = "0.1.0"\nrequires-python = ">=3.11"\nreadme = "README.md"\n'
        '[tool.hatch.build.targets.wheel]\nonly-include = ["src"]\nsources = ["src"]\n'
        f'[tool.sharpbind]\nmodule = "{module}"\n',
        encoding="utf-8",
    )


def build_peer(workspace, wheels, full):
    project = workspace / "peer"
    create_python_project(project, "sharpbind_peer")
    bridge = (
        workspace / "python/native/NativeFixture.csproj"
        if full
        else ROOT / "tests/NativeFixture/NativeFixture.csproj"
    )
    run(
        "dotnet",
        "msbuild",
        bridge,
        "-restore",
        "-t:SharpBindPackage",
        "-p:Configuration=Release",
        f"-p:SharpBindPythonExecutable={sys.executable}",
        f"-p:SharpBindPythonProject={project}",
        "-p:SharpBindPythonModuleName=sharpbind_peer",
        "-p:SharpBindLocalWheel=true",
    )
    (wheel,) = (project / "dist").glob("sharpbind_peer-*.whl")
    shutil.copy2(wheel, wheels / wheel.name)


def build_scalar(workspace, wheels, full):
    project = workspace / "scalar"
    create_python_project(project, "sharpbind_scalar")
    managed = workspace / "ScalarManaged"
    managed.mkdir()
    version = ET.parse(ROOT / "src/SharpBind/SharpBind.csproj").findtext(
        "PropertyGroup/Version"
    )
    reference = (
        f'<PackageReference Include="SharpBind" Version="[{version}]" />'
        if full
        else f'<ProjectReference Include="{(ROOT / "src/SharpBind/SharpBind.csproj").as_posix()}" />'
    )
    (managed / "ScalarManaged.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>'
        "<GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup>"
        f"<ItemGroup>{reference}</ItemGroup></Project>",
        encoding="utf-8",
    )
    (managed / "ScalarApi.cs").write_text(
        "/// <summary>Scalar-only public API.</summary>\n"
        "public static class ScalarApi {\n"
        "/// <summary>Return the supplied value.</summary>\n"
        '/// <param name="value">The value to return.</param>\n'
        "[SharpBind.PythonExport] public static int Echo(int value) => value;\n}\n",
        encoding="utf-8",
    )
    template = (
        workspace / "python/native/NativeFixture.csproj"
        if full
        else ROOT / "tests/NativeFixture/NativeFixture.csproj"
    )
    tree = ET.parse(template)
    properties = tree.getroot().find("PropertyGroup")
    properties.find("SharpBindInputProject").text = (
        "../../ScalarManaged/ScalarManaged.csproj"
    )
    properties.find("SharpBindPythonProject").text = ".."
    if not full:
        properties.find("SharpBindGeneratorPath").text = (
            ROOT / "src/SharpBind.Generator/bin/Release/net9.0/SharpBind.Generator.dll"
        ).as_posix()
        for imported in tree.getroot().findall("Import"):
            imported.set("Project", (ROOT / "build/SharpBind.targets").as_posix())
    for referenced in tree.getroot().findall(".//ProjectReference"):
        referenced.set("Include", "$(SharpBindInputProject)")
    native = project / "native"
    native.mkdir()
    bridge = native / "ScalarNative.csproj"
    tree.write(bridge, encoding="utf-8", xml_declaration=True)
    run(
        "dotnet",
        "msbuild",
        bridge,
        "-restore",
        "-t:SharpBindPackage",
        "-p:Configuration=Release",
        f"-p:SharpBindPythonExecutable={sys.executable}",
        "-p:SharpBindLocalWheel=true",
    )
    (wheel,) = (project / "dist").glob("sharpbind_scalar-*.whl")
    with zipfile.ZipFile(wheel) as archive:
        metadata = BytesParser().parsebytes(
            archive.read("sharpbind_scalar-0.1.0.dist-info/METADATA")
        )
        assert not metadata.get_all("Requires-Dist", [])
        assert b"numpy" not in archive.read("sharpbind_scalar/_bindings.py")
    shutil.copy2(wheel, wheels / wheel.name)


def preserve_generated_fixture(wheel):
    """Keep the last successfully tested wrappers at a predictable location."""
    destination = ROOT / "artifacts/fixtures/sharpbind_fixture"
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(wheel) as archive:
        for name in ("_bindings.py", "_bindings.pyi", "py.typed"):
            (destination / name).write_bytes(archive.read(f"sharpbind_fixture/{name}"))


def current_target():
    platform = runpy.run_path(str(ROOT / "build/python/sharpbind_pack/_platform.py"))
    return platform["current_target"]()


def repair_and_audit(wheels, workspace, target):
    sys.path.insert(0, str(ROOT / "build/python"))
    from sharpbind_pack.release import repair_wheel

    repaired = workspace / "repaired"
    repaired.mkdir()
    for wheel in wheels.glob("*.whl"):
        repair_wheel(wheel, repaired, target)
    for wheel in repaired.glob("*.whl"):
        run(sys.executable, "-m", "abi3audit", "--strict", wheel)
        run(sys.executable, "-m", "twine", "check", "--strict", wheel)
    return repaired


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--full",
        action="store_true",
        help="Also pack NuGet, scaffold from the template, and rebuild after removing an export.",
    )
    parser.add_argument(
        "--audit",
        action="store_true",
        help="Repair platform wheels and verify the Limited API (implies --full).",
    )
    options = parser.parse_args()
    target = current_target()
    staging = ROOT / "artifacts/package-smoke"
    staging.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix="consumer spaces ", dir=staging))
    wheels = workspace / "wheels"
    wheels.mkdir()
    environment = dict(os.environ)
    full = options.full or options.audit
    if full:
        wheel = build_template_fixture(workspace, environment)
    else:
        wheel = build_fixture(create_source_project(workspace), environment)
    shutil.copy2(wheel, wheels / wheel.name)
    build_peer(workspace, wheels, full)
    build_scalar(workspace, wheels, full)
    if options.audit:
        wheels = repair_and_audit(wheels, workspace, target)
    run(
        sys.executable,
        ROOT / "tests/install_wheels.py",
        "--wheels",
        wheels,
        "--output",
        workspace / "consumer-test",
    )
    preserve_generated_fixture(wheel)
    # A manifest pins this run's files; later checks must not choose an arbitrary old wheel.
    report = {
        "rid": target.rid,
        "wheels": str(wheels),
        "workspace": str(workspace),
        "audited": options.audit,
        "full": full,
    }
    (ROOT / "artifacts/latest-build.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as destination:
            print(f"wheels={wheels.as_posix()}", file=destination)
            if full:
                print(f"nuget={(workspace / 'packages').as_posix()}", file=destination)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
