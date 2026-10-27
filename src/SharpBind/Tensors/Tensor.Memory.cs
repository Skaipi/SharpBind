using System.Buffers;

namespace SharpBind;

public unsafe partial class Tensor<T>
{
    private TensorMemoryManager? memoryManager;

    /// <summary>Borrow contiguous elements without creating another storage reference.</summary>
    /// <remarks>Keep this handle alive and undisposed while using its memory or spans.
    /// Retain a separate handle before storing memory beyond an exported call.</remarks>
    public ReadOnlyMemory<T> Memory
    {
        get
        {
            lock (gate)
            {
                var owner = GetStorage();
                if (!owner.IsContiguous)
                    throw new InvalidOperationException("Strided storage cannot be represented by contiguous memory.");
                return (memoryManager ??= new TensorMemoryManager(this, checked((int)owner.Length))).Memory;
            }
        }
    }

    /// <summary>Allocate zero-filled, writable native CPU storage.</summary>
    public static Tensor<T> Allocate(params long[] shape) => new(TensorMemory.Allocate<T>(shape, true));

    /// <summary>Copy managed elements into an independent native vector.</summary>
    public static Tensor<T> FromArray(T[] values, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Tensor<T>(TensorMemory.Allocate<T>([values.LongLength], !readOnly));
        values.AsSpan().CopyTo(new Span<T>(result.GetStorage().Data, values.Length));
        return result;
    }

    private class TensorMemoryManager(Tensor<T> tensor, int length) : MemoryManager<T>
    {
        public override Span<T> GetSpan()
        {
            lock (tensor.gate) return new Span<T>(tensor.GetStorage().Data, length);
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            lock (tensor.gate)
            {
                var owner = tensor.GetStorage();
                if ((uint)elementIndex > (uint)length)
                    throw new ArgumentOutOfRangeException(nameof(elementIndex));

                // Native storage cannot move, so pinning needs a lifetime reference,
                // not a GC pin. Each MemoryHandle gets its own independently retained owner.
                var retained = tensor.Retain();
                try
                {
                    return new MemoryHandle(owner.Data + elementIndex, default, new PinOwner(retained));
                }
                catch
                {
                    retained.Dispose();
                    throw;
                }
            }
        }

        // MemoryHandle.Dispose calls its PinOwner.Unpin, not this shared manager.
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private class PinOwner(Tensor<T> tensor) : IPinnable
    {
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException();
        public void Unpin() => tensor.Dispose();
    }
}
