#!/usr/bin/env bash
set -u

usage() {
    echo "usage: run-fork-tests.sh [-p:Name=Value ...] [-- exe-arg ...]" >&2
    echo "  args before -- go to dotnet build; args after -- go to the test exe" >&2
    echo "  (e.g. -- --report-trx --report-trx-filename fork-facts.trx)" >&2
    echo "  -p:ForkCheckedHost=true builds a host that checks il access, into bin/checked" >&2
    echo "  (run it with -- --filter-class '*.CheckedHostAccessTests' --filter-class '*.EnumStageTests')" >&2
    echo "  FORK_FACTS_COVERAGE=<file> runs the exe under coverlet and writes an opencover report there" >&2
}

case "${1:-}" in
    -h|--help) usage; exit 0 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$script_dir/Concord.Waylith.ForkTests.csproj"
configuration="${CONFIGURATION:-Release}"

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

if [ -n "${FORK_FACTS_COVERAGE:-}" ] && ! command -v coverlet >/dev/null; then
    echo "run-fork-tests: FORK_FACTS_COVERAGE is set but coverlet is not on PATH (dotnet tool install --global coverlet.console)" >&2
    exit 1
fi

dotnet build "$project" --nologo -c "$configuration" -tl:false "${build_args[@]}"
build_status=$?
if [ "$build_status" -ne 0 ]; then
    echo "run-fork-tests: build failed with exit $build_status" >&2
    exit "$build_status"
fi

target_dir="$(dotnet msbuild "$project" -nologo -getProperty:TargetDir -p:Configuration="$configuration" "${build_args[@]}")"
exe="${target_dir%/}/Concord.Waylith.ForkTests"
if [ ! -x "$exe" ]; then
    echo "run-fork-tests: build succeeded but no exe at $exe" >&2
    exit 1
fi

if [ -n "${FORK_FACTS_COVERAGE:-}" ]; then
    output="$(coverlet "$exe.dll" --target "$exe" --targetargs "${exe_args[*]}" \
        --include '[Concord.Waylith]*' --include '[Concord]*' \
        --exclude '[Concord.Waylith.ForkTests]*' \
        --format opencover --output "$FORK_FACTS_COVERAGE")"
    run_status=$?
else
    output="$("$exe" "${exe_args[@]}")"
    run_status=$?
fi
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
