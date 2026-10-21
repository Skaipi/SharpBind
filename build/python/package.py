"""MSBuild entry point; uses the developer's installed Python prerequisites."""

import sys

if sys.version_info < (3, 11):
    sys.exit(
        "SharpBind packaging requires Python 3.11 or later. Set SharpBindPythonExecutable to a supported interpreter."
    )

from sharpbind_pack.release import main

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"SharpBind packaging failed: {error}", file=sys.stderr)
        sys.exit(1)
