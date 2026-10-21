# Common runtime

This is SharpBind's interoperability layer: the shared ABI, ownership rules, type conversions, and error translation that connect generated native exports with Python calls.

- `native/` contains the C ABI declarations and CPython buffer/lease adapter.
- `src/sharpbind_runtime/` contains Python ABI layouts, argument/result handling, native-library loading, error translation, and managed-object lifetime support.

The corresponding managed declarations live in `src/SharpBind/Interop` at the repository root.
See [ABI maintenance](../../docs/abi-maintenance.md) for the contracts that must agree across C, C#, and Python.

`build/python/sharpbind_pack` handles build and packaging orchestration.
NuGet ships these common-runtime sources in this same layout; the packager compiles the adapter and bundles the implementation as `<consumer_package>._sharpbind`.
The source layer is not a separately installed Python distribution.
Consumers install their chosen package and use its public API and ordinary NumPy arrays.
