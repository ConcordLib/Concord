#!/usr/bin/env bash
set -u

usage() {
    echo "usage: run-fork-tests.sh [-p:Name=Value ...] [-- exe-arg ...]" >&2
    echo "  args before -- go to dotnet build; args after -- go to the test exe" >&2
    echo "  (e.g. -- --report-trx --report-trx-filename fork-facts.trx)" >&2
}

case "${1:-}" in
    -h|--help) usage; exit 0 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$script_dir/Concord.Host.Adapter.ForkTests.csproj"
configuration="${CONFIGURATION:-Release}"
tfm="net10.0"
rid="linux-x64"

build_args=()
exe_args=()
past_separator=0
for arg in "$@"; do
    if [ "$past_separator" -eq 1 ]; then
        exe_args+=("$arg")
    elif [ "$arg" = "--" ]; then
        past_separator=1
    else
        build_args+=("$arg")
    fi
done

dotnet build "$project" --nologo -c "$configuration" -tl:false "${build_args[@]}"
build_status=$?
if [ "$build_status" -ne 0 ]; then
    echo "run-fork-tests: build failed with exit $build_status" >&2
    exit "$build_status"
fi

exe="$script_dir/bin/$configuration/$tfm/$rid/Concord.Host.Adapter.ForkTests"
if [ ! -x "$exe" ]; then
    echo "run-fork-tests: build succeeded but no exe at $exe" >&2
    exit 1
fi

output="$("$exe" "${exe_args[@]}")"
run_status=$?
printf '%s\n' "$output"

clean="$(printf '%s\n' "$output" | sed 's/\x1b\[[0-9;]*m//g')"
total=$(printf '%s\n' "$clean" | sed -n 's/^[[:space:]]*total: \([0-9]*\)$/\1/p' | tail -1)
failed=$(printf '%s\n' "$clean" | sed -n 's/^[[:space:]]*failed: \([0-9]*\)$/\1/p' | tail -1)
passed=$(printf '%s\n' "$clean" | sed -n 's/^[[:space:]]*succeeded: \([0-9]*\)$/\1/p' | tail -1)
skipped=$(printf '%s\n' "$clean" | sed -n 's/^[[:space:]]*skipped: \([0-9]*\)$/\1/p' | tail -1)
if [ -z "$total" ]; then
    echo "run-fork-tests: no test summary line in the run output" >&2
    exit 1
fi

echo "run-fork-tests: passed=$passed failed=$failed skipped=$skipped total=$total"

gap="$(printf '%s\n' "$output" | sed 's/\x1b\[[0-9;]*m//g' | grep -o 'fork-gap:.*' | head -1)"
if [ -n "$gap" ]; then
    echo "run-fork-tests: $gap" >&2
    echo "run-fork-tests: every body-replacement fact stands down on this runtime, so a green run here would only" >&2
    echo "run-fork-tests: be the pure encoder facts passing. Point ForkRuntimeDir at a pipeline runtime." >&2
    exit 1
fi

if [ "$run_status" -ne 0 ]; then
    echo "run-fork-tests: exe exited $run_status" >&2
    exit "$run_status"
fi

if [ "$total" -eq 0 ]; then
    echo "run-fork-tests: the run reported no facts at all" >&2
    exit 1
fi

exit 0
