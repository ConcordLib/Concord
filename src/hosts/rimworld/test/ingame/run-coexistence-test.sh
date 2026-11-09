#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/gamecrate-harness.sh"

usage() {
  echo "usage: $0 [1.1|1.2|1.3|1.4|1.5|1.6|1.7] [--contested|--uncontested|--harmony-absent|--late-harmony|--second-harmony|--second-harmony-inner] [--prepatcher]"
  exit 2
}

VERSION="${1:-1.6}"
MODE="${2:---contested}"
PREPATCHER="${3:-}"
case "$VERSION" in -h|--help) usage ;; esac
case "$MODE" in --contested|--uncontested|--harmony-absent|--late-harmony|--second-harmony|--second-harmony-inner) ;; *) usage ;; esac
case "$PREPATCHER" in ""|--prepatcher) ;; *) usage ;; esac

NEEDS_HARMONY=1
case "$MODE" in
  --contested)            PROBES=(ConcordHalfMod HarmonyHalfMod) ;;
  --uncontested)          PROBES=(ConcordHalfMod) ;;
  --harmony-absent)       PROBES=(ConcordHalfMod); NEEDS_HARMONY=0 ;;
  --late-harmony)         PROBES=(ConcordHalfMod HarmonyLateMod) ;;
  --second-harmony)       PROBES=(ConcordHalfMod HarmonyHalfMod HarmonySecondMod) ;;
  --second-harmony-inner) PROBES=(ConcordHalfMod HarmonyHalfMod HarmonyBreakerMod) ;;
esac

[ -n "$PREPATCHER" ] && [ "$NEEDS_HARMONY" = 0 ] && usage

if [ "$NEEDS_HARMONY" = 1 ] && [ "$VERSION" = "1.1" ]; then
  echo "UNRUNNABLE(1.1): $MODE needs Harmony loaded, and there is no Harmony to load on 1.1."
  echo "  The workshop Harmony (2009463077) declares 1.2 through 1.7 and its LoadFolders.xml has no"
  echo "  v1.1 entry, so on 1.1 it contributes no assembly at all. This scenario would pass with"
  echo "  nothing to coexist with, which proves nothing. Only --harmony-absent runs on 1.1."
  exit 2
fi

if [ -n "$PREPATCHER" ]; then
  case "$VERSION" in
    1.4|1.5|1.6) ;;
    *)
      echo "UNRUNNABLE($VERSION): --prepatcher needs zetrith.prepatcher (workshop 2934420800), and its"
      echo "  LoadFolders.xml declares 1.4, 1.5 and 1.6 only. There is no Prepatcher to load on $VERSION."
      exit 2
      ;;
  esac
fi

concord_build "$VERSION"
for probe in "${PROBES[@]}"; do
  probe_build "$SCRIPT_DIR/$probe/Source/${probe%Mod}.csproj" "$VERSION"
done

ONLY=()
[ "$NEEDS_HARMONY" = 1 ] && ONLY+=(workshop:2009463077)
[ -n "$PREPATCHER" ] && ONLY+=(workshop:2934420800)
for probe in "${PROBES[@]}"; do ONLY+=("path:$SCRIPT_DIR/$probe"); done

case "$MODE" in
  --late-harmony)         MARKER="[COEX] late-invoke result=" ;;
  --second-harmony)       MARKER="[COEX] second-invoke head-count=" ;;
  --second-harmony-inner) MARKER="[COEX] breaker-invoke result=" ;;
  *)                      MARKER="[COEX] invoke-result=" ;;
esac

SCENARIO="coex-${MODE#--}"
[ -n "$PREPATCHER" ] && SCENARIO="$SCENARIO-prepatcher"

RUN_STATUS=0
scenario_run "$VERSION" "$SCENARIO" "$MARKER" 420 "${ONLY[@]}" || RUN_STATUS=$?
LOG="$(scenario_log)"

fail() {
  echo "FAIL($VERSION $MODE): $1"
  echo "--- log tail ($LOG) ---"
  grep -aiE "\[COEX\]|\[Concord.Coex\]|Concord runtime wired|Exception|error" "$LOG" 2>/dev/null | tail -30
  exit 1
}
require() { grep -aq "$1" "$LOG" || fail "$2"; }
forbid()  { if grep -aq "$1" "$LOG"; then fail "$2"; fi; }
composed_target() {
  if grep -aq "backend: body replacement" "$LOG"; then
    return 0
  else
    grep -aq "\[Concord.Coex\] host-owned $1" "$LOG"
  fi
}
require_bridged() {
  composed_target "$1" \
    || fail "target never reached the bridge (body replacement coexists inherently, no host-owned otherwise)"
}

[ -s "$LOG" ] || { echo "FAIL($VERSION $MODE): the game wrote no log at $LOG (gamecrate exit $RUN_STATUS)"; exit 1; }

CONTENDS=0
case "$MODE" in --contested|--late-harmony|--second-harmony|--second-harmony-inner) CONTENDS=1 ;; esac

