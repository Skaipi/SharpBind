using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SharpBind.Interop;

#pragma warning disable CS1591
// Handles retain managed objects. A call lease prevents disposal during an active call.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed unsafe class ObjectHandles
{
    private sealed class Entry(object value, bool concurrentCalls)
    {
        internal readonly object Value = value;
        internal readonly object Gate = new();
        internal readonly bool ConcurrentCalls = concurrentCalls;
        internal readonly HashSet<int> CallingThreads = new();
        internal bool Closing;
    }

    private readonly ConcurrentDictionary<ulong, Entry> Handles = new();
    // Tokens are local to this registry; pair them with a native allocation's identity
    // so another loaded provider cannot accidentally resolve the same token here.
    private readonly ulong Provider = (nuint)NativeMemory.Alloc(1);
    private long nextHandle;
    public SbHandle Add(object value, bool concurrentCalls = false)
    {
        try
        {
            ulong token = checked((ulong)Interlocked.Increment(ref nextHandle));
            if (Provider == 0 || token == 0 || !Handles.TryAdd(token, new Entry(value, concurrentCalls)))
                throw new InvalidOperationException("Handle capacity exhausted.");
            return new SbHandle { Provider = Provider, Token = token };
        }
        catch
        {
            if (value is IDisposable disposable) disposable.Dispose();
            throw;
        }
    }

    private Entry Resolve<T>(SbHandle handle) where T : class
    {
        if (handle.Provider != Provider) throw new ArgumentException("Foreign provider handle");
        if (!Handles.TryGetValue(handle.Token, out var entry)) throw new ArgumentException("Closed handle");
        if (entry.Value is not T) throw new ArgumentException("Incorrectly typed handle");

        return entry;
    }

    public sealed class CallLease<T> : IDisposable where T : class
    {
        private readonly Entry entry;
        public T Value => (T)entry.Value;
        private readonly int thread;
        private CallLease(Entry entry, int thread)
        {
            this.entry = entry;
            this.thread = thread;
        }
        public void Dispose()
        {
            lock (entry.Gate)
            {
                entry.CallingThreads.Remove(thread);
                Monitor.PulseAll(entry.Gate);
            }
        }
        internal static CallLease<T> Create(ObjectHandles handles, SbHandle handle)
        {
            var entry = handles.Resolve<T>(handle);
            lock (entry.Gate)
            {
                int thread = Environment.CurrentManagedThreadId;
                // Reject before waiting to avoid waiting on this thread's outer call.
                // Reentry stays forbidden when concurrent calls are enabled.
                if (entry.CallingThreads.Contains(thread))
                    throw new InvalidOperationException("Cannot reenter an active native call on this instance.");

                while (!entry.Closing && !entry.ConcurrentCalls && entry.CallingThreads.Count != 0)
                    Monitor.Wait(entry.Gate);

                if (entry.Closing)
                    throw new ObjectDisposedException(typeof(T).Name);

                var lease = new CallLease<T>(entry, thread);
                entry.CallingThreads.Add(thread);
                return lease;
            }
        }
    }

    public CallLease<T> Enter<T>(SbHandle handle) where T : class => CallLease<T>.Create(this, handle);

    public void Release<T>(SbHandle handle) where T : class
    {
        var entry = Resolve<T>(handle);
        lock (entry.Gate)
        {
            if (entry.Closing)
                throw new ObjectDisposedException(typeof(T).Name);
            if (entry.CallingThreads.Contains(Environment.CurrentManagedThreadId))
                throw new InvalidOperationException("Cannot release an instance from its active native call.");

            // Close admission before waking waiters. Calls that already resolved this
            // entry must also fail rather than prolong the drain with new leases.
            entry.Closing = true;
            Monitor.PulseAll(entry.Gate);

            while (entry.CallingThreads.Count != 0)
                Monitor.Wait(entry.Gate);
            Handles.TryRemove(handle.Token, out _);
        }
        // User Dispose code runs after removal and outside the admission lock.
        if (entry.Value is IDisposable disposable) disposable.Dispose();
    }
}
