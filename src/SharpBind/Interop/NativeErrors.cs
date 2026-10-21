using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpBind.Interop;

#pragma warning disable CS1591
// Each failed call owns its message until the Python caller releases it.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public unsafe class NativeErrors
{
    private readonly ConcurrentDictionary<nint, byte> Errors = new();
    private readonly UTF8Encoding Utf8 = new(false, true);

    public void Begin(SbError* error)
    {
        if (error == null || error->Size != sizeof(SbError) || error->Lease != null)
            throw new ArgumentException("Supply an empty, correctly sized error record.");

        error->Category = 0;
        error->ManagedType = error->Message = null;
    }

    public int Fail(Exception exception, SbError* error)
    {
        if (error == null || error->Size != sizeof(SbError) || error->Lease != null) return -1;

        error->Category = exception switch
        {
            ArgumentException => 1,
            ObjectDisposedException => 2,
            OverflowException => 3,
            OutOfMemoryException => 4,
            NotSupportedException => 5,
            _ => 6
        };

        // Failure to allocate error text must itself never escape an unmanaged entry point.
        void* data = null;
        try
        {
            byte[] type = Utf8.GetBytes(exception.GetType().FullName ?? exception.GetType().Name);
            byte[] message = Utf8.GetBytes(exception.Message);
            data = NativeMemory.Alloc(checked((nuint)(type.Length + message.Length + 2)));
            if (data == null) return -1;

            type.CopyTo(new Span<byte>(data, type.Length));
            ((byte*)data)[type.Length] = 0;
            message.CopyTo(new Span<byte>((byte*)data + type.Length + 1, message.Length));
            ((byte*)data)[type.Length + message.Length + 1] = 0;
            if (!Errors.TryAdd((nint)data, 0)) throw new InvalidOperationException();

            error->ManagedType = (byte*)data;
            error->Message = (byte*)data + type.Length + 1;
            error->Lease = data;
        }
        catch
        {
            if (data != null) NativeMemory.Free(data);
        }
        return -1;
    }

    public void ErrorRelease(SbError* error)
    {
        if (error == null || error->Size != sizeof(SbError)) return;
        if (error->Lease != null && Errors.TryRemove((nint)error->Lease, out _))
            NativeMemory.Free(error->Lease);

        *error = new SbError { Size = (uint)sizeof(SbError) };
    }
}
