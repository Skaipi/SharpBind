using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpBind.Interop;

#pragma warning disable CS1591
// Strings and vectors cross the ABI through temporary, independently owned storage.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public unsafe class CopiedValues
{
    private readonly ConcurrentDictionary<nint, byte> Buffers = new();
    private readonly UTF8Encoding Utf8 = new(false, true);
    public bool ReadBool(byte value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException("Boolean values must be 0 or 1.")
    };

    private int Validate<T>(T* data, long length) where T : unmanaged
    {
        if (length < 0 || length > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(length));
        if (data == null && length != 0) throw new ArgumentNullException(nameof(data));

        return checked((int)length);
    }

    public T[]? ReadArray<T>(T* data, long length, bool nullable) where T : unmanaged
    {
        if (nullable && data == null && length == -1) return null;

        return new ReadOnlySpan<T>(data, Validate(data, length)).ToArray();
    }

    public string? ReadString(byte* data, long length, bool nullable)
    {
        if (nullable && data == null && length == -1) return null;

        return Utf8.GetString(new ReadOnlySpan<byte>(data, Validate(data, length)));
    }

    public void WriteString(string? value, byte** result, long* length, bool nullable) =>
        WriteArray(value is null ? null : Utf8.GetBytes(value), result, length, nullable);

    public void WriteArray<T>(T[]? value, T** result, long* length, bool nullable) where T : unmanaged
    {
        if (value is null)
        {
            if (!nullable) throw new InvalidOperationException("Managed result violates the non-null contract.");
            *length = -1;
            return;
        }

        if (value.Length == 0) return;

        T* buffer = (T*)NativeMemory.Alloc(checked((nuint)value.Length * (nuint)sizeof(T)));
        if (buffer == null) throw new OutOfMemoryException();

        try
        {
            value.AsSpan().CopyTo(new Span<T>(buffer, value.Length));
            if (!Buffers.TryAdd((nint)buffer, 0)) throw new InvalidOperationException("Buffer already registered.");
        }
        catch
        {
            NativeMemory.Free(buffer);
            throw;
        }

        *result = buffer;
        *length = value.Length;
    }

    public void Free(void* buffer)
    {
        if (buffer == null) return;
        if (!Buffers.TryRemove((nint)buffer, out _)) throw new ArgumentException("Unknown or already freed result buffer.");
        NativeMemory.Free(buffer);
    }
}
