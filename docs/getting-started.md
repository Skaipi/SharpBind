# Getting started

Expose an ordinary public C# class from library project, then share a NumPy array with C#. 

## Prerequisites

Library authors need .NET 9.0 or higher with native build tools and CPython with development headers installed.
Install `hatchling`, `twine`, and `setuptools` in your Python environment.
Linux/macOS release builds also need the [platform repair tools](configuration.md#wheel-builds).
Users installing a compatible wheel need neither the SDK nor a compiler.

See Microsoft's [Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

## Create the C# API

```shell
dotnet new classlib -n Example
dotnet add Example/Example.csproj package SharpBind
dotnet new install SharpBind
python -m pip install hatchling twine setuptools
```

Create `Example/Counter.cs`:

```csharp
using SharpBind;

/// <summary>A running integer total.</summary>
[PythonExport]
public sealed class Counter
{
    private int total;

    /// <summary>Start a counter.</summary>
    /// <param name="initial">The starting total.</param>
    [PythonExport]
    public Counter(int initial = 0) => total = initial;

    /// <summary>Add to the total.</summary>
    /// <param name="amount">The amount to add.</param>
    [PythonExport]
    public int Add(int amount) => total += amount;
}
```

Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in the managed
project so these comments become Python docstrings.

## Build the Python package

From the directory containing `Example`:

```shell
dotnet new sharpbind-python -n ExampleNative -o python --module example_lib --library ../../Example/Example.csproj
cd python
dotnet msbuild native/ExampleNative.csproj -restore -t:SharpBindPackage -p:Configuration=Release
python -m pip install --find-links dist example-lib
```

The `--library` path is relative to the generated `python/native` directory.
The scaffold pins the SharpBind NuGet version. 
The wheel bundles its private Python support and CPython adapter.
NumPy is added automatically for numerical APIs.

```python
from example_lib import Counter

with Counter(10) as counter:
    assert counter.add(amount=3) == 13
```

## Share a NumPy array

Add this managed class:

```csharp
using SharpBind;

/// <summary>Operations on borrowed numeric storage.</summary>
public static class ArrayMath
{
    /// <summary>Add one without copying the array.</summary>
    /// <param name="values">A writable, contiguous float64 array.</param>
    [PythonExport]
    public static void Increment(TensorView<double> values)
    {
        for (long i = 0; i < values.Length; i++)
            values[i] += 1;
    }
}
```

Rebuild and reinstall the library:

```shell
dotnet msbuild native/ExampleNative.csproj -restore -t:SharpBindPackage -p:Configuration=Release
python -m pip install --force-reinstall --find-links dist example-lib
```

```python
import numpy as np
from example_lib import ArrayMath

values = np.arange(4, dtype=np.float64)
ArrayMath.increment(values)
np.testing.assert_array_equal(values, [1, 2, 3, 4])
```

The view borrows the original memory until the call returns. 
See [tensor ownership](python-runtime.md) for returned arrays and imported tensor access.
