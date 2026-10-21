namespace SharpBind.Interop;

/// <summary>
/// Infrastructure used by generated bindings. Application code uses the safe tensor types.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static unsafe class TensorInterop
{
    /// <summary>Consume one owned descriptor. Its ownership fields are cleared only on successful adoption.</summary>
    public static Tensor<T> Acquire<T>(TensorDescriptor* descriptor) where T : unmanaged => Tensor<T>.Acquire(descriptor);

    /// <summary>Validates borrowed metadata whose owner lives for the entire synchronous call.</summary>
    public static ReadOnlyTensorView<T> ReadOnlyView<T>(TensorDescriptor* descriptor) where T : unmanaged
    {
        long length = TensorDescriptorValidation.Validate<T>(descriptor, false);
        return new((T*)descriptor->Data, length,
            new ReadOnlySpan<long>(descriptor->Shape, descriptor->Rank),
            new ReadOnlySpan<long>(descriptor->Strides, descriptor->Rank));
    }

    /// <summary>Validates writable storage whose owner lives for the entire synchronous call.</summary>
    public static TensorView<T> WritableView<T>(TensorDescriptor* descriptor) where T : unmanaged
    {
        long length = TensorDescriptorValidation.Validate<T>(descriptor, true);
        return new((T*)descriptor->Data, length,
            new ReadOnlySpan<long>(descriptor->Shape, descriptor->Rank));
    }

    /// <summary>Transfers one owned lease to the native caller and closes the managed owner.</summary>
    public static TensorDescriptor Detach<T>(Tensor<T> tensor) where T : unmanaged => tensor.DetachDescriptor();
}
