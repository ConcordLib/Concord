#!/usr/bin/env python3
"""Rebuild concord-docs/versions.json in R2 from the repo's vX.Y branches.

`latest` is the highest vX.Y branch (or unstable if there are none). `versions`
lists every vX.Y branch newest-first, with `unstable` last. LATEST may be passed
in the environment to reuse the value computed earlier in the workflow.
"""
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from versionsel import latest, version_branches


def main():
    branches = version_branches()
    data = {
        "latest": os.environ.get("LATEST", "").strip() or latest(branches),
        "versions": branches + ["unstable"],
    }

    with open("versions.json", "w") as f:
        json.dump(data, f, indent=2)

    subprocess.run(
        [
            "wrangler",
            "r2",
            "object",
            "put",
            "concord-docs/versions.json",
            "--file=versions.json",
            "--content-type=application/json",
            "--remote",
        ],
        check=True,
    )
    print("Updated versions.json:", data)


if __name__ == "__main__":
    main()
