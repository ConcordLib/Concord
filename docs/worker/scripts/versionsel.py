"""Shared helpers for docs version selection from vX.Y git branches."""
import os
import re
import subprocess

VER = re.compile(r"v\d+\.\d+")


def sort_key(v):
    return tuple(int(x) for x in v[1:].split("."))


def version_branches():
    """Every vX.Y branch on origin, plus the VERSION being deployed, newest-first."""
    out = subprocess.run(
        ["git", "ls-remote", "--heads", "origin", "refs/heads/v[0-9]*.[0-9]*"],
        capture_output=True,
        text=True,
    ).stdout
    names = set()
    for line in out.splitlines():
        name = line.split("refs/heads/")[-1].strip()
        if VER.fullmatch(name):
            names.add(name)
    version = os.environ.get("VERSION", "").strip()
    if VER.fullmatch(version):
        names.add(version)
    return sorted(names, key=sort_key, reverse=True)


def latest(branches):
    return branches[0] if branches else "unstable"
