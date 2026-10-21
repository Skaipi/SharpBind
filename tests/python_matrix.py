"""Run the exact latest build on explicitly supplied CPython interpreters."""

import argparse
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--python", action="append", required=True, dest="interpreters")
    parser.add_argument("--wheels", type=Path)
    args = parser.parse_args()
    wheels = args.wheels or Path(
        json.loads((ROOT / "artifacts/latest-build.json").read_text())["wheels"]
    )
    for interpreter in args.interpreters:
        subprocess.run(
            [
                interpreter,
                str(ROOT / "tests/install_wheels.py"),
                "--wheels",
                str(wheels),
            ],
            check=True,
        )


if __name__ == "__main__":
    main()
