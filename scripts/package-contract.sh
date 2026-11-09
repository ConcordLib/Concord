#!/usr/bin/env bash
set -uo pipefail

usage() {
    cat >&2 <<'EOF'
Usage: scripts/package-contract.sh

Packs Concord.Ref, Concord.Runtime, Concord.Analyzers, Concord.Generators, Concord.Waylith,
Concord.MonoMod and Concord.HarmonyX to a temporary local feed, then restores and builds
tests/Concord.PackageContract against that feed and nuget.org only. Proves the published
NuGet surface compiles on its own, with no project reference into this tree.
EOF
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 1
fi

cd "$(dirname "$0")/.."

FEED=$(mktemp -d)
trap 'rm -rf "$FEED"' EXIT
export FEED
NUGET_PACKAGES="$FEED/gpf/"
export NUGET_PACKAGES

FORK_RUNTIME_DIR=$(bash scripts/fork-refs.sh)
if [[ -z "$FORK_RUNTIME_DIR" ]]; then
    echo "package-contract: step 'resolve fork runtime' failed, Concord.Waylith needs a WaylithCLR runtime to compile against" >&2
    exit 1
fi

echo "package-contract: building src/core/Concord/Concord.csproj"
dotnet build src/core/Concord/Concord.csproj -c Release -p:ForkRuntimeDir="$FORK_RUNTIME_DIR"
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'dotnet build src/core/Concord/Concord.csproj' failed" >&2
    exit 1
fi

PROJECTS=(
    src/core/Concord.Ref/Concord.Ref.csproj
    src/core/Concord/Concord.csproj
    src/core/Concord.Analyzers/Concord.Analyzers.csproj
    src/core/Concord.Generators/Concord.Generators.csproj
    src/backends/Concord.Waylith/Concord.Waylith.csproj
    src/backends/Concord.MonoMod/Concord.MonoMod.csproj
    src/backends/Concord.HarmonyX/Concord.HarmonyX.csproj
)

for proj in "${PROJECTS[@]}"; do
    echo "package-contract: packing $proj"
    dotnet pack "$proj" -c Release -o "$FEED" -p:MinVerVersionOverride=0.0.0-contract -p:ForkRuntimeDir="$FORK_RUNTIME_DIR"
    if [[ $? -ne 0 ]]; then
        echo "package-contract: step 'dotnet pack $proj' failed" >&2
        exit 1
    fi
done

echo "package-contract: nupkgs in feed"
ls "$FEED"/*.nupkg

echo "package-contract: restoring tests/Concord.PackageContract"
dotnet restore tests/Concord.PackageContract -p:ForkRuntimeDir="$FORK_RUNTIME_DIR"
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'dotnet restore' failed" >&2
    exit 1
fi

echo "package-contract: building tests/Concord.PackageContract"
dotnet build tests/Concord.PackageContract --no-restore -c Release -p:ForkRuntimeDir="$FORK_RUNTIME_DIR"
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'dotnet build' failed" >&2
    exit 1
fi

echo "package-contract: ok"
