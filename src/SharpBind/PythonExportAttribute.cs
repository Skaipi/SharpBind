namespace SharpBind;

/// <summary>Opts a public class, constructor, or synchronous method into Python binding.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class PythonExportAttribute : Attribute
{
    /// <summary>Creates an export for a nongeneric class or member.</summary>
    public PythonExportAttribute() { }

    /// <summary>Creates an export specialized with the supplied generic type arguments.</summary>
    /// <param name="typeArguments">One closed type for each generic class parameter.</param>
    public PythonExportAttribute(params Type[] typeArguments) => TypeArguments = typeArguments;

    /// <summary>Types used to specialize a generic class.</summary>
    public Type[] TypeArguments { get; } = [];

    /// <summary>Allow concurrent calls on this exported class. Defaults to serialized calls.</summary>
    /// <remarks>The class must synchronize its mutable state. Close still waits for all active calls.</remarks>
    public bool ConcurrentCalls { get; set; }

    /// <summary>Optional Python class or method name. Constructors cannot be renamed.</summary>
    public string? Name { get; set; }
}
