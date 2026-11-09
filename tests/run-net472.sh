#!/usr/bin/env bash
set -uo pipefail

usage() {
    cat >&2 <<'EOF'
Usage: tests/run-net472.sh [filter]

`dotnet test` cannot run a net472 assembly on Linux: its net472 path wants
Extensions/TestHostNetFramework/testhost.net472.exe, which the Linux SDK does not ship.
This script runs the net472 test assemblies directly with xunit's net4x console
runner under mono instead.

Runs net472 test assemblies under tests/*/bin/$CONFIG/net472 and
src/hosts/*/Source/*/bin/$CONFIG/net472. $CONFIG defaults to Debug; set
CONFIG=Release to pick the Release build instead.

Set HARMONY_VERSION to a Lib.Harmony package version (e.g. 2.4.3) to swap that
release's 0Harmony.dll into Concord.MonoMod.Tests' bin directory before it runs,
and restore the 3.0.0-rc.4 build output copy afterwards.

  filter   only run assemblies whose file name contains this substring

Examples:
  ./tests/run-net472.sh            # every net472 test assembly
  ./tests/run-net472.sh Detour     # only assemblies matching "Detour"
  CONFIG=Release ./tests/run-net472.sh Emit
  HARMONY_VERSION=2.4.3 CONFIG=Release ./tests/run-net472.sh MonoMod
EOF
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 1
fi

CONFIG="${CONFIG:-Debug}"

cd "$(dirname "$0")/.."

command -v mono >/dev/null || { echo "mono is not installed" >&2; exit 1; }

RUNNER=$(find ~/.nuget/packages/xunit.runner.console -path '*net4*' -name 'xunit.console.exe' 2>/dev/null | sort | tail -1)
[[ -n "$RUNNER" ]] || { echo "xunit.runner.console net4x runner not restored; run a net472 build first" >&2; exit 1; }

if [[ -n "${HARMONY_VERSION:-}" ]]; then
    harmony_swap="$HOME/.nuget/packages/lib.harmony/$HARMONY_VERSION/lib/net472/0Harmony.dll"
    [[ -e "$harmony_swap" ]] || { echo "Lib.Harmony $HARMONY_VERSION carries no lib/net472/0Harmony.dll under $harmony_swap" >&2; exit 1; }
fi

filter="${1:-}"
total=0
failed=0
skipped=0
ran=0

summary='Total: ([0-9]+), Errors: ([0-9]+), Failed: ([0-9]+), Skipped: ([0-9]+)'

for dll in tests/*/bin/"$CONFIG"/net472/*.Tests.dll src/hosts/*/Source/*/bin/"$CONFIG"/net472/*.Tests.dll; do
    [[ -e "$dll" ]] || continue
    dir=${dll%/*}
    name=${dll##*/}
    [[ -n "$filter" ]] && [[ "$name" != *"$filter"* ]] && continue

    swapped=0
    if [[ -n "${HARMONY_VERSION:-}" ]] && [[ "$name" == "Concord.MonoMod.Tests.dll" ]]; then
        cp "$dir/0Harmony.dll" "$dir/0Harmony.dll.rc4"
        cp "$harmony_swap" "$dir/0Harmony.dll"
        swapped=1
    fi

    output=$(cd "$dir" && mono "$RUNNER" "$name" -parallel none 2>&1)

    if [[ "$swapped" -eq 1 ]]; then
        cp "$dir/0Harmony.dll.rc4" "$dir/0Harmony.dll"
        rm -f "$dir/0Harmony.dll.rc4"
    fi

    ran=$((ran + 1))
    if [[ ! "$output" =~ $summary ]]; then
        echo "FAIL  $dll (runner produced no summary)"
        failed=$((failed + 1))
        continue
    fi

    line=${BASH_REMATCH[0]}
    t=${BASH_REMATCH[1]}
    e=${BASH_REMATCH[2]}
    f=${BASH_REMATCH[3]}
    s=${BASH_REMATCH[4]}
    total=$((total + t))
    failed=$((failed + f + e))
    skipped=$((skipped + s))

    version_suffix=""
    [[ "$swapped" -eq 1 ]] && version_suffix=" (Lib.Harmony $HARMONY_VERSION)"

    printf '%-6s %-62s %s%s\n' "$([[ "$((f + e))" -eq 0 ]] && echo ok || echo FAIL)" "$dll" "$line" "$version_suffix"
done

if [[ "$ran" -eq 0 ]]; then
    if [[ -n "$filter" ]]; then
        echo "no net472 test assembly under tests/ or src/hosts/ for $CONFIG matched '$filter'" >&2
    else
        echo "no net472 test assemblies found for $CONFIG under tests/ or src/hosts/; build the solution first" >&2
    fi
    exit 1
fi

echo
echo "net472: $total tests across $ran assemblies, $failed failed, $skipped skipped"
[[ "$failed" -eq 0 ]]
