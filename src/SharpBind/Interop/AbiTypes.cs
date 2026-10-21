using System.Runtime.InteropServices;

namespace SharpBind.Interop;

/// <summary>Identifies an object within one native provider.</summary>
/// <remarks>Matches sb_handle in python/common-runtime/native/abi.h and Handle in the Python runtime's _abi.py.</remarks>
[StructLayout(LayoutKind.Sequential)]
public struct SbHandle
{
    /// <summary>Identity of the native library that created the object.</summary>
    public ulong Provider;

    /// <summary>Opaque object token; zero denotes an empty handle.</summary>
    public ulong Token;
}

/// <summary>An owned error record returned by a native call.</summary>
/// <remarks>Matches sb_error in python/common-runtime/native/abi.h and Error in the Python runtime's _abi.py.</remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SbError
{
    /// <summary>Size of this record, in bytes. The caller initializes it before a call.</summary>
    public uint Size;

    /// <summary>Error category interpreted by the Python provider.</summary>
    public uint Category;

    /// <summary>Null-terminated UTF-8 managed exception type, owned by the lease.</summary>
    public byte* ManagedType;

    /// <summary>Null-terminated UTF-8 error message, owned by the lease.</summary>
    public byte* Message;

    /// <summary>Opaque owner released by sharpbind_error_release.</summary>
    public void* Lease;
}
