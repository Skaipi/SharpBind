# Configuration and build integration

The native bridge owns native compilation. Managed libraries can also generate
Python wrappers during ordinary builds by setting `SharpBindPythonModuleName`.
Outputs default to `artifacts/sharpbind/<configuration>/<framework>/python/<module>`.

```xml
<PropertyGroup>
  <SharpBindPythonModuleName>example_lib</SharpBindPythonModuleName>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

```toml
[tool.sharpbind]
module = "example_lib"
```

Run the bridge's `SharpBindPackage` MSBuild target to produce the wheel.
Distribution name comes from `[project].name`, import name from `module`, and
native filename from the binding `AssemblyName`. Native export names incorporate
the input managed assembly simple name.

| Property | Purpose |
| --- | --- |
| SharpBindInputProject | Managed project to discover; add a matching ProjectReference |
| SharpBindStrictDocumentation | Reject missing XML summaries/parameter docs |
| SharpBindNativeOutput | Generated C#, header and schema directory |
| SharpBindPythonModuleName | Top-level ASCII import name |
| SharpBindPythonPackageOutput | Generated package staging directory |
| SharpBindPythonOutput | Root for the default Python output directory |
| SharpBindGeneratePython | Enable or disable generation after a managed build |
| SharpBindNativeLibraryName | Expected bridge filename for managed-build wrappers; defaults to AssemblyName + Native |
| SharpBindGeneratorPath | Contributor generator override |
| SharpBindPythonProject | Python project directory containing pyproject.toml; defaults to the bridge's parent |
| SharpBindPythonExecutable | Python executable path; defaults to python on PATH |
| SharpBindWheelOutput | Wheel output directory; defaults to the Python project's dist directory |
| SharpBindLocalWheel | Set true to skip release platform repair; architecture and metadata validation still run |

Setting the module name supplies a default package output; override it when needed.
Selection is exclusively through attributes; there is no external JSON binding configuration.

## Wheel builds

See [Getting started](getting-started.md#build-the-python-package) for build and install commands.
The packaging target uses the requested Configuration and validates that the RID
matches the Python build host. Paths in MSBuild properties are relative to the
bridge project. Its helpers ship in NuGet and use the selected Python environment;
install `hatchling`, `twine`, and `setuptools`, plus the platform repair tools below for releases. 
Hatchling assembles the wheel. Setuptools compiles the private CPython adapter and manages compiler discovery.
These are author build tools and are not consumer dependencies.
Building the adapter also requires the interpreter's development headers and a C compiler.

Build each wheel on its target OS and architecture using conventional 64-bit CPython 3.11–3.14.
For Linux releases, use a `manylinux_2_28` build environment.

| Release platform | Additional Python prerequisites |
| --- | --- |
| Windows x64 | None |
| Linux x64, glibc 2.28+ | `auditwheel>=6`, `patchelf>=0.14` |
| Apple Silicon, macOS 14+ | `delocate>=0.12` |

Pass `-p:SharpBindLocalWheel=true` to skip platform repair for local development.
Build generated binding projects through MSBuild; direct `python -m build`, editable
installs and source distributions are unsupported.

Packaging does not require an installed SharpBind Python package or NumPy.
The NuGet package supplies all private support sources; each wheel bundles its
own Python support and compiled adapter. Numerical APIs automatically declare
`numpy>=1.26` as a normal dependency. Scalar-only APIs have no NumPy dependency.
Consumer wheels use the CPython 3.11 Stable ABI (`cp311-abi3`) and are tested on
the supported Python versions. Arrays returned from managed tensor methods are
ordinary NumPy arrays with private owners retaining their native allocations.

## Native compilation

The bridge requires .NET 9.0 or higher with Native AOT.
The scaffold derives its target framework from the selected.
By default, `dotnet` selects the highest installed SDK; a `global.json` can pin it to an older version.
To pin the native framework, replace the scaffold's `TargetFramework` expression with a specific value such as `net9.0`.
Its input framework is selected by normal SDK project-reference resolution.
The NuGet targets supply unsafe compilation, shared-library output, marshalling,
and optimization defaults; the bridge project keeps its framework and AOT settings.
Explicit `SharpBindNativeDependency` items
stage extra shared libraries, whose format/architecture is validated.

Globalization remains the application's choice. Linux applications may need ICU;
include required native dependencies or deliberately choose invariant globalization.
RequiresDynamicCode declarations are rejected, and the scaffold treats Native AOT
warnings as errors. Authors must validate application-specific reflection, trimming
and dependency requirements.
