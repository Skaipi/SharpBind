namespace SharpBind.Generator;

/// <summary>Backend spellings resolved during discovery, before either emitter runs.</summary>
internal record NativeRepresentation(string Managed, string Native, string C, string Ctypes, string Dtype);

/// <summary>Record describing type under Python, Common Runtime, and Dotnet.</summary>
internal record BindingType(LogicalType Logical, NativeRepresentation Representation)
{
    public LogicalTypeKind Kind => Logical.Kind;
    public string ManagedTypeName => Representation.Managed;
    public string NativeTypeName => Representation.Native;
    public string CTypeName => Representation.C;
    public string CtypesName => Representation.Ctypes;
    public string NumpyDtype => Representation.Dtype;
    public bool IsBuffer => Kind is LogicalTypeKind.Array or LogicalTypeKind.String;
    public bool IsTensorOwner => Kind == LogicalTypeKind.Tensor && Logical.Lifetime == TensorLifetime.Retainable;
}

/// <summary>Represent argument of a function under Python, Common Runtime, and Dotnet.</summary>
internal record ExportArgument(string Name, string NativeName, string SourceName, BindingType Type, bool Optional, object? DefaultValue);

/// <summary>Represents callable method under Python, Common Runtime, and Dotnet</summary>
internal record ExportMethod(string Name, string EntryPoint, string ManagedName, bool IsConstructor, bool IsStatic, BindingType? Result, ExportArgument[] Arguments, string Documentation);

/// <summary>Represents a class under Python, Common Runtime, and Dotnet</summary>
internal record ExportClass(string Name, string Prefix, string ManagedTypeName, ExportMethod[] Methods, string Documentation, bool ConcurrentCalls = false)
{
    public bool HasInstances => Methods.Any(m => m.IsConstructor || !m.IsStatic);
}
