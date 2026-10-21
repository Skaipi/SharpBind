# Using generated Python APIs

This guide explains how to call a SharpBind-generated package from Python: which values you can pass, when data is copied, and when to close objects.
Examples use the `example_lib` package from [Getting started](getting-started.md).

Generated methods have Python signatures, keyword arguments, and type stubs for editor support.
Use `help(Counter.add)` or your editor to inspect a method's parameters and documentation.
The underlying C# type still determines numeric ranges and array requirements, as described below.

## Create and close an instance

Use a context manager to release a managed instance when you finish with it:

```python
from example_lib import Counter

with Counter(10) as counter:
    assert counter.add(amount=3) == 13
    assert not counter.closed

assert counter.closed
```

You can also call `counter.close()` explicitly, for example in a `finally` block.
Closing calls the managed object's `Dispose()` method if it implements
`IDisposable`, then releases the instance. Calling `close()` again is safe.
Calling an instance method after closing raises `RuntimeError`.

Static methods such as `ArrayMath.increment(values)` do not create an instance
and need no `close()`. Managed instances cannot be copied with `copy.copy()` or
`copy.deepcopy()`, or serialized with `pickle`.

## Pass Python values

SharpBind checks arguments before calling the C# method. It rejects values that
would require silently truncating an integer or wrapping an out-of-range number.

| C# parameter | What you can pass | Conversion rules |
| --- | --- | --- |
| Integer, such as `int` or `long` | A Python `int` or an object with `__index__()`, including NumPy integers | The value must fit the C# type. A float such as `3.0` raises `TypeError`; an out-of-range integer raises `OverflowError`. |
| `float` or `double` | Real numbers convertible with `float()` | Strings, bytes, and complex numbers are rejected. Finite values outside the target range raise `OverflowError`. Normal floating-point rounding applies; NaN and infinity are supported. |
| `bool` | `True`, `False`, or an integer-like value equal to `0` or `1` | This does not use Python truthiness. `2` raises `ValueError`, and `"yes"` raises `TypeError`. |
| `string` | A Python `str` | Text is copied using strict UTF-8 encoding. Bytes are not decoded automatically. |
| Nullable string or numeric array | An accepted value or `None` | `None` is allowed only when the generated signature permits it. It is different from an empty string or array. |

For example, a Python parameter annotated as `int` may represent a C# `int`, whose
range is `-2**31` through `2**31 - 1`. Python's unlimited integer precision does not
extend that range. See [Supported types](supported-types.md) for the full mapping.

### Custom argument objects

Converting an argument can itself execute Python code. For example, when you pass
an integer-like object, SharpBind calls its `__index__()` method to obtain the
integer. A DLPack input similarly runs the producer's `__dlpack__()` method.

These hooks run before the C# method starts. A hook can call another method on
the same instance; that call can complete before the original one begins. If a
hook closes the instance, the original call raises `RuntimeError` without running
its C# method:

```python
from example_lib import Counter

with Counter(0) as counter:
    class ClosingValue:
        def __index__(self):
            counter.close()
            return 1

    try:
        counter.add(ClosingValue())
    except RuntimeError:
        assert counter.closed  # add() never entered its C# method
```

This matters when implementing custom conversion hooks. Ordinary calls such as
`counter.add(1)` need no special handling.

## Choose between copied and shared arrays

An array parameter's C# type determines whether changes affect your original
Python data. Here, `T` is the numeric element type, such as `double`.

| C# parameter | Copy or share? | Input requirements | How long C# can use it |
| --- | --- | --- | --- |
| `T[]` | Copies elements into a managed array | One-dimensional numeric input, including lists; elements are converted with range checks | The managed copy is independent of Python storage. |
| `ReadOnlyTensorView<T>` | Shares storage for reading | CPU storage with the exact dtype; slices, transposes, and reversed views are accepted | Until the method returns. |
| `TensorView<T>` | Shares storage for reading and writing | CPU storage with the exact dtype, writable and C-contiguous | Until the method returns. |
| `Tensor<T>` | Shares storage through a separate managed handle | CPU storage with the exact dtype; writing additionally requires writable, C-contiguous storage | Until the method returns, unless C# explicitly retains another handle. |

For shared inputs, pass a NumPy array, a CPU DLPack
producer, or another supported numeric buffer. A Python list has no numeric
buffer to share; convert it to an array first. GPU storage is unsupported.

### Pass a copied vector

A `T[]` input always produces a managed copy. A `T[]` return value becomes an
independent one-dimensional NumPy array. Changes made to the C# input array do
not modify the Python source.

For example, if your library exports `double[] Echo(double[] values) => values` as
`ArrayMath.echo`, you can pass either a list or a one-dimensional array:

