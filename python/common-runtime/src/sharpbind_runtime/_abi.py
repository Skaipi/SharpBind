"""Native ABI layouts, stable tensor dtype IDs, and ctypes call signatures.

Keep layouts aligned with native/abi.h and SharpBind.Interop. See
docs/abi-maintenance.md when changing the contract.
"""

import ctypes as ct

# Must match BindingSchema.SchemaVersion in the managed generator.
SCHEMA_VERSION = 1

# Numeric keys are ABI identifiers. Existing IDs must never be renumbered.
TENSOR_DTYPES = {
    1: "int8",
    2: "uint8",
    3: "int16",
    4: "uint16",
    5: "int32",
    6: "uint32",
    7: "int64",
    8: "uint64",
    9: "float32",
    10: "float64",
}
TENSOR_DTYPE_CODES = {name: code for code, name in TENSOR_DTYPES.items()}


class Handle(ct.Structure):
    """An object token scoped to its native provider; matches sb_handle."""

    _fields_ = [
        ("provider", ct.c_uint64),
        ("token", ct.c_uint64),
    ]


class Error(ct.Structure):
    """Caller-initialized error record; its lease owns the UTF-8 strings."""

    _fields_ = [
        ("size", ct.c_uint32),
        ("category", ct.c_uint32),
        ("managed_type", ct.c_char_p),
        ("message", ct.c_char_p),
        ("lease", ct.c_void_p),
    ]

    def __init__(self):
        super().__init__()
        self.size = ct.sizeof(type(self))


class Descriptor(ct.Structure):
    """CPU tensor metadata; shape lengths and signed byte strides match sb_tensor."""

    _fields_ = [
        ("size", ct.c_uint32),
        ("data", ct.c_void_p),
        ("dtype", ct.c_int32),
        ("rank", ct.c_int32),
        ("shape", ct.POINTER(ct.c_int64)),
        ("strides", ct.POINTER(ct.c_int64)),
        ("device_type", ct.c_int32),
        ("device_id", ct.c_int32),
        ("flags", ct.c_uint64),
        ("lease", ct.c_void_p),
        ("release", ct.c_void_p),
    ]


def _element(spec):
    return Handle if spec["kind"] == "handle" else getattr(ct, spec["ct"])


def _argument_types(method):
    # Instance methods receive an implicit object handle before their explicit arguments.
    types = [] if method["constructor"] or method["is_static"] else [Handle]

    for arg in method["arguments"]:
        spec = arg["type"]
        if spec["kind"] == "tensor":
            types.append(ct.c_void_p)
        elif spec["kind"] == "scalar":
            types.append(_element(spec))
        elif spec["kind"] in ("array", "string"):
            types.extend([ct.POINTER(_element(spec)), ct.c_int64])
        else:
            raise ValueError(f"Unsupported argument kind: {spec['kind']}")

    # Results point to caller-provided storage that the native function fills.
    result = method["result"]
    if result:
        if result["kind"] == "tensor":
            types.append(ct.c_void_p)
        elif result["kind"] in ("scalar", "handle"):
            types.append(ct.POINTER(_element(result)))
        elif result["kind"] in ("array", "string"):
            types.extend(
                [ct.POINTER(ct.POINTER(_element(result))), ct.POINTER(ct.c_int64)]
            )
        else:
            raise ValueError(f"Unsupported result kind: {result['kind']}")

    return types + [ct.POINTER(Error)]
