using SharpBind.Interop;

namespace SharpBind;

internal interface ITensorStorage
{
    void RemoveReference();
}

/// <summary>One native ownership token shared by managed handles and exported owners.</summary>
internal unsafe class TensorStorage<T> : ITensorStorage where T : unmanaged
{
    private readonly TensorDescriptor descriptor;
    private int references = 1;

    internal TensorStorage(TensorDescriptor descriptor)
    {
        Length = TensorDescriptorValidation.Validate<T>(&descriptor, false);
        if (descriptor.Lease == null || descriptor.Release == null)
            throw new ArgumentException("An owned tensor requires a storage token and release function.");
        // Snapshot metadata so managed handles do not depend on the caller's descriptor
        // or metadata buffers. Element storage remains shared through the cleanup token.
        Shape = new ReadOnlySpan<long>(descriptor.Shape, descriptor.Rank).ToArray();
        Strides = new ReadOnlySpan<long>(descriptor.Strides, descriptor.Rank).ToArray();
        IsContiguous = TensorLayout.IsContiguous(Shape, Strides, sizeof(T), Length);
        this.descriptor = descriptor;
    }

    internal long Length { get; }
    internal long[] Shape { get; }
    internal long[] Strides { get; }
    internal bool IsReadOnly => (descriptor.Flags & 1) == 0;
    internal bool IsContiguous { get; }
    internal T* Data => (T*)descriptor.Data;

    internal void AddReference()
    {
        // The owning handle keeps the count nonzero while this method runs.
        // The CAS loop enforces the limit without ever wrapping the reference count.
        int count;
        do
        {
            count = Volatile.Read(ref references);
            if (count == int.MaxValue) throw new OverflowException("Too many tensor references.");
        } while (Interlocked.CompareExchange(ref references, count + 1, count) != count);
    }

    public void RemoveReference()
    {
        if (Interlocked.Decrement(ref references) == 0)
            descriptor.Release(descriptor.Lease);
    }

    internal TensorDescriptor Export() => TensorMemory.Export(descriptor, this, Shape, Strides);

    internal ReadOnlyTensorView<T> ReadView() => new(Data, Length, Shape, Strides);

    internal TensorView<T> WriteView()
    {
        if (IsReadOnly) throw new InvalidOperationException("Read-only tensor.");
        if (!IsContiguous) throw new InvalidOperationException("Writable views require contiguous, non-overlapping storage.");

        return new(Data, Length, Shape);
    }
}
