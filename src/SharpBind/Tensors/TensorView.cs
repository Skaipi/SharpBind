namespace SharpBind;

/// <summary>A synchronous, borrowed, writable, contiguous CPU view.</summary>
public readonly unsafe ref struct TensorView<T> where T : unmanaged
{
    private readonly T* data;
    private readonly ReadOnlySpan<long> shape;

    // Only the interop subsystem constructs views from validated native storage.
    internal TensorView(T* data, long length, ReadOnlySpan<long> shape)
    {
        this.data = data;
        this.shape = shape;
        Length = length;
    }

    /// <summary>Element count.</summary>
    public long Length { get; }

    /// <summary>Logical dimensions.</summary>
    public ReadOnlySpan<long> Shape => shape;

    /// <summary>Access a contiguous element for this view's lifetime.</summary>
    public ref T this[long index]
    {
        get
        {
            if ((ulong)index >= (ulong)Length) throw new IndexOutOfRangeException();
            return ref data[index];
        }
    }

    /// <summary>Borrow the elements as a span. Throws if the count exceeds the span's int32 limit.</summary>
    public Span<T> AsSpan() => new(data, checked((int)Length));

    /// <summary>Supports C# fixed statements for this view's lifetime.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public ref T GetPinnableReference() => ref *(Length == 0 ? null : data);
}
