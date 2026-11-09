#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/gamecrate-harness.sh"

VERSION="${1:-1.7}"
case "$VERSION" in -h|--help) echo "usage: $0 [1.1|1.2|1.3|1.4|1.5|1.6|1.7]"; exit 2 ;; esac
concord_check_version "$VERSION"

COSMERE_ROOT="${COSMERE_ROOT:-/home/aaron/projects/RimworldCosmere/RimworldCosmere}"
COSMERE_ABOUT="$COSMERE_ROOT/CosmereCore/About/About.xml"
COSMERE_DLL="$COSMERE_ROOT/CosmereCore/Assemblies/Cosmere.dll"
COSMERE_MODS=(CosmereCore CosmereScadrial CosmereRoshar)
RIMLOGGING_ROOT="${RIMLOGGING_ROOT:-/home/aaron/projects/RimWorks/rimworld-logging-framework}"
QUICKSTARTS_ROOT="${QUICKSTARTS_ROOT:-/home/aaron/projects/RimWorks/Quickstarts}"

for m in "${COSMERE_MODS[@]}"; do
  test -d "$COSMERE_ROOT/$m/About" || { echo "FAIL: $COSMERE_ROOT/$m is not a mod directory"; exit 1; }
done
test -f "$COSMERE_DLL" || { echo "FAIL: $COSMERE_DLL missing - build Cosmere first"; exit 1; }
test -f "$QUICKSTARTS_ROOT/$VERSION/Assemblies/Quickstarts.dll" || { echo "FAIL: no Quickstarts build for $VERSION at $QUICKSTARTS_ROOT/$VERSION/Assemblies"; exit 1; }
test -f "$RIMLOGGING_ROOT/$VERSION/Assemblies/RimWorks.RimLogging.dll" || { echo "FAIL: no RimLogging build for $VERSION at $RIMLOGGING_ROOT/$VERSION/Assemblies"; exit 1; }

SUPPORTED=$(sed -n '/<supportedVersions>/,/<\/supportedVersions>/p' "$COSMERE_ABOUT" | grep -oE '[0-9]+\.[0-9]+')
grep -qx "$VERSION" <<<"$SUPPORTED" || {
  echo "REFUSING($VERSION): Cosmere.Core's About.xml only declares supportedVersions={$(tr '\n' ' ' <<<"$SUPPORTED")}- running it on $VERSION would be a hollow pass, not a real regression"
  exit 2
}

concord_dlc_drops "$VERSION"
HARD_DLC=$(grep -oE '<packageId>Ludeon\.RimWorld\.[^<]+</packageId>' "$COSMERE_ABOUT" | sed -E 's#</?packageId>##g' | tr '[:upper:]' '[:lower:]')
for ((i = 1; i < ${#CONCORD_DLC_DROPS[@]}; i += 2)); do
  dropped="${CONCORD_DLC_DROPS[i]}"
  if grep -qx "$dropped" <<<"$HARD_DLC"; then
    echo "REFUSING($VERSION): Cosmere.Core hard-depends on $dropped, which RimWorld $VERSION does not have"
    exit 2
  fi
done

concord_build "$VERSION"
echo "runtime under test: fresh build for RimWorld $VERSION (md5 $(md5sum < "$CONCORD_REPO_ROOT/$VERSION/Assemblies/0Concord.dll" | cut -d' ' -f1))"
echo "consumer: $COSMERE_DLL ($(date -r "$COSMERE_DLL" '+%Y-%m-%d %H:%M'))"

RUN_STATUS=0
scenario_run "$VERSION" cosmere "[Quickstarts/Quickstarter] Loaded " 600 \
  "path:$RIMLOGGING_ROOT" "path:$QUICKSTARTS_ROOT" \
  "path:$COSMERE_ROOT/CosmereCore" "path:$COSMERE_ROOT/CosmereScadrial" "path:$COSMERE_ROOT/CosmereRoshar" \
  || RUN_STATUS=$?
LOG="$(scenario_log)"

fail() {
  echo "FAIL($VERSION): $1"
  echo "--- log ($LOG) ---"
  grep -aiE "\[Cosmere\]|CONC[0-9]{3}|Concord|Exception" "$LOG" 2>/dev/null | tail -40
  exit 1
}
require() { grep -aq "$1" "$LOG" || fail "$2"; }
forbid()  { if grep -aq "$1" "$LOG"; then fail "$2"; fi; }

[ -n "$LOG" ] && [ -s "$LOG" ] || { echo "FAIL($VERSION): the game wrote no log (gamecrate exit $RUN_STATUS)"; exit 1; }

require "\[Concord.RimWorld\] Concord runtime wired."  "the Concord adapter never wired - the staged runtime did not load"

if grep -aq "ReflectionTypeLoadException getting types in assembly Cosmere" "$LOG"; then
  MISSING=$(grep -aoE "expected class '[^']*'" "$LOG" | sort -u | tr '\n' ' ')
  fail "this Concord cannot host this Cosmere build - unresolved: ${MISSING:-unknown}"
fi

require "Cosmere DLL compiled:"                        "Cosmere's mod ctor never ran - the mod did not load"
require "\[Quickstarts/Quickstarter\] Loaded .* with [0-9]* colonists" "the quickstart never generated a colony"

forbid  "CONC0[0-9][0-9]"                              "a Concord runtime diagnostic fired"
forbid  "CONC1[0-9][0-9]"                              "a Concord runtime diagnostic fired"

forbid  "at Concord"                                   "an exception stack ran through Concord"
forbid  "TypeLoadException"                            "a type failed to load"
forbid  "MissingMethodException"                       "a method reference did not bind"
forbid  "MissingFieldException"                        "a field reference did not bind"

echo "PASS($VERSION): Concord wired, Cosmere applied its patches, and its quickstart generated a live colony"