if [ "$CONTENDS" = 1 ] && ! composed_target "SharedTarget.Compute"; then
  echo "FAIL($VERSION $MODE): concord did not compose with harmony on this target."
  echo "  the log's own reason lines, rather than a cause guessed here:"
  grep -aoE "\[Concord.Coex\] (hook-unavailable|copier-hook-unavailable|arrival-failed|stream-rejected)[^\"]{0,140}" "$LOG" | sort -u | sed 's/^/    /' || echo "    none. concord logged no refusal at all, which is its own finding."
  echo "--- log tail ($LOG) ---"
  grep -aiE "\[COEX\]|\[Concord.Coex\]" "$LOG" 2>/dev/null | tail -20
  exit 1
fi

require "\[COEX\] invoke-result=42 head-count=1" "wrong composed result or head count"
require "\[COEX\] concord-head" "concord head missing"
forbid  "\[Concord.Coex\] hook-unavailable" "a live harmony copy was refused whole, so nothing made through that copy is seen"
forbid  "\[Concord.Coex\] copier-hook-unavailable" "harmony built its wrapper over the declared body instead of concord's"
forbid  "\[Concord.Coex\] arrival-failed" "the bridge never armed on a loaded harmony"
forbid  "\[Concord.Coex\] host-owned notifier failed" "concord could not park its own detour under harmony's jump"
forbid  "\[Concord\] declaration error" "a concord patch failed to compose, read the CONC line above it"
[ "$NEEDS_HARMONY" = 1 ] && require "\[Concord.Coex\] hook-installed" "the notifier never armed any copy, so a late harmony patch drops concord's injections unseen"

if [ -n "$PREPATCHER" ]; then
  forbid "ReflectionOnly api" "prepatcher's reload left a reflection-only Harmony copy behind"
  forbid "Error while instantiating a mod of type Prepatcher" "prepatcher itself failed to load"
  PASS_COUNT=$(grep -ac "\[Concord.RimWorld\] Concord v" "$LOG")
  [ "$PASS_COUNT" = 2 ] || fail "expected concord to log once per pass (saw $PASS_COUNT, want 2)"
  SECOND_PASS_LINE=$(grep -an "\[Concord.RimWorld\] Concord v" "$LOG" | sed -n 2p | cut -d: -f1)
  tail -n "+$SECOND_PASS_LINE" "$LOG" | grep -aq "\[Concord.MonoMod\] backend:" \
    || fail "no backend line after the second pass's Concord v line"
fi

case "$MODE" in
  --contested)
    require "\[COEX\] harmony-postfix" "harmony postfix missing"
    require_bridged "SharedTarget.Compute"
    ;;
  --late-harmony)
    require "\[COEX\] harmony-late-patched" "the late harmony patch never applied"
    require "\[COEX\] late-invoke result=42 head-count=2" "wrong composed result or head count after the late harmony patch"
    require "\[COEX\] harmony-postfix" "harmony postfix missing"
    require_bridged "SharedTarget.Compute"
    ;;
  --uncontested)
    if ! grep -aq "backend: body replacement" "$LOG"; then
      require "\[Concord.Coex\] copier-hook-installed" "the bridge should hook Harmony's copier when Harmony is present"
    fi
    forbid  "\[Concord.Coex\] host-owned SharedTarget.Compute" "uncontested target must stay raw"
    ;;
  --harmony-absent)
    forbid  "\[Concord.Coex\] copier-hook-installed" "the bridge must never hook a copier without Harmony"
    forbid  "\[Concord.Coex\] host-owned" "nothing can route without Harmony"
    ;;
  --second-harmony)
    require "\[COEX\] harmony-postfix" "harmony postfix missing"
    require_bridged "SharedTarget.Compute"
    require "\[COEX\] harmony-second-patched" "second harmony patch never applied"
    require "\[COEX\] harmony-second-postfix" "second harmony postfix never ran"
    require "\[COEX\] second-invoke head-count=2" "concord head did not survive the second harmony rebuild"
    ;;
  --second-harmony-inner)
    require "\[COEX\] harmony-postfix" "harmony postfix missing"
    require_bridged "SharedTarget.Compute"
    require "\[COEX\] harmony-breaker-patched" "breaker transpiler never applied"
    require "\[COEX\] breaker-invoke result=42" "method must still return the correct value"
    if grep -aq "backend: body replacement" "$LOG"; then
      require "injection-evicted" "concord owns this body, so it had to evict the breaker's illegal transpiler"
      require "github.com/ConcordLib/Core/issues" "the eviction report did not ask for a bug report"
      require "\[COEX\] breaker-invoke result=42 head-count=2" "concord dropped its own head instead of only the breaker's patch"
    else
      forbid  "CONC175" "concord reported the incoming stream as illegal; the mono backends compose it"
      forbid  "\[Concord.Coex\] stream-rejected" "concord stood down on a stream it should have composed"
      require "\[COEX\] breaker-invoke result=42 head-count=2" "concord's own head must still run alongside the breaker's patch"
    fi
    ;;
esac

echo "PASS($VERSION $MODE): coexistence holds in-game on RimWorld $VERSION (log: $LOG)"
