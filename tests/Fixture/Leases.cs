using System.Runtime.CompilerServices;
using SharpBind;

namespace SharpBind.Fixture;

/// <summary>A data structure retaining shared tensor storage until disposal.</summary>
[PythonExport]
public sealed class RetainedDataset : IDisposable
{
    private readonly Tensor<double> values;

    /// <summary>Keep an independent reference to the input buffer without copying elements.</summary>
    /// <param name="input">CPU buffer to retain.</param>
    [PythonExport]
    public RetainedDataset(Tensor<double> input) => values = input.Retain();

    /// <summary>Sum retained elements, including strided inputs.</summary>
    [PythonExport]
    public double Sum() => values.Read(Tensors.SumView);

    /// <summary>Run an existing span-based algorithm over contiguous retained storage.</summary>
    [PythonExport]
    public double SumContiguous() => values.Read(Tensors.SumContiguous);

    /// <summary>Get the retained address.</summary>
    [PythonExport]
    public ulong Address() => values.Read(Tensors.Address);

    /// <summary>Increment writable contiguous storage.</summary>
    [PythonExport]
    public void Increment() => values.Write(Tensors.Increment);

    /// <summary>Copy the retained shape metadata.</summary>
    [PythonExport]
    public long[] Shape() => values.Shape.ToArray();

    /// <summary>Copy the retained byte strides.</summary>
    [PythonExport]
    public long[] Strides() => values.Strides.ToArray();

    /// <summary>Release storage on a managed worker thread.</summary>
    [PythonExport]
    public void ReleaseOnWorker() => Task.Run(values.Dispose).GetAwaiter().GetResult();

    public void Dispose() => values.Dispose();
}

/// <summary>Exceptional and abandoned tensor lease lifetimes.</summary>
public static class LeaseTests
{
    /// <summary>Transfer the imported owner back to Python without copying its elements.</summary>
    /// <param name="input">Tensor owner to return.</param>
    /// <returns>The same storage with transferred ownership.</returns>
    [PythonExport]
    public static Tensor<double> Echo(Tensor<double> input) => input;

    private static Tensor<double>? unretained;
    private static ReadOnlyMemory<double> borrowedMemory;

    /// <summary>Use memory in a temporary heap computation without retaining an owner.</summary>
    /// <param name="input">Contiguous call-owned input.</param>
    [PythonExport]
    public static double SumMemory(Tensor<double> input)
    {
        var memory = input.Memory;
        return Task.Run(() =>
        {
            double total = 0;
            foreach (double value in memory.Span)
                total += value;
            return total;
        }).GetAwaiter().GetResult();
    }

    /// <summary>Save borrowed memory to verify invalidation when the call returns.</summary>
    /// <param name="input">Call-owned input.</param>
    [PythonExport]
    public static void SaveMemory(Tensor<double> input) => borrowedMemory = input.Memory;

    /// <summary>Check that borrowed memory cannot be accessed after its call.</summary>
    [PythonExport]
    public static bool SavedMemoryIsInvalid()
    {
        try
        {
            _ = borrowedMemory.Span.Length;
            return false;
        }
        catch (ObjectDisposedException) { return true; }
    }

    /// <summary>Save a call handle without retaining it, to check that the binding disposes it.</summary>
    /// <param name="input">Call-owned input.</param>
    [PythonExport]
    public static void SaveWithoutRetain(Tensor<double> input) => unretained = input;

    /// <summary>Check whether the saved call handle was disposed on return.</summary>
    [PythonExport]
    public static bool SavedIsDisposed() => unretained?.IsDisposed == true;

    /// <summary>Throw after acquiring two inputs.</summary>
    /// <param name="first">First input.</param>
    /// <param name="second">Second input.</param>
    [PythonExport]
    public static void Fail(Tensor<double> first, Tensor<double> second)
        => throw new InvalidOperationException("lease failure");

    /// <summary>Abandon an independently retained handle for finalizer testing.</summary>
    /// <param name="input">Input whose retained handle is abandoned.</param>
    [PythonExport]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Abandon(Tensor<double> input) => _ = input.Retain();

    /// <summary>Collect abandoned managed leases and wait for their finalizers.</summary>
    [PythonExport]
    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
