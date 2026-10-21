# Supported types

| C# type | Python value | Policy |
| --- | --- | --- |
| Signed/unsigned 8/16/32/64-bit integer | int | Checked |
| float / double | float | Finite overflow rejected; NaN/infinity supported |
| bool | bool | Integer-indexable input equal to 0 or 1 |
| string / string? | str / str or None | Strict UTF-8 copy |
| Numeric T[] / T[]? | 1D array-like input; NumPy result | Checked copy |
| ReadOnlyTensorView<T> input | NumPy array, CPU DLPack producer or numeric buffer | Borrow exact CPU dtype; signed byte strides |
| TensorView<T> input | Writable NumPy array, CPU DLPack producer or numeric buffer | Borrow exact dtype; C-contiguous |
| Tensor<T> input | NumPy array, CPU DLPack producer or numeric buffer | Call-owned managed handle; Retain() explicitly extends storage lifetime |
| Tensor<T> result | NumPy ndarray | Shares native storage; arrays and views keep it alive |
| void | None | Synchronous call |

Elements are numeric integer/floating-point primitives.
Boolean arrays, enums, char, decimal, complex and custom structs are unsupported.

Nullable metadata is respected; oblivious reference metadata permits null.
Tensor owners are non-null, including when nullable metadata is absent.
`Tensor<T>.Allocate` and `FromArray` create native storage.
Borrowed views are ref structs: they cannot be returned or retained beyond a call.
Spans obtained from borrowed views share the same call lifetime.
The bindings construct views over validated foreign CPU storage.

Tensor lease inputs must be non-null.
The binding disposes the input handle when the call ends.
Call `Retain()` to obtain an independent handle for a field or other longer-lived owner, then dispose that handle when done.
`Read(...)` accepts strided layouts; `Write(...)` requires writable C-contiguous storage.
Both callbacks hold storage alive for their entire invocation.
See [retaining tensor inputs](python-runtime.md#retain-tensor-inputs-in-managed-objects).

Vectors retain .NET array limits.
Tensor rank is 0-32, dimensions/byte strides use signed 64-bit integers, and arithmetic is checked.
Rank-zero tensors contain one element.
An empty dimension produces an empty tensor.

Copied matrices/jagged arrays are unsupported; use tensor views or C# adaptation.
Open generic types, generic methods, GPU devices, callbacks across the Python boundary, async calls, properties/events, ref/out/params, general collections and object interchange are deferred.
