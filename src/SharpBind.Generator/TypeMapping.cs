namespace SharpBind.Generator;

/// <summary>
/// Maps managed types to their logical and native representations.
/// </summary>
internal static class TypeMapping
{
    private const string ReadOnlyTensorViewName = "SharpBind.ReadOnlyTensorView`1";
    private const string TensorViewName = "SharpBind.TensorView`1";
    private const string TensorName = "SharpBind.Tensor`1";

    private static readonly BindingType VoidType = CreateVoidType();
    private static readonly BindingType StringType = CreateStringType();

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
                Managed = CSharpTypeNames.Format(type)
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
                Managed = CSharpTypeNames.Format(type)
            }
        };
    }

    // Match by full name to support isolated assembly load contexts.
    private static BindingType MapPrimitive(Type type)
    {
        return type.FullName switch
        {
            "System.Boolean" => Scalar(
                LogicalElement.Bool,
                new NativeRepresentation(
                    Managed: "bool",
                    Native: "byte",
                    C: "uint8_t",
                    Ctypes: "c_uint8",
                    Dtype: "bool")),

            "System.SByte" => Scalar(
                LogicalElement.Int8,
                new NativeRepresentation(
                    Managed: "sbyte",
                    Native: "sbyte",
                    C: "int8_t",
                    Ctypes: "c_int8",
                    Dtype: "int8")),

            "System.Byte" => Scalar(
                LogicalElement.UInt8,
                new NativeRepresentation(
                    Managed: "byte",
                    Native: "byte",
                    C: "uint8_t",
                    Ctypes: "c_uint8",
                    Dtype: "uint8")),

            "System.Int16" => Scalar(
                LogicalElement.Int16,
                new NativeRepresentation(
                    Managed: "short",
                    Native: "short",
                    C: "int16_t",
                    Ctypes: "c_int16",
                    Dtype: "int16")),

            "System.UInt16" => Scalar(
                LogicalElement.UInt16,
                new NativeRepresentation(
                    Managed: "ushort",
                    Native: "ushort",
                    C: "uint16_t",
                    Ctypes: "c_uint16",
                    Dtype: "uint16")),

            "System.Int32" => Scalar(
                LogicalElement.Int32,
                new NativeRepresentation(
                    Managed: "int",
                    Native: "int",
                    C: "int32_t",
                    Ctypes: "c_int32",
                    Dtype: "int32")),

            "System.UInt32" => Scalar(
                LogicalElement.UInt32,
                new NativeRepresentation(
                    Managed: "uint",
                    Native: "uint",
                    C: "uint32_t",
                    Ctypes: "c_uint32",
                    Dtype: "uint32")),

            "System.Int64" => Scalar(
                LogicalElement.Int64,
                new NativeRepresentation(
                    Managed: "long",
                    Native: "long",
                    C: "int64_t",
                    Ctypes: "c_int64",
                    Dtype: "int64")),

            "System.UInt64" => Scalar(
                LogicalElement.UInt64,
                new NativeRepresentation(
                    Managed: "ulong",
                    Native: "ulong",
                    C: "uint64_t",
                    Ctypes: "c_uint64",
                    Dtype: "uint64")),

            "System.Single" => Scalar(
                LogicalElement.Float32,
                new NativeRepresentation(
                    Managed: "float",
                    Native: "float",
                    C: "float",
                    Ctypes: "c_float",
                    Dtype: "float32")),

            "System.Double" => Scalar(
                LogicalElement.Float64,
                new NativeRepresentation(
                    Managed: "double",
                    Native: "double",
                    C: "double",
                    Ctypes: "c_double",
                    Dtype: "float64")),

            _ => throw new NotSupportedException(
                $"Unsupported binding type: {type}.")
        };
    }

    private static BindingType CreateVoidType()
    {
        var logical = new LogicalType(
            Kind: LogicalTypeKind.Void,
            Element: LogicalElement.Void,
            Nullable: false);

        var representation = new NativeRepresentation(
            Managed: "void",
            Native: "void",
            C: "void",
            Ctypes: "void",
            Dtype: "void");

        return new BindingType(logical, representation);
    }

    private static BindingType CreateStringType()
    {
        var logical = new LogicalType(
            Kind: LogicalTypeKind.String,
            Element: LogicalElement.Utf8,
            Nullable: false);

        var representation = new NativeRepresentation(
            Managed: "string",
            Native: "byte",
            C: "uint8_t",
            Ctypes: "c_uint8",
            Dtype: "uint8");

        return new BindingType(logical, representation);
    }

    private static BindingType Scalar(LogicalElement element, NativeRepresentation representation)
    {
        var logical = new LogicalType(
            Kind: LogicalTypeKind.Scalar,
            Element: element,
            Nullable: false);

        return new BindingType(logical, representation);
    }

    private static void EnsureNumericScalar(BindingType type, string description)
    {
        if (type.Kind != LogicalTypeKind.Scalar || type.Logical.Element == LogicalElement.Bool)
            throw new NotSupportedException($"{description} must be numeric primitives.");
    }
}
