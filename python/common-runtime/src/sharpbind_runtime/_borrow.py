"""Acquire native tensor storage and release retired owners without eager native imports."""

import atexit
import threading
from ._abi import TENSOR_DTYPE_CODES

_adapter = None
_adapter_lock = threading.Lock()


def adapter():
    global _adapter
    if _adapter is None:
        with _adapter_lock:
            if _adapter is None:
                from . import _buffer

                _adapter = _buffer
                atexit.register(_buffer.shutdown_leases)
    return _adapter


def collect():
    if _adapter is not None:
        _adapter.collect_leases()


def borrow(value, dtype, writable):
    """Prefer a DLPack export; retain ordinary CPU buffers when DLPack is unavailable."""
    expected = 0 if dtype is None else TENSOR_DTYPE_CODES.get(dtype)
    if expected is None:
        raise ValueError(f"Unsupported tensor dtype: {dtype}")
    native = adapter()
    if hasattr(value, "__dlpack__"):
        if tuple(value.__dlpack_device__()) != (1, 0):
            raise BufferError("Only CPU device 0 is supported")
        try:
            capsule = value.__dlpack__(max_version=(1, 0), copy=False)
        except TypeError as error:
            # Older producers accept no version/copy keywords. Do not retry producer failures.
            if "keyword" not in str(error):
                raise
            try:
                capsule = value.__dlpack__()
            except BufferError:
                # Legacy DLPack cannot describe a read-only NumPy buffer.
                return native.borrow(value, writable, expected)
        return native.from_dlpack(capsule, writable, expected)
    try:
        return native.borrow(value, writable, expected)
    except ValueError as error:
        raise BufferError(str(error)) from error
