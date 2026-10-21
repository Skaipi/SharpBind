namespace SharpBind.Generator;

/// <summary>Python type annotations generator.</summary>
internal static class PythonTypes
{
    internal static string Annotation(LogicalType? type, bool isResult = false)
    {
        string result = type?.Kind switch
        {
            null => "None",
            LogicalTypeKind.String => "str",
            LogicalTypeKind.Tensor => "_npt.NDArray[_np." + BindingJson.EnumName(type.Element) + "]",
            LogicalTypeKind.Array when isResult => "_npt.NDArray[_np." + BindingJson.EnumName(type.Element) + "]",
            LogicalTypeKind.Array => "_npt.ArrayLike",
            LogicalTypeKind.Scalar => type.Element == LogicalElement.Bool ? "bool" : type.Element is LogicalElement.Float32 or LogicalElement.Float64 ? "float" : "int",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unsupported logical type kind.")
        };
        return type?.Nullable == true ? result + " | None" : result;
    }

}
