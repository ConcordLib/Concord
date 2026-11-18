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

CONTRACT_BIN=tests/Concord.PackageContract/bin/Release
CHECKS="$CONTRACT_BIN/net10.0/Concord.PackageContract.dll"

echo "package-contract: checking the net10.0 dll has one manifest, no concord attribute and no generated type with a concord interface"
dotnet "$CHECKS" metadata "$CONTRACT_BIN/net10.0/Concord.PackageContract.dll" none
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'net10.0 metadata' failed" >&2
    exit 1
fi

echo "package-contract: checking the net472 dll has concord attributes, the registry attribute and one manifest"
dotnet "$CHECKS" metadata "$CONTRACT_BIN/net472/Concord.PackageContract.dll" some
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'net472 metadata' failed" >&2
    exit 1
fi

echo "package-contract: running Patcher.Apply on the net10.0 build"
dotnet "$CHECKS" apply
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'Patcher.Apply' failed" >&2
    exit 1
fi

echo "package-contract: restoring and building tests/Concord.PackageContract/Both"
dotnet build tests/Concord.PackageContract/Both -c Release
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'dotnet build Both' failed" >&2
    exit 1
fi

echo "package-contract: checking the Both dll has concord attributes, the registry attribute and one manifest"
dotnet "$CHECKS" metadata tests/Concord.PackageContract/Both/bin/Release/net472/Concord.PackageContract.Both.dll some
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'Both metadata' failed" >&2
    exit 1
fi

RUNTIME_PKG="$NUGET_PACKAGES/concord.runtime/0.0.0-contract"

echo "package-contract: checking Concord.Runtime depends on Concord.Generators only"
DEPENDENCIES=$(/usr/bin/grep -c '<dependency ' "$RUNTIME_PKG/concord.runtime.nuspec")
GENERATOR_DEPENDENCIES=$(/usr/bin/grep -c '<dependency id="Concord.Generators" version="\[0.0.0-contract\]"' "$RUNTIME_PKG/concord.runtime.nuspec")
if [[ "$DEPENDENCIES" -ne 2 || "$GENERATOR_DEPENDENCIES" -ne 2 ]]; then
    echo "package-contract: step 'Runtime dependencies' failed, found $DEPENDENCIES dependencies and $GENERATOR_DEPENDENCIES on Concord.Generators [0.0.0-contract]" >&2
    exit 1
fi

for tfm in net10.0 net472; do
    echo "package-contract: checking the $tfm Concord.dll in Concord.Runtime has no generator type"
    dotnet "$CHECKS" merged "$RUNTIME_PKG/lib/$tfm/Concord.dll"
    if [[ $? -ne 0 ]]; then
        echo "package-contract: step '$tfm merged' failed" >&2
        exit 1
    fi
done

echo "package-contract: restoring and building tests/Concord.PackageContract/Runtime"
dotnet build tests/Concord.PackageContract/Runtime -c Release
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'dotnet build Runtime' failed" >&2
    exit 1
fi

RUNTIME_BIN=tests/Concord.PackageContract/Runtime/bin/Release

echo "package-contract: checking the Runtime net10.0 dll has one manifest, no concord attribute and no generated type with a concord interface"
dotnet "$CHECKS" metadata "$RUNTIME_BIN/net10.0/Concord.PackageContract.Runtime.dll" none
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'Runtime net10.0 metadata' failed" >&2
    exit 1
fi

echo "package-contract: checking the Runtime net472 dll has concord attributes, the registry attribute and one manifest"
dotnet "$CHECKS" metadata "$RUNTIME_BIN/net472/Concord.PackageContract.Runtime.dll" some
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'Runtime net472 metadata' failed" >&2
    exit 1
fi

echo "package-contract: running Patcher.Apply on the Runtime net10.0 build"
dotnet "$CHECKS" apply "$RUNTIME_BIN/net10.0/Concord.PackageContract.Runtime.dll"
if [[ $? -ne 0 ]]; then
    echo "package-contract: step 'Runtime Patcher.Apply' failed" >&2
    exit 1
fi

echo "package-contract: ok"
