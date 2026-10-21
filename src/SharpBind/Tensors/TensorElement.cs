namespace SharpBind;

/// <summary>Maps supported tensor elements to their stable native dtype IDs.</summary>
internal static class TensorElement
{
    internal static int Code<T>() where T : unmanaged
    {
        if (typeof(T).IsEnum)
            throw new NotSupportedException("Tensor elements must be numeric primitives, not enums.");

        // Keep these IDs aligned with python/common-runtime/native/dtypes.h and python/common-runtime/src/sharpbind_runtime/_abi.py.
        return Type.GetTypeCode(typeof(T)) switch
        {
            TypeCode.SByte => 1,
            TypeCode.Byte => 2,
            TypeCode.Int16 => 3,
            TypeCode.UInt16 => 4,
            TypeCode.Int32 => 5,
            TypeCode.UInt32 => 6,
            TypeCode.Int64 => 7,
            TypeCode.UInt64 => 8,
            TypeCode.Single => 9,
            TypeCode.Double => 10,
            _ => throw new NotSupportedException("Tensor element must be a numeric primitive.")
        };
    }
}
