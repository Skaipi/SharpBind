# PYTHON_DISTRIBUTION

Python bindings for NativeBindings, including documented signatures and type stubs.

```shell
pip install PYTHON_DISTRIBUTION
```

NumPy is installed automatically when the generated API uses numeric arrays.
Exported instances support context managers and an explicit `close()` method.
Array results are ordinary NumPy arrays whose storage follows Python references.

To build a wheel, install .NET 9.0 or higher, Native AOT compiler prerequisites, Python 3.11 or later, and the Python packaging tools:

```shell
python -m pip install hatchling twine setuptools
dotnet msbuild native/NativeBindings.csproj -restore -t:SharpBindPackage -p:Configuration=Release
```

The SharpBind NuGet package includes all SharpBind build helpers. They use the
tools already installed in your Python environment. Set `SharpBindPythonExecutable` to select a particular interpreter.
NumPy is needed for numerical use, not for building the wheel.
The wheel includes private binding support and the compiled CPython adapter; no separate SharpBind Python distribution is required.
Hatchling assembles the wheel, and Setuptools compiles the adapter during the build.

Linux release builds also need `auditwheel>=6` and `patchelf>=0.14`; macOS needs
`delocate>=0.12`. Build Linux releases inside a manylinux environment.
The target repairs platform dependencies and validates the final wheel before
writing it to `dist`. Set `SharpBindLocalWheel=true` to skip repair for local use,
or `SharpBindWheelOutput` to change the output directory.

Build through MSBuild; direct `python -m build`, editable installs and source
distributions are unsupported. Installing and using a completed wheel requires
neither the .NET SDK nor the build tools.
