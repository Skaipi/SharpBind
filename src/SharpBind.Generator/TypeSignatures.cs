using System.Runtime.CompilerServices;

namespace SharpBind.Generator;

internal static class PythonTypes
{
    // Python always map one types one to one
    internal static List<string> TypeSignatures(LogicalType? type, bool isResult = false)
    {
        string result = type?.Kind switch
        {
            null => "None",
            LogicalTypeKind.Scalar => type.Element == LogicalElement.Bool ? "bool" : type.Element is LogicalElement.Float32 or LogicalElement.Float64 ? "float" : "int",
            LogicalTypeKind.String => "str",
            LogicalTypeKind.Array when isResult => "_npt.NDArray[_np." + BindingJson.EnumName(type.Element) + "]",
            LogicalTypeKind.Array => "_npt.ArrayLike",
            LogicalTypeKind.Tensor => "_npt.NDArray[_np." + BindingJson.EnumName(type.Element) + "]",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unsupported logical type kind.")
        };
        result = type?.Nullable == true ? result + " | None" : result;
        return new List<string> { result };
    }
}

internal static class CsharpTypes
{
    internal static List<string> TypeSignature(BindingType type)
    {
        string mainType = type?.Kind switch
        {
            null => "void",
            LogicalTypeKind.Scalar => $"{type.NativeTypeName}",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (RequireAuxilaryMetadata(type.Logical)) result.Add("long* resultLength");

        return result;
    }

    private static bool RequireAuxilaryMetadata(LogicalType? type)
    {
        return type?.Kind == LogicalTypeKind.Array ? true : false;
    }
}

internal static class CTypes
{
    internal static List<string> TypeSignature(BindingType type)
    {
        string mainType = type?.Kind switch
        {
            null => "void",
            LogicalTypeKind.Scalar => $"{type.CTypeName}",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unsupported logical type kind.")
        };

        var result = new List<string> { mainType };
        if (RequireAuxilaryMetadata(type?.Logical)) result.Add("int64_t* result_length");

        return result;
    }

    private static bool RequireAuxilaryMetadata(LogicalType? type)
    {
        return type?.Kind == LogicalTypeKind.Array ? true : false;
    }
}