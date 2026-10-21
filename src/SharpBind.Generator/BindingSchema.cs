namespace SharpBind.Generator;

/// <summary>
/// Serializes the generated call descriptions consumed by the Python runtime.
/// </summary>
internal static class BindingSchema
{
    // Must match SCHEMA_VERSION in python/common-runtime/src/sharpbind_runtime/_abi.py.
    // See docs/abi-maintenance.md before changing the schema or native ABI.
    internal const int SchemaVersion = 1;

    internal static string Serialize(ExportClass[] classes, string libraryName, string? module) =>
        BindingJson.Serialize(new
        {
            schema_version = SchemaVersion,
            library = libraryName,
            python_module = module,
            classes = classes.Select(cls => new
            {
                name = cls.Name,
                release = cls.HasInstances ? cls.Prefix + "_release" : null,
                methods = cls.Methods.Select(method => new
                {
                    name = method.Name,
                    entry = method.EntryPoint,
                    constructor = method.IsConstructor,
                    is_static = method.IsStatic,
                    result = method.IsConstructor
                        ? new
                        {
                            kind = LogicalTypeKind.Handle,
                            ct = "Handle",
                            dtype = "",
                            nullable = false
                        }
                        : SchemaType(method.Result),
                    arguments = method.Arguments.Select(a => new
                    {
                        name = a.Name,
                        type = SchemaType(a.Type)
                    })
                })
            })
        });

    private static object? SchemaType(BindingType? type)
    {
        if (type is null) return null;

        var result = new Dictionary<string, object>
        {
            ["kind"] = type.Kind,
            ["ct"] = type.CtypesName,
            ["dtype"] = type.NumpyDtype,
            ["nullable"] = type.Logical.Nullable
        };

        if (type.Kind == LogicalTypeKind.Tensor)
        {
            result["access"] = type.Logical.Access;
            result["lifetime"] = type.Logical.Lifetime;
        }
        return result;
    }
}
