# PythonExportAttribute

Opts in a public class, constructor, or synchronous ordinary method.
`Name` overrides a class or method's Python name.

```csharp
[PythonExport(Name = "identity_int")]
public static int Identity(int value) => value;
```

Instance methods require an exported containing class and exactly one exported
constructor. Constructors cannot be named and may have optional parameters.

A generic class needs one attribute per closed specialization. Supply one type
for every generic parameter and give every specialization a distinct Python name:

```csharp
[PythonExport(typeof(int), Name = "IntegerAccumulator")]
[PythonExport(typeof(double), Name = "DoubleAccumulator")]
public sealed class Accumulator<T> where T : INumber<T>
{
    [PythonExport]
    public Accumulator(T initial) { }
}
```

Type arguments are only valid on classes. They must be closed and satisfy the
class's generic constraints.

Static methods can opt in an ordinary containing class themselves. Static-only
Python namespaces expose methods without instance ownership or close operations.

Parameters derive their Python names from C#. Use a docstring to rename them.
Generic methods, properties, events, callbacks, and asynchronous methods are
unsupported; adapt them to a supported synchronous signature in C#.
