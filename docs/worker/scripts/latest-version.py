#!/usr/bin/env python3
"""Print the canonical `latest` version (highest vX.Y branch, else unstable)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from versionsel import latest, version_branches

print(latest(version_branches()))
