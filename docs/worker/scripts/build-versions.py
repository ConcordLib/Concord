#!/usr/bin/env python3
"""Build versions.json from the repo's vX.Y branches.

`latest` is the highest vX.Y branch (or unstable if there are none). `versions`
lists every vX.Y branch newest-first, with `unstable` last. LATEST may be passed
in the environment to reuse the value computed earlier in the workflow.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from versionsel import latest, version_branches


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        default="versions.json",
        help="Path to write (default: versions.json)",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    branches = version_branches()
    data = {
        "latest": os.environ.get("LATEST", "").strip() or latest(branches),
        "versions": branches + ["unstable"],
    }

    with open(args.output, "w") as f:
        json.dump(data, f, indent=2)

    print(f"Wrote {args.output}:", data)


if __name__ == "__main__":
    main()
