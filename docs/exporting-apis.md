# Exporting APIs

`[PythonExport]` is an attribute needed to designate public member of library for export.
Instance methods require an exported class and one exported constructor.
An annotated static method can opt in its containing class.

Classes keep their names; methods and parameters use snake_case.
Use `[PythonExport(Name = "identity_int")]` to give exported entry a specific name.
Constructors cannot be renamed.

## Specialize a generic class

Repeat the class attribute for each specialization.
Every attribute supplies one closed type per generic parameter and a unique Python class name:

```csharp
using System.Numerics;
using SharpBind;

[PythonExport(typeof(int), Name = "IntegerAccumulator")]
[PythonExport(typeof(double), Name = "DoubleAccumulator")]
public sealed class Accumulator<T> where T : INumber<T>
{
    [PythonExport]
    public Accumulator(T initial) { }
}
```

The generator outputs specialized native entry points, so `T` is emitted as `int` or `double` in the corresponding binding.
Generic methods remain unsupported.
Invalid or incomplete specializations are rejected with build error.
Nested generic classes supply their containing type's arguments first, followed by their own arguments.

## Document the facade

Direct XML summaries, parameters, return descriptions, remarks and examples become Python docstrings.
Enable `GenerateDocumentationFile` on the managed project.
Enable `SharpBindStrictDocumentation` on the binding project to reject missing summaries or parameter descriptions.

Write documentation directly on the exported facade. `inheritdoc` is rejected with an exception on build.

Methods must be synchronous and Native AOT compatible.
See [Supported types](supported-types.md) and the [export attribute reference](reference/python-export-attribute.md).
