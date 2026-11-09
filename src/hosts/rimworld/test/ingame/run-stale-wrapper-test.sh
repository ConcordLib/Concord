#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/gamecrate-harness.sh"

VERSION="${1:-1.7}"
case "$VERSION" in -h|--help) echo "usage: $0 [1.7]"; exit 2 ;; esac
if [ "$VERSION" != "1.7" ]; then
  echo "UNRUNNABLE($VERSION): field injection is a Waylith runtime feature, and only rimworld 1.7 runs Waylith."
  exit 2
fi

concord_build "$VERSION"
probe_build "$SCRIPT_DIR/StaleWrapperMod/Source/StaleWrapper.csproj" "$VERSION"

RUN_STATUS=0
scenario_run "$VERSION" stale "[STALE] done" 420 "path:$SCRIPT_DIR/StaleWrapperMod" || RUN_STATUS=$?
LOG="$(scenario_log)"

fail() {
  echo "FAIL($VERSION): $1"
  echo "--- log ($LOG) ---"
  grep -aiE "\[STALE\]|\[Concord.Waylith\]|CONC[0-9]{3}|InvalidProgramException|Field injection|Exception" "$LOG" 2>/dev/null | tail -40
  exit 1
}
require()    { grep -aq "$1" "$LOG" || fail "$2"; }
require_re() { grep -aqE "$1" "$LOG" || fail "$2"; }
forbid()     { if grep -aq "$1" "$LOG"; then fail "$2"; fi; }
forbid_re()  { if grep -aqE "$1" "$LOG"; then fail "$2"; fi; }

[ -s "$LOG" ] || { echo "FAIL($VERSION): the game wrote no log at $LOG (gamecrate exit $RUN_STATUS)"; exit 1; }

require "\[STALE\] applied" "Patcher.Apply never reported - the probe assembly did not load"
require "\[STALE\] done"    "the probe never finished"
forbid  "\[STALE\] FAILED"  "the probe reported a failure"

require_re "backend-fallback 'Verse.IngredientCount.GetBaseCount'.*CONC157" \
  "concord did not fall back to a DynamicMethod wrapper on GetBaseCount, so nothing in this run was ever a stale wrapper"
forbid  "no stale-method handler" "concord never registered its stale-method handler"
require "\[STALE\] injection-supported=True" "the runtime reports field injection switched off"
require "\[STALE\] before=counted=1 value=1 thrown=none" "the patch was not running before the injection"
require "\[STALE\] accessor loaded StaleWrapperFields.Marker rewritten=False" "the accessor was already bound when it loaded, so the field went in before the wrapper existed"
require "\[STALE\] runtime Message Field injection: applied 1 batches" "the runtime never applied the injected field"
require_re "\[STALE\] reported .*GetBaseCount" "the runtime never reported concord's wrapper as stale, so the handler was never asked"
require "\[STALE\] after=counted=1 value=1 thrown=none" "the patch did not survive the injection (wanted exactly one count on that call and no throw)"
require "\[STALE\] offered StaleWrapperFields.Marker" \
  "the chain never reached the probe's trailing handler, so concord's false on the host-rewritten accessor did not pass it on"
forbid_re "\[STALE\] offered .*GetBaseCount" "concord answered false for its own wrapper and the runtime passed it down the chain"
forbid  "dynamic methods compiled against the old layout now throw" "the runtime reports a stale dynamic method nobody regenerated"
forbid  "InvalidProgramException" "a method compiled against the old layout was called"
forbid  "stale-failed" "concord's handler threw"
forbid  "stale-no-recompose" "the installed backend cannot recompose"

echo "PASS($VERSION): concord's DynamicMethod wrapper on IngredientCount.GetBaseCount kept running after a mod injected a field onto IngredientCount (log: $LOG)"
