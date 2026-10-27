"""Coordinate input preparation, native calls, and result ownership."""

from ._arguments import prepare_arguments
from ._provider import NativeProvider
from ._result import _Result


class Library:
    """One generated package's call descriptions and native provider."""

    def __init__(self, schema, package_file):
        """schema from serialized json file and package_file set to caller's __file__"""
        self.native = NativeProvider(schema, package_file)
        self.methods = {m["entry"]: m for c in schema["classes"] for m in c["methods"]}

    def prepare(self, entry, arguments):
        """Acquire converted inputs before an instance is locked for a native call."""
        return prepare_arguments(arguments, self.methods[entry]["arguments"])

    def invoke(self, entry, handle, arguments):
        with self.prepare(entry, arguments) as values:
            return self.invoke_prepared(entry, handle, values)

    def invoke_prepared(self, entry, handle, values):
        """Execute while prepared inputs and any instance lease remain alive."""
        method = self.methods[entry]
        instance = []

        if not method["constructor"] and not method["is_static"]:
            if handle is None or not handle.token:
                raise RuntimeError("Native instance is closed")
            instance = [handle]

        result = _Result(method["result"])
        self.native.call(entry, *instance, *values, *result.arguments())
        return result.read(self.native)

    def release(self, entry, handle):
        self.native.call(entry, handle)