```python
import numpy as np
from example_lib import ArrayMath

result = ArrayMath.echo([1, 2, 3])
assert result.dtype == np.float64

source = np.arange(4, dtype=np.float64)
result = ArrayMath.echo(source)
source[0] = 99
assert result[0] == 0
```

Noncontiguous vectors are accepted because this path copies the data. When the
dtype differs, each element follows the scalar conversion rules: for example,
`[1.5]` cannot be passed to an `int[]` parameter. Multidimensional inputs raise
`ValueError`; use a tensor parameter for matrices and higher-dimensional data.

### Share an array for in-place changes

The `ArrayMath.increment` method from [Getting started](getting-started.md#share-a-numpy-array)
takes a `TensorView<double>`. It updates the original NumPy array:

```python
import numpy as np
from example_lib import ArrayMath

values = np.arange(4, dtype=np.float64)
ArrayMath.increment(values)
np.testing.assert_array_equal(values, [1, 2, 3, 4])
```

Shared inputs are validated without automatically casting or copying them:

| Requirement | Example of a rejected input | How to prepare suitable storage |
| --- | --- | --- |
| Exact native dtype | A `float32` array for `TensorView<double>` | Use `np.asarray(values, dtype=np.float64)`. |
| C-contiguous layout for writing | `values[::2]` or a reversed vector | Use `np.ascontiguousarray(values)`. |
| Writable storage for writing | An array with `flags.writeable == False` | Use `values.copy()` to create writable storage. |

C-contiguous means elements are stored consecutively in C (row-major) order.
These preparation steps may create a new array. Pass the prepared array and read
changes from it; a copy's changes do not propagate back to its source.

Read-only tensor parameters accept strided and reversed layouts without copying.
An individual C# algorithm can still require contiguous input, for example if it
uses `AsSpan()` internally; check that method's documentation.

Keep shared storage the same size and avoid changing it through another array
or thread while C# is using it. Read-only access in C# does not prevent another
Python reference from modifying the same storage.

## Work with returned arrays

A C# method returning `Tensor<T>` produces an ordinary NumPy `ndarray` sharing the returned native storage.
Python users do not construct or close tensor wrappers.
Use the standard array properties `shape`, `strides`, `dtype`, and `flags.writeable`, and call `array.copy()` when independent storage is needed.

For example, add this method to an exported C# class:

```csharp
[PythonExport]
public static Tensor<double> Create() => Tensor<double>.FromArray([1, 2, 3]);
```

The corresponding Python method returns an array:

```python
import numpy as np
from example_lib import ArrayMath

values = ArrayMath.create()
assert isinstance(values, np.ndarray)
view = values[::2]
independent = values.copy()
del values
assert view[0] == 1  # the view keeps the native allocation alive
```

A private buffer owner retains the allocation and native library until the last array, view, or retained managed handle releases it.
Read-only native storage produces a read-only array.
Array lifetime follows ordinary Python references; there is no separate array `close()` operation.

### Exchange storage with DLPack

Generated methods accept CPU objects implementing `__dlpack__` and `__dlpack_device__`.
Pass the producer directly to a tensor parameter.
Legacy and versioned DLPack inputs are supported.
GPU devices and imports requiring an implicit copy are rejected.

Returned arrays use NumPy's DLPack interface, so compatible consumers can share their storage.
A consumer retains its own storage reference.
Packages generated by different SharpBind versions communicate through ordinary array and buffer protocols, without sharing implementation classes.

### Package dependencies

Each wheel bundles its private Python support and CPython adapter alongside the native library.
There is no separately installed SharpBind Python package.
The wheel builder automatically declares NumPy for APIs with numeric arrays or tensor parameters/results.
Scalar-only packages do not depend on NumPy.

## Handle errors

Input conversion and managed execution use different exception types:

| Exception | What it means |
| --- | --- |
| `TypeError` | An argument has an unsupported Python type, or `None` violates its contract. |
| `ValueError` | A value has the wrong form, such as `2` for a boolean or a matrix for a vector parameter. |
| `OverflowError` | A Python input does not fit the target numeric type or supported size. |
| `BufferError` | Storage cannot be shared as requested, for example because its dtype, layout, write access, or device is unsuitable. |
| `RuntimeError` | A managed execution failure, invalid binding, closed object, or forbidden reentrant call. |
| `MemoryError` | A native allocation failed. |
| `NotImplementedError` | A required platform or native capability is unavailable. |

Managed argument failures use `ValueError`; managed arithmetic overflow uses `OverflowError`.
An input conversion failure prevents the C# method from running.
A method that has started can already have modified its instance or shared inputs; calls do not roll back those changes.

Exceptions reported by managed code also carry `category` and `managed_type`
attributes for diagnostics:

```python
try:
    result = ArrayMath.create()
except RuntimeError as error:
    print(str(error))
    print(getattr(error, "managed_type", ""))
```

## Use instances from multiple threads

By default, native calls on the same managed instance run one at a time.
A library author can enable overlapping calls with `[PythonExport(ConcurrentCalls = true)]` if the class synchronizes its own state.
Static methods have no per-instance serialization; the author must protect any shared static state.

If another thread calls `close()`, new calls are rejected and closing waits for active calls to finish before releasing the instance.
Calling back into the same instance, or closing it, from the same thread during an active native call or disposal raises `RuntimeError` to avoid waiting on itself.
This is different from the [argument conversion hooks](#custom-argument-objects), which run before the native call starts.

Instance serialization does not protect shared arrays from changes made through other references. Coordinate access to those arrays separately.

## Access imported tensor storage from C#

The remaining sections are for library authors implementing tensor methods.

`ReadOnlyTensorView<T>` and `TensorView<T>` borrow Python storage only until the exported method returns.
Use their indexers for logical element access, including strided reads.
Use `AsSpan()` for contiguous algorithms; read-only `AsSpan()` rejects strided input, and spans cannot contain more than `int.MaxValue` elements.
Keep views and spans inside the call that borrowed them.

Use `Tensor<T>` instead when managed code needs a storage reference that it can explicitly retain beyond the call.

## Retain tensor inputs in managed objects

A `Tensor<T>` parameter gives your method a temporary managed handle to the input storage.
The binding disposes that handle when the call ends, including when the method throws.
This does not close the Python input object.

If you want to store the input in a field, call `Retain()` and store the new
handle.
Assigning the parameter itself to a field does not extend its lifetime.
Dispose the retained handle when your object finishes using it:

```csharp
using SharpBind;

[PythonExport]
public sealed class Dataset : IDisposable
{
    private readonly Tensor<double> values;

    [PythonExport]
    public Dataset(Tensor<double> input) => values = input.Retain();

    [PythonExport]
    public double Sum() => values.Read(view =>
    {
        double total = 0;
        for (long i = 0; i < view.Length; i++)
            total += view[i];
        return total;
    });

    public void Dispose() => values.Dispose();
}
```

```python
import numpy as np
from example_lib import Dataset
values = np.arange(4, dtype=np.float64)
with Dataset(values) as dataset:
    del values                # the dataset retains the original storage
    assert dataset.sum() == 6
# Leaving the block disposes Dataset and releases its retained handle.
```

You can also pass a NumPy array or another supported CPU buffer directly to `Dataset`.
Retaining keeps the imported buffer and its exporter alive without copying elements.
Changes through other references remain visible to the dataset.
Do not resize or concurrently modify the shared storage.

Each `Retain()` creates an independently disposable handle. Repeated `Dispose()` is safe.
Element access and further `Retain()` calls through a disposed or transferred handle fail; metadata such as `Length`, `Shape`, `Strides`, `IsReadOnly`, and `IsContiguous` remains available.

### Read, write, and borrow memory

`Read(...)` accepts strided storage. Writing through the indexer or `Write(...)` requires writable, C-contiguous storage. Each callback holds a temporary reference so that concurrent disposal of the original handle cannot invalidate its view.
Keep the view and any spans inside the callback.

For existing span algorithms, use `values.Read(view => Sum(view.AsSpan()))` or `values.Write(view => Increment(view.AsSpan()))`.
These require contiguous storage with at most `int.MaxValue` elements.

For an algorithm using `ReadOnlyMemory<T>`, use `tensor.Memory`.
It supports slicing and requires contiguous storage with at most `int.MaxValue` elements.
Memory borrowed from an input parameter is valid only until the exported call returns; any worker tasks using it must finish before then. For longer use, retain a separate `Tensor<T>` and borrow that handle's `Memory`.

Disposing the handle prevents further memory access.
Spans obtained earlier must not be used afterward.
An active `Pin()` handle keeps storage alive independently until the pin is disposed.

### Create and return tensors

Use `Tensor<T>.Allocate(shape)` for zero-filled writable native storage or `Tensor<T>.FromArray(values, readOnly: false)` for an independent copy of a managed vector.

Returning a tensor transfers that handle to Python.
For example, you can return an imported input without copying its elements:

```csharp
[PythonExport]
public static Tensor<double> Echo(Tensor<double> input) => input;
```

The binding transfers the result before disposing the input's temporary handle.
Call `Retain()` first if another managed consumer also needs its own handle.

### Release retained Python storage

Python buffer cleanup runs with the GIL held.
Normal generated calls drain pending releases before returning.
Releases from managed worker threads and finalizers are queued for Python cleanup; those threads do not acquire the GIL or block a .NET finalizer waiting for Python.
Dispose retained application objects before interpreter shutdown.
Finalization is a fallback, and late shutdown releases do not reenter Python.

