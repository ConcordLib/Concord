#!/usr/bin/env bash
set -uo pipefail

want_native=0
case "${1:-}" in
    "") ;;
    --runtime) want_native=1 ;;
    *)
        echo "fork-refs: unknown argument '$1'. The only flag is --runtime, which asks for a directory that can host the fork facts and not only compile against them." >&2
        exit 2
        ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
report=""

note() {
    report="$report
  $1"
}

qualifies() {
    if [ ! -f "$1/System.Private.CoreLib.dll" ]; then
        note "$1: no System.Private.CoreLib.dll"
        return 1
    fi
    if [ ! -f "$1/RimWorld.runtimeconfig.json" ]; then
        note "$1: no RimWorld.runtimeconfig.json"
        return 1
    fi
    if [ "$want_native" -eq 1 ] && [ ! -f "$1/libcoreclr.so" ]; then
        note "$1: has the fork's dlls but no libcoreclr.so, so it can compile the adapter and not host the fork facts"
        return 1
    fi
    return 0
}

if [ -n "${FORK_RUNTIME_DIR:-}" ]; then
    printf '%s\n' "$FORK_RUNTIME_DIR"
    exit 0
fi

if command -v gamecrate >/dev/null 2>&1; then
    refs="$(cd "$repo_root" && gamecrate refs v17 2>/dev/null | tail -n1)"
    if [ -z "$refs" ]; then
        note "gamecrate refs v17: printed no directory"
    elif qualifies "$refs"; then
        printf '%s\n' "$refs"
        exit 0
    fi
else
    note "gamecrate: not on PATH"
fi

steam="/mnt/games/SteamLibrary/steamapps/common/RimWorld"
steam_config="$steam/RimWorld.runtimeconfig.json"
if [ ! -f "$steam_config" ]; then
    note "$steam: no RimWorld.runtimeconfig.json"
elif ! grep -q waylithclr "$steam_config"; then
    note "$steam: its runtimeconfig names no waylithclr runtime"
elif qualifies "$steam"; then
    printf '%s\n' "$steam"
    exit 0
fi

early="/home/aaron/.cache/waylith-earlybuild"
if qualifies "$early"; then
    printf '%s\n' "$early"
    exit 0
fi

wanted="System.Private.CoreLib.dll"
[ "$want_native" -eq 1 ] && wanted="$wanted and libcoreclr.so"
echo "fork-refs: no fork runtime found. A directory qualifies when it holds $wanted." >&2
echo "fork-refs: set FORK_RUNTIME_DIR, or build a game image so gamecrate refs can extract one. Tried:$report" >&2
exit 1
