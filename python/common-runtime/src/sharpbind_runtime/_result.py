"""Result slots and the transfer or release of returned native storage."""

import ctypes as ct
from ._abi import _element, Descriptor
from ._conversion import MANAGED_ARRAY_LIMIT


class _Result:
    def __init__(self, spec):
        self.spec = spec
        self.length = ct.c_int64()
        self.buffer = spec["kind"] in ("array", "string")
        if spec["kind"] == "void":
            self.output = None
        elif spec["kind"] == "tensor":
            self.output = Descriptor()
        else:
            self.output = (
                ct.POINTER(_element(spec))() if self.buffer else _element(spec)()
            )

    def arguments(self):
        if self.spec["kind"] == "void":
            return []
        arguments = [ct.byref(self.output)]
        if self.buffer:
            arguments.append(ct.byref(self.length))
        return arguments

    def read(self, library):
        if self.spec["kind"] == "void":
            return None
        if self.spec["kind"] == "tensor":
            from ._borrow import adapter

            # The ndarray's buffer base retains the lease and its native provider.
            memory = adapter().adopt(ct.addressof(self.output), library)
            try:
                import numpy as np

                return np.asarray(memory)
            finally:
                # A retained conversion traceback must not keep failed adoptions alive.
                del memory
        if not self.buffer:
            if self.spec["kind"] == "handle":
                return self.output
            return (
                bool(self.output.value)
                if self.spec["dtype"] == "bool"
                else self.output.value
            )
        try:
            length = self.length.value
            if length == -1 and self.spec["nullable"]:
                return None
            if (
                length < 0
                or length > MANAGED_ARRAY_LIMIT
                or (length and not self.output)
            ):
                raise RuntimeError("Invalid native result buffer")
            if self.spec["kind"] == "string":
                return (
                    ct.string_at(self.output, length).decode("utf-8") if length else ""
                )
            import numpy as np

            if not length:
                return np.empty(0, dtype=self.spec["dtype"])
            # The ABI buffer is freed below. Return independent array storage so a
            # Python result cannot retain a view into that temporary native allocation.
            return np.ctypeslib.as_array(self.output, shape=(length,)).copy()
        finally:
            if self.output:
                library.release_buffer(self.output)
