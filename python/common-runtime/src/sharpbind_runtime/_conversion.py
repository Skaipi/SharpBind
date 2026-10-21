"""Checked scalar copies, numeric vector copies, and native tensor ownership."""

import ctypes as ct
import math
import operator

MANAGED_ARRAY_LIMIT = 0x7FFFFFC7

# Check ranges before ctypes or NumPy can silently wrap or truncate a value.
INTEGER_LIMITS = {
    "int8": (-(2**7), 2**7 - 1),
    "uint8": (0, 2**8 - 1),
    "int16": (-(2**15), 2**15 - 1),
    "uint16": (0, 2**16 - 1),
    "int32": (-(2**31), 2**31 - 1),
    "uint32": (0, 2**32 - 1),
    "int64": (-(2**63), 2**63 - 1),
    "uint64": (0, 2**64 - 1),
}
FLOAT_LIMITS = {
    "float32": 3.4028234663852886e38,
    "float64": 1.7976931348623157e308,
}


def _scalar(value, spec):
    dtype = spec["dtype"]
    if dtype == "bool":
        value = operator.index(value)
        if value not in (0, 1):
            raise ValueError("Boolean values must be 0 or 1")
    elif dtype in INTEGER_LIMITS:
        value = operator.index(value)
        lower, upper = INTEGER_LIMITS[dtype]
        if not lower <= value <= upper:
            raise OverflowError(f"Value does not fit {dtype}")
    else:
        if isinstance(value, (str, bytes, complex)):
            raise TypeError("Expected a real number")
        value = float(value)
        if math.isfinite(value) and abs(value) > FLOAT_LIMITS[dtype]:
            raise OverflowError(f"Value does not fit {dtype}")
    return getattr(ct, spec["ct"])(value)


def _input(value, spec, keepalive):
    kind = spec["kind"]
    if kind == "scalar":
        return [_scalar(value, spec)]
    if kind == "tensor":
        from ._borrow import adapter, borrow

        memory = borrow(value, spec["dtype"], spec["access"] == "writable")
        if spec["lifetime"] == "retainable":
            # The capsule releases an unconsumed token if this or a later argument
            # fails. Managed adoption clears the token to prevent a second release.
            transfer = adapter().transfer(memory)
            keepalive.append(transfer)
            return [adapter().transfer_address(transfer)]
        keepalive.append(memory)
        return [memory.descriptor_address()]

    element = getattr(ct, spec["ct"])
    if value is None:
        if not spec["nullable"]:
            raise TypeError("None violates this parameter's non-null contract")
        return [ct.POINTER(element)(), -1]
    if kind == "string":
        if not isinstance(value, str):
            raise TypeError("Expected a string")
        encoded = value.encode("utf-8")
        if len(encoded) > MANAGED_ARRAY_LIMIT:
            raise OverflowError("String exceeds managed array limits")
        buffer = ct.create_string_buffer(encoded)
        keepalive.append(buffer)
        return [ct.cast(buffer, ct.POINTER(element)), len(encoded)]

    import numpy as np

    # All tensor producers use the same acquisition and validation path. Sequences
    # are a convenience for copied inputs only; sharing never silently converts.
    from ._borrow import borrow

    if isinstance(value, np.ndarray):
        value = _vector(value, spec)
    elif not hasattr(value, "__dlpack__"):
        try:
            memoryview(value)
        except TypeError:
            value = _vector(np.asarray(value), spec)
    acquired = borrow(value, None, False)
    keepalive.append(acquired)
    converted = _vector(np.asarray(acquired), spec)
    keepalive.append(converted)
    return [converted.ctypes.data_as(ct.POINTER(element)), converted.size]


def _vector(array, spec):
    import numpy as np

    if array.ndim != 1:
        raise ValueError(
            "Expected a 1D numeric vector; use a tensor view for multidimensional data"
        )
    if array.size > MANAGED_ARRAY_LIMIT:
        raise OverflowError("Array exceeds managed array limits")
    if array.dtype == np.dtype(spec["dtype"]):
        # C# performs the managed copy. Only normalize layouts the pointer/length ABI cannot express.
        converted = np.require(array, requirements=["C", "A"])
    else:
        converted = np.empty(array.shape, dtype=spec["dtype"])
        # NumPy casts can wrap integers or lose out-of-range values. Apply the same
        # checked conversion used by scalar parameters to every differing-dtype item.
        for i, item in enumerate(array):
            converted[i] = _scalar(item, spec).value
    return converted
