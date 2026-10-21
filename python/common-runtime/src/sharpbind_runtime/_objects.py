"""Lease native calls and release each managed instance once all calls finish."""

from contextlib import contextmanager
import threading
from typing import Self


class NativeObject:
    """Own one instance. Closing rejects new calls and waits for all active calls."""

    __slots__ = (
        "_handle",
        "_condition",
        "_active",
        "_closing",
        "_initialized",
        "_releasing",
    )
    _release_entry = None

    def __new__(cls, *args, **kwargs):
        self = super().__new__(cls)
        self._handle = None
        self._condition = threading.Condition()
        # Thread identities let same-thread reentry fail instead of waiting on itself.
        self._active = set()
        self._releasing = None
        self._closing = self._initialized = False
        return self

    def __init__(self):
        raise TypeError("This class has no exported constructor")

    def _initialize(self, entry, args):
        with self._condition:
            if self._initialized or self._closing:
                raise RuntimeError("Native instance is already initialized or closed")
            self._initialized = True
        with self._provider.prepare(entry, args) as values:
            with self._call_lease(initializing=True):
                self._handle = self._provider.invoke_prepared(entry, None, values)

    @property
    def closed(self) -> bool:
        with self._condition:
            return self._closing or self._handle is None

    def _invoke(self, entry, args):
        if self.closed:
            raise RuntimeError("Native instance is closed or uninitialized")
        # Conversion can execute Python user code, including another call or close().
        # Acquire the lease afterward so it rechecks the state changed by that code.
        with self._provider.prepare(entry, args) as values:
            with self._call_lease():
                return self._provider.invoke_prepared(entry, self._handle, values)

    @contextmanager
    def _call_lease(self, *, initializing=False):
        """Keep close out until invocation and result reading finish."""
        thread = threading.get_ident()
        with self._condition:
            self._reject_reentry(thread)
            # Native handle admission owns serialization and concurrency policy.
            # This lease only protects Python initialization and result handoff from close.
            if self._closing or (not initializing and self._handle is None):
                raise RuntimeError("Native instance is closed or uninitialized")
            self._active.add(thread)
        try:
            yield
        finally:
            with self._condition:
                self._active.remove(thread)
                self._condition.notify_all()

    def _reject_reentry(self, thread):
        if thread in self._active or thread == self._releasing:
            raise RuntimeError(
                "Cannot reenter an active native call or release on this instance"
            )

    def close(self) -> None:
        with self._condition:
            self._reject_reentry(threading.get_ident())
            if self._closing:
                while self._active or self._releasing is not None:
                    self._condition.wait()
                return
            self._closing = True
            self._condition.notify_all()
            while self._active:
                self._condition.wait()
            handle, self._handle = self._handle, None
            if handle is not None:
                # Other close callers must wait for native disposal as well as calls.
                # Record the releasing thread so a callback cannot wait on itself.
                self._releasing = threading.get_ident()
        try:
            if handle is not None:
                self._provider.release(self._release_entry, handle)
        finally:
            with self._condition:
                self._releasing = None
                self._condition.notify_all()

    def __enter__(self) -> Self:
        if self.closed:
            raise RuntimeError("Native instance is closed")
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass

    def __copy__(self):
        raise TypeError("Native instances cannot be copied")

    def __deepcopy__(self, memo):
        raise TypeError("Native instances cannot be copied")

    def __reduce_ex__(self, protocol):
        raise TypeError("Native instances cannot be pickled")
