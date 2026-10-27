namespace SharpBind.Generator;

internal static class PythonTypes
{
    // Python always map one types one to one
    internal static string TypeSignatures(LogicalType type, bool isResult = false)
    {
        string result = type.Kind switch
        {
            LogicalTypeKind.Void => "None",
            LogicalTypeKind.Scalar => ScalarSignature(type.Element),
            LogicalTypeKind.String => "str",
            LogicalTypeKind.Array when isResult => $"_npt.NDArray[_np.{BindingJson.EnumName(type.Element)}]",
            LogicalTypeKind.Array => "_npt.ArrayLike",
            LogicalTypeKind.Tensor => $"_npt.NDArray[_np.{BindingJson.EnumName(type.Element)}]",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported logical type kind.")
        };
        result = type?.Nullable == true ? result + " | None" : result;
        return result;
    }

    private static string ScalarSignature(LogicalElement element)
    {
        return element switch
        {
            LogicalElement.Bool => "bool",
            LogicalElement.Float32 => "float",
            LogicalElement.Float64 => "float",
            LogicalElement.Int8 => "int",
            LogicalElement.Int16 => "int",
            LogicalElement.Int32 => "int",
            LogicalElement.Int64 => "int",
            LogicalElement.UInt8 => "int",
            LogicalElement.UInt16 => "int",
            LogicalElement.UInt32 => "int",
            LogicalElement.UInt64 => "int",
            _ => throw new ArgumentOutOfRangeException("Unsupported logical type specialization.")
        };
    }
}

internal static class CsharpTypes
{
    // Abstract types require auxilary data to be correctly represented.
    internal static List<string> TypeSignature(BindingType type, string name)
    {
        string mainType = type.Kind switch
        {
            LogicalTypeKind.Void => throw new ArgumentException("Void function argument.", nameof(type)),
            LogicalTypeKind.Scalar => $"{type.NativeTypeName} {name}",
            LogicalTypeKind.String => $"{type.NativeTypeName}* {name}",
            LogicalTypeKind.Array => $"{type.NativeTypeName}* {name}",
            LogicalTypeKind.Tensor => $"global::SharpBind.Interop.TensorDescriptor* {name}",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (type.IsBuffer) result.Add($"long {name}Length");

        return result;
    }

    // Results are returned via caller-provided storage
    internal static List<string> ResultSignature(BindingType type)
    {
        if (type.Kind == LogicalTypeKind.Void) return [];

        string mainType = type.Kind switch
        {
            LogicalTypeKind.Scalar => $"{type.NativeTypeName}* result",
            LogicalTypeKind.String => $"{type.NativeTypeName}** result",
            LogicalTypeKind.Array => $"{type.NativeTypeName}** result",
            LogicalTypeKind.Tensor => "global::SharpBind.Interop.TensorDescriptor* result",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (type.IsBuffer) result.Add("long* resultLength");

        return result;
    }

    internal static string ThisSignature => "SbHandle handle";

    internal static string ErrorSignature => "SbError* error";

    internal static string ConstructorResultSignature => "SbHandle* result";
}

internal static class CTypes
{
    // Abstract types require auxilary data to be correctly represented.
    internal static List<string> TypeSignature(BindingType type, string name)
    {
        string mainType = type.Kind switch
        {
            LogicalTypeKind.Void => throw new ArgumentException("Void function argument.", nameof(type)),
            LogicalTypeKind.Scalar => $"{type.CTypeName} {name}",
            LogicalTypeKind.String => $"const {type.CTypeName}* {name}",
            LogicalTypeKind.Array => $"const {type.CTypeName}* {name}",
            LogicalTypeKind.Tensor when type.IsTensorOwner => $"sb_tensor* {name}",
            LogicalTypeKind.Tensor => $"const sb_tensor* {name}",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (type.IsBuffer) result.Add($"int64_t {name}_length");

        return result;
    }

    // Results are returned via caller-provided storage
    internal static List<string> ResultSignature(BindingType type)
    {
        if (type.Kind == LogicalTypeKind.Void) return [];

        string mainType = type.Kind switch
        {
            LogicalTypeKind.Scalar => $"{type.CTypeName}* result",
            LogicalTypeKind.String => $"{type.CTypeName}** result",
            LogicalTypeKind.Array => $"{type.CTypeName}** result",
            LogicalTypeKind.Tensor => $"sb_tensor* result",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (type.IsBuffer) result.Add("int64_t* result_length");

        return result;
    }

    internal static string ThisSignature => "sb_handle handle";

    internal static string ErrorSignature => "sb_error* error";

    internal static string ConstructorResultSignature => "sb_handle* result";
}
