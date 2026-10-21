using System.Numerics;
using SharpBind;

namespace SharpBind.Fixture;

/// <summary>Accumulate numeric values.</summary>
[PythonExport(typeof(int), Name = "AccumulatorInt32")]
[PythonExport(typeof(double), Name = "AccumulatorDouble")]
public sealed class Accumulator<T> where T : INumber<T>
{
    private T total;
    /// <summary>Start an accumulator.</summary>
    /// <param name="initial">The initial total.</param>
    [PythonExport] public Accumulator(T initial) => total = initial;
    /// <summary>Add <paramref name="amount"/> to the total.</summary>
    /// <param name="amount">The value to add.</param>
    /// <returns>The new total.</returns>
    [PythonExport] public T Add(T amount) => total += amount;
    /// <summary>Copy a vector without sharing its storage.</summary>
    /// <param name="values">The vector to copy.</param>
    /// <returns>An independent vector.</returns>
    [PythonExport] public static T[] Echo(T[] values) => values;
    public Dictionary<string, T> NotExported() => [];
}

/// <summary>Exercise Python builtin names and Unicode default values.</summary>
[PythonExport]
public static class @int
{
    /// <summary>Return text with a Unicode default.</summary>
    /// <param name="value">The text to return.</param>
    [PythonExport]
    public static string Echo(string value = "🌍") => value;

    /// <summary>Return an integer despite the enclosing class name.</summary>
    /// <param name="value">The integer to return.</param>
    [PythonExport]
    public static int Number(int value) => value;
}

/// <summary>Copying conversions and scalar operations.</summary>
public static class Functions
{
    /// <summary>Negate a boolean.</summary>
    /// <param name="value">The boolean value.</param>
    [PythonExport] public static bool Negate(bool value) => !value;
    /// <summary>Copy nullable UTF-8 text.</summary>
    /// <param name="value">The text to return.</param>
    [PythonExport] public static string? EchoText(string? value = null) => value;
    /// <summary>Return a null vector.</summary>
    [PythonExport] public static int[]? NullArray() => null;
    /// <summary>Raise a structured managed error.</summary>
    /// <exception cref="InvalidOperationException">Always raised to exercise the error boundary.</exception>
    [PythonExport] public static void Fail() => throw new InvalidOperationException("fixture failure: żółć");
    /// <summary>Return an integer.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport(Name = "identity_int")] public static int Identity(int value) => value;
    /// <summary>Return a double.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport(Name = "identity_double")] public static double Identity(double value) => value;
    /// <summary>Return a Byte value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static byte Byte(byte value) => value;
    /// <summary>Return a SByte value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static sbyte SByte(sbyte value) => value;
    /// <summary>Return a Short value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static short Short(short value) => value;
    /// <summary>Return a UShort value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static ushort UShort(ushort value) => value;
    /// <summary>Return a UInt value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static uint UInt(uint value) => value;
    /// <summary>Return a Long value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static long Long(long value) => value;
    /// <summary>Return a ULong value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static ulong ULong(ulong value) => value;
    /// <summary>Return a Float value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static float Float(float value) => value;
}

/// <summary>A deterministically disposable instance.</summary>
[PythonExport]
public sealed class DisposableCounter : IDisposable
{
    private static int disposed;
    /// <summary>Create a disposable instance.</summary>
    [PythonExport]
    public DisposableCounter()
    {
    }
    /// <summary>Count completed disposals.</summary>
    [PythonExport] public static int Disposed() => disposed;
    public void Dispose() => Interlocked.Increment(ref disposed);
}

/// <summary>Named methods and optional constructor arguments.</summary>
[PythonExport]
public sealed class NamedApi
{
    private readonly int initial;
    /// <summary>Create a named API with an optional starting value.</summary>
    /// <param name="initial">The initial value.</param>
    [PythonExport] public NamedApi(int initial = 0) => this.initial = initial;
    /// <summary>Return the initial value.</summary>
    [PythonExport] public int Initial() => initial;
    /// <summary>Return <paramref name="value"/> as an integer.</summary>
    /// <param name="value">The value to return.</param>
    /// <returns>The unchanged value.</returns>
    /// <example>NamedApi.echo_int(value=3)</example>
    [PythonExport] public static int EchoInt(int value) => value;
    /// <summary>Return a floating-point value.</summary>
    /// <param name="value">The value to return.</param>
    [PythonExport] public static double EchoDouble(double value) => value;

}

/// <summary>Example third-party code adapted by an annotated facade.</summary>
public static class ThirdParty
{
    /// <summary>Return the caller's input.</summary>
    /// <param name="managedName">The input integer.</param>
    public static int Identity(int managedName) => managedName;
}

