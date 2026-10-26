namespace SharpBind;

/// <summary>Checked shape, stride, and logical-index calculations independent of native descriptors.</summary>
internal static class TensorLayout
{
    internal static long Offset(ReadOnlySpan<long> shape, ReadOnlySpan<long> strides, long index, long count)
    {
        if ((ulong)index >= (ulong)count) throw new IndexOutOfRangeException();

        long offset = 0;
        // Decode a row-major logical index from the innermost axis outward. Strides are
        // signed byte distances, so reversed and broadcast axes need no special case.
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            offset = checked(offset + index % shape[i] * strides[i]);
            index /= shape[i];
        }
        return offset;
    }

    internal static bool IsContiguous(ReadOnlySpan<long> shape, ReadOnlySpan<long> strides, int itemsize, long length)
    {
        // Empty tensors touch no storage; singleton axes never advance an address.
        // Neither imposes a stride constraint on an otherwise contiguous layout.
        if (length == 0) return true;

        long expected = itemsize;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            if (shape[i] > 1 && strides[i] != expected)
                return false;
            expected = checked(expected * shape[i]);
        }
        return true;
    }
}
