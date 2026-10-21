"""Own converted inputs, including temporary buffer acquisitions."""

from contextlib import contextmanager
from ._conversion import _input


@contextmanager
def prepare_arguments(arguments, specifications):
    """Convert before taking an instance lease."""
    if len(arguments) != len(specifications):
        raise TypeError("Wrong argument count")

    owners = []
    values = []
    try:
        for value, specification in zip(arguments, specifications):
            values.extend(_input(value, specification["type"], owners))
        yield values
    finally:
        values.clear()
        owners.clear()
        # Dropping the last input owner can enqueue cleanup; drain after both lists
        # have released their references, including when a later conversion fails.
        # Scalar-only calls do not import the compiled tensor adapter.
        from ._borrow import collect

        collect()
