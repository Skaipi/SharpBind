"""Native library loading, export binding and owned error records."""

import ctypes as ct
import os
import threading
from pathlib import Path
from ._abi import SCHEMA_VERSION, Error, Handle, _argument_types
from ._platform import current_target

_ERRORS = {
    1: (ValueError, "argument"),
    2: (RuntimeError, "closed"),
    3: (OverflowError, "overflow"),
    4: (MemoryError, "allocation"),
    5: (NotImplementedError, "unsupported"),
    6: (RuntimeError, "managed"),
}


def _runtime_path(library, package_file):
    try:
        target = current_target()
    except ValueError as error:
        raise NotImplementedError(str(error)) from error
    return (
        Path(package_file).resolve().parent
        / "_native"
        / target.rid
        / target.library_name(library)
    )


class NativeProvider:
    """Loads the bundled native library and configures its generated exports."""

    def __init__(self, schema, package_file):
        if schema.get("schema_version") != SCHEMA_VERSION:
            raise RuntimeError(
                "Unsupported binding schema version; rebuild with matching SharpBind packages."
            )
        self.schema = schema
        self.package_file = package_file
        self.lib = None
        self.lock = threading.Lock()
        self._dll_directory = None

    def call(self, entry, *arguments):
        """Resolve a configured export and translate its owned native error record."""
        self._load()
        self._call(getattr(self.lib, entry), *arguments)

    def release_buffer(self, pointer):
        """Free temporary native storage for a copied string or vector result."""
        self.call("sharpbind_free", ct.cast(pointer, ct.c_void_p))

    def _load(self):
        if self.lib is not None:
            return
        with self.lock:
            if self.lib is not None:
                return
            path = _runtime_path(self.schema["library"], self.package_file)
            if not path.is_file():
                raise FileNotFoundError(f"Native library is missing: {path}")
            if os.name == "nt" and self._dll_directory is None:
                self._dll_directory = os.add_dll_directory(str(path.parent))
            lib = ct.CDLL(str(path))
            self._configure_exports(lib)
            # Publish only after every ctypes signature is configured; another thread
            # may take the unlocked fast path as soon as lib becomes visible.
            self.lib = lib

    def _configure_exports(self, lib):
        try:
            self._bind_support(lib)
            self._bind_methods(lib)
        except (AttributeError, KeyError, ValueError, TypeError) as error:
            raise RuntimeError(f"Invalid native provider: {error}") from error

    def _bind_support(self, lib):
        self.free = lib.sharpbind_free
        self.free.argtypes = [ct.c_void_p, ct.POINTER(Error)]
        self.free.restype = ct.c_int32
        self.error_release = lib.sharpbind_error_release
        self.error_release.argtypes = [ct.POINTER(Error)]
        self.error_release.restype = None

    def _bind_methods(self, lib):
        for cls in self.schema["classes"]:
            if cls["release"]:
                release = getattr(lib, cls["release"])
                release.argtypes = [Handle, ct.POINTER(Error)]
                release.restype = ct.c_int32
            for method in cls["methods"]:
                function = getattr(lib, method["entry"])
                function.argtypes = _argument_types(method)
                function.restype = ct.c_int32

    def _call(self, function, *args):
        error = Error()
        try:
            status = function(*args, ct.byref(error))
            if status:
                exception, category = _ERRORS.get(
                    error.category, (RuntimeError, "unknown")
                )
                failure = exception(
                    (error.message or b"Native call failed").decode(
                        "utf-8", errors="replace"
                    )
                )
                failure.category = category
                failure.managed_type = (error.managed_type or b"").decode(
                    "utf-8", errors="replace"
                )
                raise failure
        finally:
            if error.lease:
                self.error_release(ct.byref(error))
            from ._borrow import collect

            collect()
