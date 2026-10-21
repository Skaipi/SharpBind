using SharpBind.Interop;

namespace SharpBind;

/// <summary>Reads a tensor while its storage is retained. The view must not escape the callback.</summary>
public delegate TResult TensorReader<T, TResult>(scoped ReadOnlyTensorView<T> values) where T : unmanaged;

/// <summary>Writes a contiguous tensor while its storage is retained. The view must not escape the callback.</summary>
public delegate void TensorWriter<T>(scoped TensorView<T> values) where T : unmanaged;

/// <summary>
/// An independently disposable owner of fixed-size native CPU storage. Retain inputs before storing
/// them beyond an exported call. Returning an owner transfers its reference without copying elements.
/// </summary>
public unsafe partial class Tensor<T> : IDisposable where T : unmanaged
{
    private readonly object gate = new();
    private TensorStorage<T>? storage;
    // Metadata survives disposal, but only storage represents this handle's live
    // ownership reference. Never use metadata to access elements after release.
    private readonly TensorStorage<T> metadata;

    private Tensor(TensorStorage<T> storage) => metadata = storage;

    // Allocated tensors transfer their initial token here, including cleanup on construction failure.
    internal Tensor(TensorDescriptor descriptor)
    {
        try
        {
            metadata = new TensorStorage<T>(descriptor);
            storage = metadata;
        }
        catch
        {
            if (descriptor.Lease != null && descriptor.Release != null)
                descriptor.Release(descriptor.Lease);
            throw;
        }
    }

    internal static Tensor<T> Acquire(TensorDescriptor* descriptor)
    {
        if (descriptor == null)
            throw new ArgumentException("Missing tensor descriptor.");
        var owner = new TensorStorage<T>(*descriptor);
        // Allocate everything before consuming the caller's ownership token.
        var result = new Tensor<T>(owner);
        result.storage = owner;
        descriptor->Lease = null;
        descriptor->Release = null;
        return result;
    }

    /// <summary>Element count. Metadata remains available after disposal or transfer.</summary>
    public long Length => metadata.Length;
    /// <summary>Immutable logical dimensions.</summary>
    public ReadOnlySpan<long> Shape => metadata.Shape;
    /// <summary>Immutable signed byte strides.</summary>
    public ReadOnlySpan<long> Strides => metadata.Strides;
    /// <summary>Whether the exporter disallows writing.</summary>
    public bool IsReadOnly => metadata.IsReadOnly;
    /// <summary>Whether the elements have C-contiguous layout.</summary>
    public bool IsContiguous => metadata.IsContiguous;
    /// <summary>Whether this handle has been disposed or transferred.</summary>
    public bool IsDisposed
    {
        get
        {
            lock (gate)
                return storage is null;
        }
    }

    /// <summary>Create another independently disposable handle to the same storage.</summary>
    public Tensor<T> Retain()
    {
        lock (gate)
        {
            var owner = GetStorage();
            var result = new Tensor<T>(owner);
            owner.AddReference();
            result.storage = owner;
            return result;
        }
    }

    /// <summary>Access flattened logical elements. Writing requires writable, contiguous storage.</summary>
    public T this[long index]
    {
        get
        {
            lock (gate)
                return GetStorage().ReadView()[index];
        }
        set
        {
            lock (gate)
                GetStorage().WriteView()[index] = value;
        }
    }

    /// <summary>Read through a borrowed view while a temporary reference keeps storage alive.</summary>
    public TResult Read<TResult>(TensorReader<T, TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Retain instead of holding gate across application code: the callback may
        // dispose the original handle, and its borrowed view must still remain valid.
        using var access = Retain();
        return reader(access.GetStorage().ReadView());
    }

    /// <summary>Write through a borrowed view while a temporary reference keeps storage alive.</summary>
    public void Write(TensorWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        using var access = Retain();
        writer(access.GetStorage().WriteView());
    }

    internal TensorStorage<T> GetStorage() => storage ?? throw new ObjectDisposedException(nameof(Tensor<T>));

    internal TensorDescriptor DetachDescriptor()
    {
        lock (gate)
        {
            // Export can allocate and fail. Invalidate this handle only after a native
            // cleanup token has taken responsibility for its existing reference.
            var result = GetStorage().Export();
            storage = null;
            GC.SuppressFinalize(this);
            return result;
        }
    }

    /// <summary>Release this handle exactly once. Other handles and active callbacks remain valid.</summary>
    public void Dispose()
    {
        ReleaseReference();
        GC.SuppressFinalize(this);
    }

    private void ReleaseReference()
    {
        TensorStorage<T>? owner;
        lock (gate)
        {
            owner = storage;
            storage = null;
        }
        // A foreign release callback can run arbitrary cleanup; invoke it outside gate.
        owner?.RemoveReference();
    }

    /// <summary>Release an abandoned owner.</summary>
    ~Tensor()
    {
        if (storage is not null)
            ReleaseReference();
    }
}
