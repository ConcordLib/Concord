#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/gamecrate-harness.sh"

VERSION="${1:-1.6}"
case "$VERSION" in -h|--help) echo "usage: $0 [1.1|1.2|1.3|1.4|1.5|1.6|1.7]"; exit 2 ;; esac

concord_build "$VERSION"
probe_build "$SCRIPT_DIR/AttachedFieldMod/Source/AttachedField.csproj" "$VERSION"

RUN_STATUS=0
scenario_run "$VERSION" attach "[ATTACH] done" 420 "path:$SCRIPT_DIR/AttachedFieldMod" || RUN_STATUS=$?
LOG="$(scenario_log)"

fail() {
  echo "FAIL($VERSION): $1"
  echo "--- log ($LOG) ---"
  grep -aiE "\[ATTACH\]|CONC[0-9]{3}|Concord|Exception|error" "$LOG" 2>/dev/null | tail -40
  exit 1
}
require() { grep -aq "$1" "$LOG" || fail "$2"; }
forbid()  { if grep -aq "$1" "$LOG"; then fail "$2"; fi; }

[ -s "$LOG" ] || { echo "FAIL($VERSION): the game wrote no log at $LOG (gamecrate exit $RUN_STATUS)"; exit 1; }

require "\[ATTACH\] applied"        "Patcher.Apply never reported - the probe assembly did not load"
require "\[ATTACH\] done"           "the probe never finished"
forbid  "\[ATTACH\] FAILED"         "the probe reported a failure"

require "\[ATTACH\] lowered first=3 second=1" "attached field is not per-instance (expected first=3 second=1)"
require "\[ATTACH\] saved label-present=True" "the attached field never reached the saved xml under concord.AttachedFieldProbe.pokes"
require "\[ATTACH\] roundtrip after-load=4"   "the attached field did not survive the save round-trip"

forbid  "CONC0[0-9][0-9]" "a Concord runtime diagnostic fired"
forbid  "CONC1[0-9][0-9]" "a Concord runtime diagnostic fired"

echo "PASS($VERSION): [Attached] fields lower correctly and survive a save round-trip in-game on RimWorld $VERSION"