/// <summary>An annotated facade over a third-party API.</summary>
public static class ExternalFunctions
{
    /// <summary>Return the caller's input.</summary>
    /// <param name="userInput">The input integer.</param>
    [PythonExport] public static int Identity(int userInput) => ThirdParty.Identity(userInput);
}

/// <summary>Tensor sharing and storage ownership examples.</summary>
public static class Tensors
{
    /// <summary>Sum a strided CPU view.</summary>
    /// <param name="values">Read-only borrowed values.</param>
    [PythonExport]
    public static double SumView(ReadOnlyTensorView<double> values)
    {
        double total = 0;
        for (long i = 0; i < values.Length; i++)
            total += values[i];
        return total;
    }
    /// <summary>Sum a contiguous CPU view through a span-based algorithm.</summary>
    /// <param name="values">Read-only contiguous values.</param>
    [PythonExport]
    public static double SumContiguous(ReadOnlyTensorView<double> values) => Sum(values.AsSpan());

    private static double Sum(ReadOnlySpan<double> values)
    {
        double total = 0;
        foreach (double value in values)
            total += value;
        return total;
    }

    /// <summary>Get the address seen by managed code.</summary>
    /// <param name="values">Borrowed values.</param>
    [PythonExport]
    public static unsafe ulong Address(ReadOnlyTensorView<double> values)
    {
        fixed (double* pointer = values)
            return (ulong)pointer;
    }
    /// <summary>Add one in place.</summary>
    /// <param name="values">Writable contiguous values.</param>
    [PythonExport]
    public static void Increment(TensorView<double> values)
    {
        for (long i = 0; i < values.Length; i++)
            values[i] += 1;
    }
    /// <summary>Allocate an independent tensor filled with consecutive numbers.</summary>
    /// <param name="count">Number of elements.</param>
    [PythonExport]
    public static Tensor<double> Create(int count = 4)
    {
        var result = Tensor<double>.Allocate(count);
        for (int i = 0; i < count; i++)
            result[i] = i;
        return result;
    }
    /// <summary>Allocate a read-only tensor.</summary>
    [PythonExport] public static Tensor<double> ReadOnly() => Tensor<double>.FromArray([1, 2, 3], readOnly: true);
    /// <summary>Count live native tensor allocations.</summary>
    [PythonExport] public static long Active() => TensorMemory.ActiveAllocations;
    /// <summary>Count completed native tensor releases.</summary>
    [PythonExport] public static long Released() => TensorMemory.TotalReleased;
}

/// <summary>A controllable synchronous call for concurrent-close tests.</summary>
[PythonExport]
public sealed class BlockingCall : IDisposable
{
    private static int entered, disposed;
    private static readonly ManualResetEventSlim resume = new(false);
    /// <summary>Create a blocking instance.</summary>
    [PythonExport]
    public BlockingCall()
    {
        entered = 0;
        resume.Reset();
    }
    /// <summary>Block until the test releases this call.</summary>
    [PythonExport]
    public int Wait()
    {
        Interlocked.Increment(ref entered);
        resume.Wait();
        return disposed;
    }
    /// <summary>Count calls that entered managed code.</summary>
    [PythonExport] public static int Entered() => Volatile.Read(ref entered);
    /// <summary>Resume blocked calls.</summary>
    [PythonExport] public static void Resume() => resume.Set();
    /// <summary>Count disposals.</summary>
    [PythonExport] public static int Disposed() => Volatile.Read(ref disposed);
    public void Dispose() => Interlocked.Increment(ref disposed);
}

/// <summary>Concurrent calls with controllable completion for lifetime tests.</summary>
[PythonExport(ConcurrentCalls = true)]
public sealed class ConcurrentBlockingCall : IDisposable
{
    private static int entered, disposed;
    private static readonly ManualResetEventSlim resume = new(false);
    /// <summary>Create a concurrent instance.</summary>
    [PythonExport]
    public ConcurrentBlockingCall()
    {
        entered = 0;
        resume.Reset();
    }
    /// <summary>Wait until the test allows every active call to finish.</summary>
    [PythonExport]
    public int Wait()
    {
        Interlocked.Increment(ref entered);
        resume.Wait();
        return Volatile.Read(ref disposed);
    }
    /// <summary>Count calls that entered managed code.</summary>
    [PythonExport] public static int Entered() => Volatile.Read(ref entered);
    /// <summary>Resume all blocked calls.</summary>
    [PythonExport] public static void Resume() => resume.Set();
    /// <summary>Count completed disposals.</summary>
    [PythonExport] public static int Disposed() => Volatile.Read(ref disposed);
    public void Dispose() => Interlocked.Increment(ref disposed);
}
