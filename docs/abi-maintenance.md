# Maintaining the binding schema and native ABI

SharpBind maintains its ABI declarations directly in C, C# and Python.
`python/common-runtime` is the interoperability layer shared by both sides;
`build/python/sharpbind_pack` contains its build and packaging orchestration.
Edit these source files by hand.
Layout and installed-package tests check their agreement.
The managed binding generator creates consumer wrappers, native exports and call descriptions; it does not generate SharpBind's own ABI declarations.

## Changing the schema version

Both parties must agree on their schema version.
Update versions together when making an incompatible change to serialized call descriptions, native calling conventions, structure layouts, dtype IDs or ownership rules:

| File | Constant |
| --- | --- |
| `src/SharpBind.Generator/BindingSchema.cs` | `BindingSchema.SchemaVersion` |
| `python/common-runtime/src/sharpbind_runtime/_abi.py` | `SCHEMA_VERSION` |

`BindingSchema` writes the version into `bindings.json` and the schema embedded in the Python wrappers.
`_provider.py` rejects schemas with a different version before loading the native library. 
There is no separate native ABI version negotiation: the generated wrappers and native library must be rebuilt and shipped together.
The NuGet package versions the generator, build helpers, and private Python/C support together.
Each consumer wheel embeds the matching implementation.

Refactoring source files does not require a version change when the serialized descriptions, layouts and behavior remain compatible.

When changing the schema's fields or meanings, inspect `BindingSchema.cs` and `BindingTypes.cs` alongside the Python runtime's `_abi.py`, `_provider.py`, `_arguments.py`, `_conversion.py` and `_result.py`.
Update both emitters when the generated native entry points or Python wrappers need a different call shape.

## Changing a native structure

Keep field order, widths, alignment and ownership consistent across these files:

| Structure | C# declaration | C declaration | Python declaration |
| --- | --- | --- | --- |
| Object handle | `src/SharpBind/Interop/AbiTypes.cs`: `SbHandle` | `python/common-runtime/native/abi.h`: `sb_handle` | `_abi.py`: `Handle` |
| Error record | `src/SharpBind/Interop/AbiTypes.cs`: `SbError` | `python/common-runtime/native/abi.h`: `sb_error` | `_abi.py`: `Error` |
| Tensor descriptor | `src/SharpBind/Interop/TensorDescriptor.cs` | `python/common-runtime/native/abi.h`: `sb_tensor` | `_abi.py`: `Descriptor` |

Python paths in this table refer to `python/common-runtime/src/sharpbind_runtime`.
The generator embeds `python/common-runtime/native/abi.h` directly into emitted `bindings.h` files; there is no second C header to maintain.

Also update the code that fills, validates and releases the changed record.
For tensors this includes `TensorDescriptorValidation.cs`, `TensorMemory.cs`, `TensorInterop.cs` and the native adapter's descriptor and lease handling.
Check sizes and field offsets in the managed layout tests and `tests/integration/test_tensors.py`, which compares ctypes with the compiled C adapter.
Add validation and lifetime cases for new fields or ownership rules.

## Changing supported primitive or tensor element types

| File | Responsibility |
| --- | --- |
| `src/SharpBind.Generator/TypeMapping.cs` | Scalar representations in C#, C and ctypes; dtype names in call descriptions |
| `src/SharpBind.Generator/BindingTypes.cs` | Logical element names used by discovery and emitters |
| `src/SharpBind/Tensors/TensorElement.cs` | Managed tensor element IDs |
| `python/common-runtime/native/dtypes.h` | Tensor IDs, sizes, buffer formats and DLPack codes |
| `python/common-runtime/src/sharpbind_runtime/_abi.py` | Python tensor IDs and names |
| `python/common-runtime/src/sharpbind_runtime/_conversion.py` | Checked scalar and copied-vector numeric bounds |

Existing tensor IDs are explicit ABI values; do not renumber them.
When adding a tensor dtype, also review the supported dtype bounds and special cases in `native/descriptor.c`, `native/buffer.c` and `native/dlpack.c`, and the generator's Python typing and scalar emission.
Boolean scalars use a byte on the native boundary; boolean tensors remain unsupported.

The maximum tensor rank is maintained in `native/dtypes.h` and `src/SharpBind/Interop/TensorDescriptorValidation.cs`.
Device and access flag rules must likewise agree between managed validation and the C adapter.

## Changing supported platforms

Update `build/python/sharpbind_pack/_platform.py`.
The packager copies this same source into each consumer wheel, so build-time and installed host checks agree.

## Verifying a change

Run the managed tests, then build fresh wheels and run the installed-package smoke test:

```shell
dotnet test tests/SharpBind.Tests/SharpBind.Tests.csproj
python tests/package_smoke.py
```

Follow the [contributor checks](contributing.md) for the platform and packaging tests.
Never mix wrappers generated for the new contract with an older native library.
