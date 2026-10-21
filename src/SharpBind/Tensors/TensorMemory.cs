using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpBind.Interop;

namespace SharpBind;

/// <summary>Allocates and releases native tensor storage. Allocation records belong to this managed provider.</summary>
internal static unsafe class TensorMemory
{
    private sealed record Allocation(nint Data, nint Dimensions);
    private static readonly ConcurrentDictionary<nint, Allocation> Allocations = new();
    // Native tokens are not GC roots. Keep exported managed storage reachable until
    // the consumer invokes ReleaseExport, even if all managed handles have gone away.
    private static readonly ConcurrentDictionary<nint, ITensorStorage> Exports = new();
    private static long totalReleased;
    /// <summary>Number of currently owned native allocations.</summary>
    public static long ActiveAllocations => Allocations.Count;
    /// <summary>Total native tensor allocations released.</summary>
    public static long TotalReleased => Interlocked.Read(ref totalReleased);

    internal static TensorDescriptor Allocate<T>(long[] shape, bool writable) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(shape);
        shape = (long[])shape.Clone();
        if (shape.Length > 32)
            throw new ArgumentException("Tensor rank exceeds 32.");
        int dtype = TensorElement.Code<T>();
        if (shape.Any(dimension => dimension < 0))
            throw new ArgumentException("Negative dimension.");
        long length = shape.Contains(0L) ? 0 : 1;
        if (length != 0)
            foreach (long dimension in shape)
                length = checked(length * dimension);
        nuint bytes = checked((nuint)checked(length * sizeof(T)));
        void* data = null;
        long* dimensions = null;
        try
        {
            data = NativeMemory.AllocZeroed(bytes == 0 ? 1 : bytes);
            dimensions = (long*)NativeMemory.Alloc(checked((nuint)Math.Max(1, shape.Length * 2) * sizeof(long)));
            if (data == null || dimensions == null)
                throw new OutOfMemoryException();
            long stride = length == 0 ? 0 : sizeof(T);
            for (int i = shape.Length - 1; i >= 0; i--)
            {
                dimensions[i] = shape[i];
                dimensions[i + shape.Length] = stride;
                stride = checked(stride * shape[i]);
            }
            if (!Allocations.TryAdd((nint)data, new((nint)data, (nint)dimensions)))
                throw new InvalidOperationException();
            return new TensorDescriptor
            {
                Size = (uint)sizeof(TensorDescriptor),
                Data = data,
                Dtype = dtype,
                Rank = shape.Length,
                Shape = dimensions,
                Strides = dimensions + shape.Length,
                DeviceType = 1,
                Flags = writable ? 1UL : 0,
                Lease = data,
                Release = &Release
            };
        }
        catch
        {
            NativeMemory.Free(data);
            NativeMemory.Free(dimensions);
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(void* lease) => ReleaseManaged(lease);

    // Transfer one existing managed reference to a native consumer; no foreign retain callback.
    internal static TensorDescriptor Export(TensorDescriptor descriptor, ITensorStorage storage,
        long[] shape, long[] strides)
    {
        void* token = NativeMemory.Alloc(checked((nuint)Math.Max(1, shape.Length * 2) * sizeof(long)));
        if (token == null)
            throw new OutOfMemoryException();
        try
        {
            shape.AsSpan().CopyTo(new Span<long>(token, shape.Length));
            strides.AsSpan().CopyTo(new Span<long>((long*)token + shape.Length, strides.Length));
            if (!Exports.TryAdd((nint)token, storage))
                throw new InvalidOperationException("Tensor export already registered.");
        }
        catch
        {
            NativeMemory.Free(token);
            throw;
        }
        descriptor.Lease = token;
        descriptor.Release = &ReleaseExport;
        descriptor.Shape = (long*)token;
        descriptor.Strides = (long*)token + shape.Length;
        return descriptor;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseExport(void* token)
    {
        if (Exports.TryRemove((nint)token, out var storage))
        {
            NativeMemory.Free(token);
            storage.RemoveReference();
        }
    }

    internal static void ReleaseManaged(void* lease)
    {
        if (Allocations.TryRemove((nint)lease, out var allocation))
        {
            NativeMemory.Free((void*)allocation.Data);
            NativeMemory.Free((void*)allocation.Dimensions);
            Interlocked.Increment(ref totalReleased);
        }
    }
}
