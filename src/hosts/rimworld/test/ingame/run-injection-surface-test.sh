#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/gamecrate-harness.sh"

VERSION="${1:-1.6}"
case "$VERSION" in -h|--help) echo "usage: $0 [1.1|1.2|1.3|1.4|1.5|1.6|1.7]"; exit 2 ;; esac

concord_build "$VERSION"
probe_build "$SCRIPT_DIR/InjectionSurfaceMod/Source/InjectionSurface.csproj" "$VERSION"

RUN_STATUS=0
scenario_run "$VERSION" surf "[SURF] done" 420 "path:$SCRIPT_DIR/InjectionSurfaceMod" || RUN_STATUS=$?
LOG="$(scenario_log)"

fail() {
  echo "FAIL($VERSION): $1"
  echo "--- log ($LOG) ---"
  grep -aiE "\[SURF\]|CONC[0-9]{3}|Concord|Exception|error" "$LOG" 2>/dev/null | tail -40
  exit 1
}
require() { grep -aq "$1" "$LOG" || fail "$2"; }
forbid()  { if grep -aq "$1" "$LOG"; then fail "$2"; fi; }

[ -s "$LOG" ] || { echo "FAIL($VERSION): the game wrote no log at $LOG (gamecrate exit $RUN_STATUS)"; exit 1; }

require "\[SURF\] applied"                                      "Patcher.Apply never reported - the probe assembly did not load"
require "\[SURF\] done reported=7"                              "not every probe reported"
forbid  "\[SURF\] FAILED"                                       "a probe reported a failure"

require "\[SURF\] state-head wrote="                            "state slot head injection never ran"
require "\[SURF\] state-tail read="                             "state slot tail injection never ran"
WROTE=$(sed -n 's/.*\[SURF\] state-head wrote=\([0-9.]*\).*/\1/p' "$LOG" | head -1)
READ=$(sed -n 's/.*\[SURF\] state-tail read=\([0-9.]*\).*/\1/p' "$LOG" | head -1)
[ -n "$WROTE" ] || fail "could not parse the written state value"
[ "$WROTE" = "$READ" ] || fail "state slot did not round-trip: wrote=$WROTE read=$READ"

require "\[SURF\] capture width=170 "                           "unsliced capture bound the wrong call site (expected rect3, width 170)"
require "\[SURF\] slice width=145 count=8"                      "slice did not rebase by:1 to the webRect listing (expected width 145, count 8)"
case "$VERSION" in
  1.6|1.7) WEBLINK3="https://www.reddit.com/r/RimWorld/" ;;
  *)       WEBLINK3="https://ludeon.com/forums" ;;
esac
require "\[SURF\] injectnew url=$WEBLINK3"                      "InjectNew capture bound the wrong construction (expected the Subreddit url)"
require "\[SURF\] argument original=[^ ]"                       "At.Argument injection never observed the original label"
require "\[SURF\] argument-effect label0=Concord|"              "the rewritten ctor argument never reached the constructed object"

forbid  "CONC0[0-9][0-9]"                                       "a Concord runtime diagnostic fired"
forbid  "CONC1[0-9][0-9]"                                       "a Concord runtime diagnostic fired"

echo "PASS($VERSION): state slot, [Capture], [InjectNew] and [Slice] all hold in-game on RimWorld $VERSION"
