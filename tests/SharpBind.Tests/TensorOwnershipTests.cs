using SharpBind;
using SharpBind.Interop;
using System.Runtime.InteropServices;

namespace SharpBind.Tests;

[TestClass]
[DoNotParallelize]
public class TensorOwnershipTests
{
    [TestMethod]
    public void EmptyShapesDoNotOverflowAnIrrelevantProduct()
    {
        long before = TensorMemory.ActiveAllocations;
        using (var tensor = Tensor<double>.Allocate(long.MaxValue, 2, 0))
        {
            Assert.ThrowsException<IndexOutOfRangeException>(() => _ = tensor[0]);
            Assert.AreEqual(before + 1, TensorMemory.ActiveAllocations);
        }
        Assert.AreEqual(before, TensorMemory.ActiveAllocations);
    }

    [TestMethod]
    public void OwnedStorageChecksBoundsReadonlyAndDispose()
    {
        long before = TensorMemory.ActiveAllocations;
        var tensor = Tensor<double>.FromArray([1, 2], readOnly: true);
        Assert.AreEqual(2, tensor[1]);
        Assert.ThrowsException<InvalidOperationException>(() => tensor[0] = 3);
        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = tensor[-1]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = tensor[2]);

        tensor.Dispose();
        tensor.Dispose(); // Second dispose should cause no harm
        Assert.ThrowsException<ObjectDisposedException>(() => _ = tensor[0]);
        Assert.AreEqual(before, TensorMemory.ActiveAllocations);
    }

    [TestMethod]
    public void InvalidAllocationsLeaveNoNativeOwner()
    {
        long before = TensorMemory.ActiveAllocations;
        Assert.ThrowsException<ArgumentException>(() => Tensor<double>.Allocate(-1));
        Assert.ThrowsException<OverflowException>(() => Tensor<double>.Allocate(long.MaxValue));
        Assert.ThrowsException<NotSupportedException>(() => Tensor<DayOfWeek>.Allocate(1));
        Assert.AreEqual(before, TensorMemory.ActiveAllocations);
    }

    [TestMethod]
    public void OwnedMetadataSurvivesDisposal()
    {
        long[] shape = [2, 3];
        var tensor = Tensor<double>.Allocate(shape);
        shape[0] = 99;
        tensor.Dispose();
        Assert.AreEqual(6L, tensor.Length);
        CollectionAssert.AreEqual(new long[] { 2, 3 }, tensor.Shape.ToArray());
    }

    [TestMethod]
    public void TensorDescriptorUsesTheDocumentedNativeLayout()
    {
        Assert.AreEqual(72, Marshal.SizeOf<TensorDescriptor>());
        var names = new[] { "Size", "Data", "Dtype", "Rank", "Shape", "Strides", "DeviceType", "DeviceId", "Flags", "Lease", "Release" };
        var offsets = new[] { 0, 8, 16, 20, 24, 32, 40, 44, 48, 56, 64 };
        for (int i = 0; i < names.Length; i++)
            Assert.AreEqual(offsets[i], (int)Marshal.OffsetOf<TensorDescriptor>(names[i]));
    }

    [TestMethod]
    public void HandleAndErrorUseTheDocumentedNativeLayouts()
    {
        Assert.AreEqual(16, Marshal.SizeOf<SbHandle>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<SbHandle>(nameof(SbHandle.Provider)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<SbHandle>(nameof(SbHandle.Token)));

        Assert.AreEqual(32, Marshal.SizeOf<SbError>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<SbError>(nameof(SbError.Size)));
        Assert.AreEqual(4, (int)Marshal.OffsetOf<SbError>(nameof(SbError.Category)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<SbError>(nameof(SbError.ManagedType)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<SbError>(nameof(SbError.Message)));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<SbError>(nameof(SbError.Lease)));
    }
}
