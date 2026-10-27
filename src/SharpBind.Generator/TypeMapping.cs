namespace SharpBind.Generator;

/// <summary>
/// Maps managed types to their logical and native representations.
/// </summary>
internal static class TypeMapping
{
    private const string ReadOnlyTensorViewName = "SharpBind.ReadOnlyTensorView`1";
    private const string TensorViewName = "SharpBind.TensorView`1";
    private const string TensorName = "SharpBind.Tensor`1";

    private static readonly BindingType VoidType = new(
        Logical: new(
            Kind: LogicalTypeKind.Void,
            Element: LogicalElement.Void,
            Nullable: false),
        Representation: new(
            Managed: "void",
            Native: "void",
            C: "void",
            Ctypes: "void",
            Dtype: "void"));

    private static readonly BindingType StringType = new(
        Logical: new(
            Kind: LogicalTypeKind.String,
            Element: LogicalElement.Utf8,
            Nullable: false),
        Representation: new(
            Managed: "string",
            Native: "byte",
            C: "uint8_t",
            Ctypes: "c_uint8",
            Dtype: "uint8"));

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

        if (type == typeof(void))
            return VoidType;

        if (type == typeof(string))
            return StringType;

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
            "System.Boolean" => Scalar(
                LogicalElement.Bool,
                managed: "bool",
                native: "byte",
                c: "uint8_t",
                ctypes: "c_uint8",
                dtype: "bool"),

            "System.SByte" => Scalar(
                LogicalElement.Int8,
                managed: "sbyte",
                c: "int8_t",
                ctypes: "c_int8",
                dtype: "int8"),

            "System.Byte" => Scalar(
                LogicalElement.UInt8,
                managed: "byte",
                c: "uint8_t",
                ctypes: "c_uint8",
                dtype: "uint8"),

            "System.Int16" => Scalar(
                LogicalElement.Int16,
                managed: "short",
                c: "int16_t",
                ctypes: "c_int16",
                dtype: "int16"),

            "System.UInt16" => Scalar(
                LogicalElement.UInt16,
                managed: "ushort",
                c: "uint16_t",
                ctypes: "c_uint16",
                dtype: "uint16"),

            "System.Int32" => Scalar(
                LogicalElement.Int32,
                managed: "int",
                c: "int32_t",
                ctypes: "c_int32",
                dtype: "int32"),

            "System.UInt32" => Scalar(
                LogicalElement.UInt32,
                managed: "uint",
                c: "uint32_t",
                ctypes: "c_uint32",
                dtype: "uint32"),

            "System.Int64" => Scalar(
                LogicalElement.Int64,
                managed: "long",
                c: "int64_t",
                ctypes: "c_int64",
                dtype: "int64"),

            "System.UInt64" => Scalar(
                LogicalElement.UInt64,
                managed: "ulong",
                c: "uint64_t",
                ctypes: "c_uint64",
                dtype: "uint64"),

            "System.Single" => Scalar(
                LogicalElement.Float32,
                managed: "float",
                c: "float",
                ctypes: "c_float",
                dtype: "float32"),

            "System.Double" => Scalar(
                LogicalElement.Float64,
                managed: "double",
                c: "double",
                ctypes: "c_double",
                dtype: "float64"),

            _ => throw new NotSupportedException(
                $"Unsupported binding type: {type}.")
        };

    private static BindingType Scalar(
        LogicalElement element,
        string managed,
        string c,
        string ctypes,
        string dtype,
        string? native = null) =>
        new(
            Logical: new(
                Kind: LogicalTypeKind.Scalar,
                Element: element,
                Nullable: false),
            Representation: new(
                Managed: managed,
                Native: native ?? managed,
                C: c,
                Ctypes: ctypes,
                Dtype: dtype));

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
