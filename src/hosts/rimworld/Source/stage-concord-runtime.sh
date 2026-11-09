#!/usr/bin/env bash
set -euo pipefail

core_dir="$1"
tfm="$2"
dest_dir="$3"
min_bytes=1000000

core_dll="$core_dir/Assemblies/$tfm/Concord.dll"

[[ -f "$core_dll" ]] || { echo "Core built but produced no $core_dll" >&2; exit 1; }

size=$(stat -c%s "$core_dll")
if (( size < min_bytes )); then
  echo "Concord.dll at $core_dll is only $size bytes; expected the ILRepack-merged runtime at roughly 2.8MB (>= $min_bytes bytes). A pre-merge or mid-ILRepack artifact got staged instead." >&2
  exit 1
fi

mkdir -p "$dest_dir/Assemblies"
cp "$core_dll" "$dest_dir/Assemblies/0Concord.dll"
