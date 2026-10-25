using System.Text.Json.Serialization;

namespace SharpBind.Generator;

/// <summary>Record describing how given type should be handled.</summary>
internal record LogicalType(LogicalTypeKind Kind, LogicalElement Element, bool Nullable, TensorAccess Access = TensorAccess.ReadOnly, TensorLifetime Lifetime = TensorLifetime.Borrowed);

internal enum TensorAccess { ReadOnly, Writable }
internal enum TensorLifetime { Borrowed, Retainable }

// BindingJson writes these as snake_case strings. Explicit names preserve the
// existing wire spelling where it differs from the naming policy.
internal enum LogicalTypeKind { Void, Scalar, String, Array, Tensor, Handle }

internal enum LogicalElement
{
    Void,
    Bool,
    [JsonStringEnumMemberName("uint8")]
    UInt8,
    Int8,
    Int16,
    [JsonStringEnumMemberName("uint16")]
    UInt16,
    Int32,
    [JsonStringEnumMemberName("uint32")]
    UInt32,
    Int64,
    [JsonStringEnumMemberName("uint64")]
    UInt64,
    Float32,
    Float64,
    Utf8
}
