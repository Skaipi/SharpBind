<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Skaipi/SharpBind/HEAD/docs/images/SharpBind_full_logo_dark.svg">
    <img src="https://raw.githubusercontent.com/Skaipi/SharpBind/HEAD/docs/images/SharpBind_full_logo.png" alt="SharpBind — C# bindings for Python" width="600">
  </picture>
</p>

# Your C# library, ready for Python

SharpBind turns annotated C# APIs into installable Python packages. It builds a
self-contained Native AOT library and generates the Python interface, so your users
can install a wheel with `pip` without installing the .NET runtime or a separate
SharpBind Python package. Numerical APIs use ordinary NumPy arrays.

[Getting started](https://skaipi.github.io/SharpBind/getting-started.html) · [Documentation](https://skaipi.github.io/SharpBind/) · [Examples and API guide](https://skaipi.github.io/SharpBind/python-runtime.html)

## What you can build

- **Reuse C# code in Python.** Export static methods, stateful classes, and named
  specializations of generic classes with `[PythonExport]`.
- **Offer a natural Python API.** Get snake_case methods, keyword arguments,
  defaults, type stubs, and docstrings generated from C# XML documentation.
- **Share numerical data without copying.** Work directly with NumPy arrays,
  CPU DLPack producers, and numeric buffers through tensor views. Return shared
  NumPy arrays or explicitly retain storage in C# for later use.
- **Control data and object lifetimes.** Choose copied numeric arrays or shared
  arrays; use Python context managers to dispose exported C# instances.
- **Ship wheels from MSBuild.** Scaffold a binding project, then generate,
  compile, package, and validate it with the `SharpBindPackage` target.

## C# in, Python out

Export a C# class that scales an array in place:

```csharp
using SharpBind;

[PythonExport]
public sealed class Scaler
{
    private readonly double factor;

    [PythonExport]
    public Scaler(double factor) => this.factor = factor;

    [PythonExport]
    public void ScaleInPlace(TensorView<double> values)
    {
        for (long i = 0; i < values.Length; i++)
            values[i] *= factor;
    }
}
```

After building and installing your generated `example_lib` package:

```python
import numpy as np
from example_lib import Scaler

values = np.arange(4, dtype=np.float64)

with Scaler(factor=2.0) as scaler:
    scaler.scale_in_place(values=values)

np.testing.assert_array_equal(values, [0, 2, 4, 6])
```

The C# method modifies the original NumPy storage. Writable tensor views require
an exact dtype and C-contiguous storage; read-only views also accept strided arrays.
Use `T[]` when you want a checked copy instead.

## Build your first package

Follow [Getting started](https://skaipi.github.io/SharpBind/getting-started.html) to annotate your API, scaffold
the Python project, and build a wheel. Authors need .NET 9.0 or higher, Native AOT build tools, CPython, and Python packaging tools; wheel users need no .NET SDK or compiler.

Release targets are Windows x64, Linux x64 (glibc 2.28+), and Apple Silicon macOS 14+, with conventional CPython 3.11–3.14.
Build a wheel on each target OS; see [platform requirements](https://skaipi.github.io/SharpBind/configuration.html#wheel-builds).

SharpBind supports synchronous, Native AOT-compatible APIs, scalar values, strings,
numeric arrays, and CPU tensors. Async methods, GPU tensors, callbacks, and general
object interchange are outside the current scope. See [Supported types](https://skaipi.github.io/SharpBind/supported-types.html)
and [Exporting APIs](https://skaipi.github.io/SharpBind/exporting-apis.html) to check your API's fit.

[Contributing](https://skaipi.github.io/SharpBind/contributing.html) · [Publishing](https://skaipi.github.io/SharpBind/publishing.html) · [Issues](https://github.com/Skaipi/SharpBind/issues) · [MIT license](https://github.com/Skaipi/SharpBind/blob/HEAD/LICENSE)

> **AI disclaimer:** Some parts of the documentation, tests, and build tools were created with AI tools and may contain small mistakes. Take them with a grain of salt.
