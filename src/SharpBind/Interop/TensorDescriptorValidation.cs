namespace SharpBind.Interop;

/// <summary>
/// Checks native tensor metadata before constructing a safe borrowed view.
/// </summary>
internal static unsafe class TensorDescriptorValidation
{
    /// <summary>
    /// Validate tensor structure and return its length
    /// </summary>
    internal static long Validate<T>(TensorDescriptor* d, bool writable) where T : unmanaged
    {
        const int CpuDeviceType = 1;
        const ulong WritableFlag = 1;
        const int MaxRank = 32;

        int elementSize = sizeof(T);

        // Descriptor
        if (d == null || d->Size != sizeof(TensorDescriptor)) throw new ArgumentException("Invalid tensor descriptor size.");
        if (d->Dtype != TensorElement.Code<T>()) throw new ArgumentException("Tensor dtype mismatch.");
        if (d->DeviceType != CpuDeviceType || d->DeviceId != 0) throw new NotSupportedException("Only CPU device 0 is supported.");

        // Access
        if ((d->Flags & ~WritableFlag) != 0) throw new ArgumentException("Unknown tensor access flags.");
        if (writable && (d->Flags & WritableFlag) == 0) throw new ArgumentException("Tensor is not writable.");

        // Metadata
        int rank = d->Rank;
        if ((uint)rank > MaxRank) throw new ArgumentException("Invalid tensor rank.");
        if (rank > 0 && (d->Shape == null || d->Strides == null)) throw new ArgumentException("Missing tensor shape or strides.");

        // Detect empty axes before multiplying: a later zero dimension must not cause
        // an otherwise empty tensor to fail on an overflowing partial product.
        bool empty = false;
        for (int i = 0; i < rank; i++)
        {
            if (d->Shape[i] < 0) throw new ArgumentException("Negative tensor dimension.");
            empty |= d->Shape[i] == 0;
        }

        long count = empty ? 0 : 1;
        for (int i = 0; i < rank && !empty; i++)
        {
            long dimension = d->Shape[i];
            count = checked(count * dimension);
        }

        _ = checked(count * elementSize); // Python buffer byte lengths must also fit int64.

        // Data addresses logical element zero, which may lie inside a reversed view.
        // Accumulate the furthest reachable byte offsets on either side of that address.
        long minimumOffset = 0;
        long maximumOffset = 0;
        long expectedStride = elementSize;
        for (int i = rank - 1; i >= 0; i--)
        {
            long dimension = d->Shape[i];
            long stride = d->Strides[i];

            if (stride == long.MinValue || stride % elementSize != 0)
                throw new ArgumentException("Unaligned tensor stride.");

            if (writable && count != 0)
            {
                // Restrict writes to C order to exclude overlapping/broadcast layouts.
                // Singleton axes cannot overlap another element, regardless of stride.
                if (dimension > 1 && stride != expectedStride)
                    throw new ArgumentException("Writable views require contiguous, non-overlapping storage.");

                expectedStride = checked(expectedStride * dimension);
            }

            if (count != 0)
            {
                long offset = checked((dimension - 1) * stride);

                minimumOffset = checked(minimumOffset + Math.Min(0, offset));
                maximumOffset = checked(maximumOffset + Math.Max(0, offset));
            }
        }

        // Storage
        if (count == 0) return 0;
        if (d->Data == null) throw new ArgumentException("Missing tensor storage.");
        if ((nuint)d->Data % (nuint)elementSize != 0) throw new ArgumentException("Unaligned tensor storage.");
        if (minimumOffset == long.MinValue) throw new ArgumentException("Tensor address arithmetic overflows.");

        ulong address = (ulong)d->Data;
        ulong bytesBefore = (ulong)-minimumOffset;
        ulong bytesAfter = checked((ulong)maximumOffset + (uint)elementSize - 1);

        // These checks prevent address wraparound; the exporter remains responsible
        // for supplying a live allocation covering the declared shape and strides.
        if (address < bytesBefore || address > ulong.MaxValue - bytesAfter)
            throw new ArgumentException("Tensor address arithmetic overflows.");

        return count;
    }

}
