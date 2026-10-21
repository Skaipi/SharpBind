namespace SharpBind;

/// <summary>A synchronous, borrowed, read-only CPU view, including strided native inputs.</summary>
public readonly unsafe ref struct ReadOnlyTensorView<T> where T : unmanaged
{
    private readonly T* data;
    private readonly ReadOnlySpan<long> shape;
    private readonly ReadOnlySpan<long> strides;

    // Only the interop subsystem constructs views from validated native storage.
    internal ReadOnlyTensorView(T* data, long length, ReadOnlySpan<long> shape, ReadOnlySpan<long> strides)
    {
        this.data = data;
        this.shape = shape;
        this.strides = strides;
        Length = length;
    }

    /// <summary>Element count, independent of the storage's strides.</summary>
    public long Length { get; }

    /// <summary>Logical dimensions.</summary>
    public ReadOnlySpan<long> Shape => shape;

    /// <summary>Signed byte distances between adjacent elements along each axis.</summary>
    public ReadOnlySpan<long> Strides => strides;

    /// <summary>Read an element using flattened row-major logical indexing.</summary>
    public T this[long index] => *(T*)((byte*)data +
        checked((nint)TensorLayout.Offset(shape, strides, index, Length)));

    /// <summary>Borrow contiguous elements as a span. Strided layouts and counts above int32 are rejected.</summary>
    public ReadOnlySpan<T> AsSpan()
    {
        if (!TensorLayout.IsContiguous(shape, strides, sizeof(T), Length))
            throw new InvalidOperationException("A strided view cannot be represented by a contiguous span.");
        return new ReadOnlySpan<T>(data, checked((int)Length));
    }

    /// <summary>Supports C# fixed statements for this view's lifetime.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public ref readonly T GetPinnableReference() => ref *(Length == 0 ? null : data);
}
