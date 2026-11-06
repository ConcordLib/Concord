#!/usr/bin/env bash
set -uo pipefail

usage() {
    cat >&2 <<'EOF'
Usage: tests/run-net472.sh [filter]

`dotnet test` cannot run a net472 assembly on Linux: its net472 path wants
Extensions/TestHostNetFramework/testhost.net472.exe, which the Linux SDK does not ship.
This script runs the net472 test assemblies directly with xunit's net4x console
runner under mono instead.

Runs net472 test assemblies under tests/*/bin/$CONFIG/net472. $CONFIG defaults to
Debug; set CONFIG=Release to pick the Release build instead.

  filter   only run assemblies whose file name contains this substring

Examples:
  ./tests/run-net472.sh            # every net472 test assembly
  ./tests/run-net472.sh Detour     # only assemblies matching "Detour"
  CONFIG=Release ./tests/run-net472.sh Emit
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

filter="${1:-}"
total=0
failed=0
skipped=0
ran=0

summary='Total: ([0-9]+), Errors: ([0-9]+), Failed: ([0-9]+), Skipped: ([0-9]+)'

for dll in tests/*/bin/"$CONFIG"/net472/*.Tests.dll; do
    [[ -e "$dll" ]] || continue
    dir=${dll%/*}
    name=${dll##*/}
    [[ -n "$filter" ]] && [[ "$name" != *"$filter"* ]] && continue

    output=$(cd "$dir" && mono "$RUNNER" "$name" -parallel none 2>&1)

    ran=$((ran + 1))
    if [[ ! "$output" =~ $summary ]]; then
        echo "FAIL  $name (runner produced no summary)"
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

    printf '%-6s %-42s %s\n' "$([[ "$((f + e))" -eq 0 ]] && echo ok || echo FAIL)" "$name" "$line"
done

if [[ "$ran" -eq 0 ]]; then
    if [[ -n "$filter" ]]; then
        echo "no net472 test assembly under tests/*/bin/$CONFIG/net472 matched '$filter'" >&2
    else
        echo "no net472 test assemblies found under tests/*/bin/$CONFIG/net472; build the solution first" >&2
    fi
    exit 1
fi

echo
echo "net472: $total tests across $ran assemblies, $failed failed, $skipped skipped"
[[ "$failed" -eq 0 ]]
