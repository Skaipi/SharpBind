using System.Runtime.InteropServices;

namespace SharpBind.Interop;

/// <summary>Native CPU tensor metadata with an optional owned storage lease.</summary>
/// <remarks>
/// Matches sb_tensor in python/common-runtime/native/abi.h and Descriptor in the Python runtime's _abi.py.
/// Shape and strides have Rank entries; strides are signed byte distances.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TensorDescriptor
{
    /// <summary>Size of this descriptor, in bytes.</summary>
    public uint Size;

    /// <summary>Address of logical element zero, including for reversed views.</summary>
    public void* Data;

    /// <summary>Stable numeric element ID assigned by TensorElement.</summary>
    public int Dtype;

    /// <summary>Number of dimensions; zero describes a scalar.</summary>
    public int Rank;

    /// <summary>Nonnegative dimension lengths.</summary>
    public long* Shape;

    /// <summary>Signed byte distances between adjacent elements along each axis.</summary>
    public long* Strides;

    /// <summary>DLPack device type. Only CPU (1) is supported.</summary>
    public int DeviceType;

    /// <summary>Device index. Only device zero is supported.</summary>
    public int DeviceId;

    /// <summary>Access flags; bit zero permits writes.</summary>
    public ulong Flags;

    /// <summary>Opaque storage owner passed to Release.</summary>
    public void* Lease;

    /// <summary>Thread-safe cdecl callback that releases Lease without throwing.</summary>
    public delegate* unmanaged[Cdecl]<void*, void> Release;
}
