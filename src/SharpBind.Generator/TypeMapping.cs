namespace SharpBind.Generator;

/// <summary>
/// Maps managed types to their logical and native representations.
/// </summary>
internal static class TypeMapping
{
    private const string ReadOnlyTensorViewName = "SharpBind.ReadOnlyTensorView`1";
    private const string TensorViewName = "SharpBind.TensorView`1";
    private const string TensorName = "SharpBind.Tensor`1";

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte",
        "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref",
        "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    ];

    /// <summary>
    /// Returns a fully qualified C# type name with escaped identifiers.
    /// </summary>
    public static string CSharpName(Type type)
    {
        if (type.IsArray)
        {
            var elementName = CSharpName(type.GetElementType()!);
            var rank = new string(',', type.GetArrayRank() - 1);

            return $"{elementName}[{rank}]";
        }

        var hierarchy = new Stack<Type>();

        for (Type? current = type; current is not null; current = current.DeclaringType)
            hierarchy.Push(current);

        var genericArguments = type.GetGenericArguments();
        var names = new List<string>();
        var argumentOffset = 0;

        var namespaceName = hierarchy.Peek().Namespace;

        foreach (var current in hierarchy)
        {
            var name = current.Name.Split('`')[0];
            var inheritedArgumentCount =
                current.DeclaringType?.GetGenericArguments().Length ?? 0;

            var ownArgumentCount =
                current.GetGenericArguments().Length - inheritedArgumentCount;

            if (ownArgumentCount > 0)
            {
                var arguments = genericArguments
                    .Skip(argumentOffset)
                    .Take(ownArgumentCount)
                    .Select(CSharpName);

                name += $"<{string.Join(", ", arguments)}>";
            }

            names.Add(EscapeName(name));
            argumentOffset += ownArgumentCount;
        }

        var prefix = namespaceName is null
            ? "global::"
            : $"global::{EscapeName(namespaceName)}.";

        return prefix + string.Join(".", names);
    }

    /// <summary>
    /// Maps a managed parameter or return type to its binding representation.
    /// </summary>
    public static BindingType Map(Type type, bool isResult)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName;

            if (definition is ReadOnlyTensorViewName or TensorViewName or TensorName)
                return MapTensor(type, definition, isResult);
        }

        if (type == typeof(string))
            return new(
                new(LogicalTypeKind.String, LogicalElement.Utf8, false),
                new("string", "byte", "uint8_t", "c_uint8", "uint8")
            );

        return type.IsArray
            ? MapArray(type)
            : MapPrimitive(type);
    }

    private static BindingType MapTensor(Type type, string definition, bool isResult)
    {
        var isOwner = definition == TensorName;

        if (isResult && !isOwner)
        {
            throw new NotSupportedException(
                "Borrowed views are inputs only; use a tensor owner for results.");
        }

        var element = Map(type.GetGenericArguments()[0], isResult: false);
        EnsureNumericScalar(element, "Tensor elements");

        var access = definition == TensorViewName
            ? TensorAccess.Writable
            : TensorAccess.ReadOnly;

        var lifetime = isOwner
            ? TensorLifetime.Retainable
            : TensorLifetime.Borrowed;

        return element with
        {
            Logical = element.Logical with
            {
                Kind = LogicalTypeKind.Tensor,
                Access = access,
                Lifetime = lifetime
            },
            Representation = element.Representation with
            {
                Managed = CSharpName(type)
            }
        };
    }

    private static BindingType MapArray(Type type)
    {
        var elementType = type.GetElementType()!;

        if (type.GetArrayRank() != 1 || elementType.IsArray)
        {
            throw new NotSupportedException(
                $"{type}: only numeric vectors are copied; " +
                "use a tensor view for multidimensional inputs.");
        }

        var element = Map(elementType, isResult: false);
        EnsureNumericScalar(element, "Array elements");

        return element with
        {
            Logical = element.Logical with
            {
                Kind = LogicalTypeKind.Array
            },
            Representation = element.Representation with
            {
                Managed = CSharpName(type)
            }
        };
    }

    // Match by full name to support isolated assembly load contexts.
    private static BindingType MapPrimitive(Type type) =>
        type.FullName switch
        {
            "System.Boolean" => Scalar(LogicalElement.Bool, "bool", "uint8_t", "c_uint8", "bool", "byte"),
            "System.SByte" => Scalar(LogicalElement.Int8, "sbyte", "int8_t", "c_int8", "int8"),
            "System.Byte" => Scalar(LogicalElement.UInt8, "byte", "uint8_t", "c_uint8", "uint8"),
            "System.Int16" => Scalar(LogicalElement.Int16, "short", "int16_t", "c_int16", "int16"),
            "System.UInt16" => Scalar(LogicalElement.UInt16, "ushort", "uint16_t", "c_uint16", "uint16"),
            "System.Int32" => Scalar(LogicalElement.Int32, "int", "int32_t", "c_int32", "int32"),
            "System.UInt32" => Scalar(LogicalElement.UInt32, "uint", "uint32_t", "c_uint32", "uint32"),
            "System.Int64" => Scalar(LogicalElement.Int64, "long", "int64_t", "c_int64", "int64"),
            "System.UInt64" => Scalar(LogicalElement.UInt64, "ulong", "uint64_t", "c_uint64", "uint64"),
            "System.Single" => Scalar(LogicalElement.Float32, "float", "float", "c_float", "float32"),
            "System.Double" => Scalar(LogicalElement.Float64, "double", "double", "c_double", "float64"),

            _ => throw new NotSupportedException(
                $"Unsupported binding type: {type}.")
        };

    private static BindingType Scalar(LogicalElement element, string managed, string c, string ctypes, string dtype, string? native = null)
        => new(new(LogicalTypeKind.Scalar, element, false), new(managed, native ?? managed, c, ctypes, dtype));

    private static void EnsureNumericScalar(BindingType type, string description)
    {
        if (type.Kind != LogicalTypeKind.Scalar || type.Logical.Element == LogicalElement.Bool)
            throw new NotSupportedException($"{description} must be numeric primitives.");
    }

    private static string EscapeName(string name) =>
        string.Join(".",
            name.Replace('+', '.').Split('.').Select(part => CSharpKeywords.Contains(part) ? $"@{part}" : part)
        );
}