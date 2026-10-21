using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpBind;
using SharpBind.Interop;

namespace SharpBind.Tests;

[TestClass]
public class TensorLeaseTests
{
    [TestMethod]
    public unsafe void ConcurrentRetainAndDisposeDoNotInvalidateIndependentHandles()
    {
        double* data = stackalloc double[] { 3 };
        long* shape = stackalloc long[] { 1, 1 };
        long* strides = stackalloc long[] { 8, 8 };
        int references = 1;
        var descriptor = Descriptor(data, shape, strides, &references);
        using var source = TensorInterop.Acquire<double>(&descriptor);
        Parallel.For(0, 1000, _ =>
        {
            using var lease = source.Retain();
            using var copy = lease.Retain();
            lease.Dispose();
            Assert.AreEqual(3.0, copy[0]);
        });
        Assert.AreEqual(3.0, source[0]);
        source.Dispose();
        Assert.AreEqual(1, references);
    }

    [TestMethod]
    public unsafe void NativeOwnerReleasesExactlyOnceAndMetadataIsCopied()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 2 };
        long* strides = stackalloc long[] { 16, 8 };
        int references = 1;
        var descriptor = Descriptor(data, shape, strides, &references);
        using var first = TensorInterop.Acquire<double>(&descriptor);
        using var second = first.Retain();
        Assert.AreEqual(2, references);
        shape[0] = 99;
        strides[0] = 0;
        first.Dispose();
        first.Dispose();
        Assert.IsTrue(first.IsDisposed);
        Assert.IsFalse(second.IsDisposed);
        Assert.AreEqual(4L, first.Length);
        CollectionAssert.AreEqual(new long[] { 2, 2 }, second.Shape.ToArray());
        CollectionAssert.AreEqual(new long[] { 16, 8 }, second.Strides.ToArray());
        Assert.ThrowsException<ObjectDisposedException>(() => first.Retain());
        Assert.ThrowsException<ObjectDisposedException>(() => _ = first[0]);
        Assert.ThrowsException<ObjectDisposedException>(() => first.Read(Fixture.Tensors.SumView));
        Assert.AreEqual(10.0, second.Read(Fixture.Tensors.SumView));
        second.Write(Fixture.Tensors.Increment);
        Assert.AreEqual(2.0, data[0]);
        data[1] = 10;
        Assert.AreEqual(10.0, second[1]);
        second[0] = 20;
        Assert.AreEqual(39.0, second.Read(Fixture.Tensors.SumContiguous));
        Assert.AreEqual(2, references);
        second.Dispose();
        second.Dispose();
        Assert.AreEqual(1, references);
    }

    [TestMethod]
    public unsafe void NativeCallbackKeepsOwnerAliveThroughDisposalAndExceptions()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 2 };
        long* strides = stackalloc long[] { 16, 8 };
        int* references = stackalloc int[] { 1 };
        var descriptor = Descriptor(data, shape, strides, references);
        using var lease = TensorInterop.Acquire<double>(&descriptor);
        Assert.ThrowsException<InvalidOperationException>(() => lease.Read<double>(view =>
        {
            Task.Run(lease.Dispose).GetAwaiter().GetResult();
            Assert.AreEqual(2, *references);
            Assert.AreEqual(10.0, Fixture.Tensors.SumView(view));
            throw new InvalidOperationException("callback failure");
        }));
        Assert.AreEqual(1, *references);

        descriptor = Descriptor(data, shape, strides, references);
        using var writable = TensorInterop.Acquire<double>(&descriptor);
        writable.Write(view => { writable.Dispose(); view[0] = 42; });
        Assert.AreEqual(42.0, data[0]);
        Assert.AreEqual(1, *references);
    }

    [TestMethod]
    public void BorrowedMemoryExpiresWithItsHandle()
    {
        var tensor = Tensor<double>.FromArray([1, 2, 3]);
        var borrowed = tensor.Memory.Slice(1);
        using var retained = tensor.Retain();
        tensor.Dispose();
        Assert.ThrowsException<ObjectDisposedException>(() => _ = borrowed.Span.Length);
        Assert.ThrowsException<ObjectDisposedException>(() => _ = tensor.Memory);
        CollectionAssert.AreEqual(new double[] { 1, 2, 3 }, retained.Memory.ToArray());
    }

    [TestMethod]
    public unsafe void MemoryLeaseAndPinRetainForeignStorageIndependently()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 2 };
        long* strides = stackalloc long[] { 16, 8 };
        int references = 1;
        var descriptor = Descriptor(data, shape, strides, &references);
        using var lease = TensorInterop.Acquire<double>(&descriptor);
        using var memory = lease.Retain();
        var slice = memory.Memory.Slice(2, 2);
        lease.Dispose();
        Assert.AreEqual(3.0, slice.Span[0]);
        data[2] = 30;
        Assert.AreEqual(30.0, slice.Span[0]);

        using (var pin = slice.Pin())
        {
            memory.Dispose();
            memory.Dispose();
            Assert.AreEqual(2, references);
            Assert.AreEqual((nint)(data + 2), (nint)pin.Pointer);
            Assert.AreEqual(30.0, *(double*)pin.Pointer);
            Assert.ThrowsException<ObjectDisposedException>(() => _ = memory.Memory.Length);
            Assert.ThrowsException<ObjectDisposedException>(() => _ = slice.Span.Length);
            Assert.ThrowsException<ObjectDisposedException>(() => slice.Pin());
        }
        Assert.AreEqual(1, references);
    }

    [TestMethod]
    public unsafe void MemoryLeaseRejectsStridedAndOversizedStorageWithoutLeakingOwner()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 1 };
        long* strides = stackalloc long[] { 16, 8 };
        int references = 1;
        var descriptor = Descriptor(data, shape, strides, &references);
        using (var strided = TensorInterop.Acquire<double>(&descriptor))
        {
            Assert.ThrowsException<InvalidOperationException>(() => _ = strided.Memory);
            Assert.AreEqual(2, references);
        }
        Assert.AreEqual(1, references);

        shape[0] = (long)int.MaxValue + 1;
        strides[0] = 8;
        descriptor = Descriptor(data, shape, strides, &references);
        using (var oversized = TensorInterop.Acquire<double>(&descriptor))
        {
            Assert.ThrowsException<OverflowException>(() => _ = oversized.Memory);
            Assert.AreEqual(2, references);
        }
        Assert.AreEqual(1, references);
    }

    [TestMethod]
    public unsafe void InvalidNativeTensorLeavesOwnershipWithCaller()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 2 };
        long* strides = stackalloc long[] { 16, 8 };
        int references = 1;
        for (int fault = 0; fault < 8; fault++)
        {
            var original = Descriptor(data, shape, strides, &references);
            var invalid = original;
            switch (fault)
            {
                case 0: invalid.Size = 0; break;
                case 1: invalid.Lease = null; break;
                case 2: invalid.Release = null; break;
                case 3: invalid.Dtype = 1; break;
                case 4: invalid.DeviceType = 2; break;
                case 5: invalid.Flags = 8; break;
                case 6: invalid.Rank = 33; break;
                case 7: invalid.Shape = null; break;
            }
            bool rejected = false;
            try
            {
                using var lease = TensorInterop.Acquire<double>(&invalid);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                rejected = true;
            }
            finally
            {
                original.Release(original.Lease);
            }
            Assert.IsTrue(rejected, $"Fault {fault}");
            Assert.AreEqual(1, references);
        }
    }

    [TestMethod]
    public unsafe void TensorOwnersMoveThroughImportAndExportWithoutCopying()
    {
        double* data = stackalloc double[] { 1, 2, 3, 4 };
        long* shape = stackalloc long[] { 2, 2 };
        long* strides = stackalloc long[] { 16, 8 };
        int references = 1;
        var descriptor = Descriptor(data, shape, strides, &references);
        using var input = TensorInterop.Acquire<double>(&descriptor);
        Assert.IsTrue(descriptor.Lease == null && descriptor.Release == null);
        using var retained = input.Retain();
        var output = TensorInterop.Detach(input);
        Assert.IsTrue(input.IsDisposed);
        Assert.AreEqual(4L, input.Length);
        using var roundTrip = TensorInterop.Acquire<double>(&output);
        Assert.IsTrue(output.Lease == null && output.Release == null);
        retained.Dispose();
        data[1] = 20;
        Assert.AreEqual(28.0, roundTrip.Read(Fixture.Tensors.SumContiguous));
        Assert.AreEqual(2, references);
        roundTrip.Dispose();
        Assert.AreEqual(1, references);
    }

    private static unsafe TensorDescriptor Descriptor(double* data, long* shape, long* strides, int* references)
    {
        // The producer keeps its own reference and exports one independently owned token.
        Interlocked.Increment(ref *references);
        return new()
        {
            Size = (uint)sizeof(TensorDescriptor),
            Lease = references,
            Release = &Release,
            Data = data,
            Dtype = 10,
            Rank = 2,
            Shape = shape,
            Strides = strides,
            DeviceType = 1,
            Flags = 1
        };
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void Release(void* context) => Interlocked.Decrement(ref *(int*)context);
}
