#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'TEXT'
run-rimworld-probe.sh [--keep-logs|--legacy|--help]

Boots RimWorld 1.7 once in a gamecrate container with core + the local Concord checkout + one
probe mod. The adapter installs body replacement or refuses, and there is no other backend to
compare against.

The backend arrives the way a player's install delivers it. The Concord mod is copied to a temp
directory and the built adapter goes in its Current/Backend/, which the engine does not scan and
DetourBackendLoader does. ConcordMod's constructor finds it, loads it and installs it before Concord
applies its own first patch. The probe installs nothing; it reads what the loader already chose.

Current/Backend/ is gitignored in that checkout, so it may already hold a backend from an earlier
staging build. The copy step never carries it across; this script is the only thing that puts a file
there, and it prints what it put.

Checks, most important first:
  0. setup: the staged Concord runtime and the Concord the adapter compiled against carry one git
     sha. A mismatched pair means nothing below would be measuring one build.
  1. Concord loads, the backend selects body replacement, and a patch applies to a mod method and
     to a real Assembly-CSharp method
  2. every patch still holds once the game has finished loading
  3. a caller already JIT-compiled before the write runs the new body
  4. the frame running a published body on a real Assembly-CSharp method attributes back to
     Concord's injection
  5. the registry reports the claims and bodies it published
  6. the heap survives a forced compacting gen2 GC taken with a published body live

Exit: 0 all checks passed, 1 a check failed, 2 a check was inconclusive, 3 setup failed,
      4 a fixture run, which is never a measurement.

Flags:
  --keep-logs   leave the collected game log in /tmp
  --legacy      add a second run with a 1.6-era mod built against an older Concord.Ref, to see
                whether it still loads. Skipped by default because the mod is not part of this
                repo and its 1.7 incompatibilities are not Concord's. The check refuses to read
                an empty error count as a pass unless it first sees that mod's assembly in the
                AppDomain, and unless the same field reads false in a run without it.

Env:
  WAYLITH_RIMPROBE_TIMEOUT=420    seconds to wait for each run's report
  CONCORD_RIMWORLD_INSTALL=<dir>  the RimWorld 1.7 install to build against
  CONCORD_RIMWORLD_MOD=<dir>      the Concord RimWorld checkout to launch
  CONCORD_LEGACY_MOD=<dir>        the mod --legacy loads
  CONCORD_RIMPROBE_BACKEND=stage  what to put in the Concord mod's Current/Backend. The two
                                  non-default values are the negative controls for the loader:
                                    stage     the built adapter, correctly paired (default)
                                    none      nothing. The staged copy of the mod never includes a
                                              Current/Backend, whatever is in the checkout, and the
                                              script refuses to launch if one is there anyway.
                                              DetourBackendLoader must find no backend, say nothing,
                                              and leave the game booting on Concord's own detour
                                              backend. Every adapter reading then reads
                                              adapter-absent, so the run FAILS by design.
                                    mismatch  the built adapter with one hex digit of its
                                              ConcordBuild stamp flipped. The loader's pairing
                                              check must refuse it. Also FAILS by design.
  CONCORD_RIMPROBE_FIXTURE=<log>  read this log instead of launching anything, so a check can be
                                  shown failing and then passing without a game. A fixture run
                                  always exits 4 and never prints RESULT: PASS, whatever the
                                  per-check lines say.
TEXT
}

KEEP_LOGS=0
LEGACY=0
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --keep-logs) KEEP_LOGS=1 ;;
    --legacy) LEGACY=1 ;;
    *) echo "unknown argument '$arg'. --help lists them." >&2; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WORKSPACE="$(cd "$REPO/../.." && pwd)"
CORE_ROOT="$REPO"
RIM_MOD="${CONCORD_RIMWORLD_MOD:-$WORKSPACE/Concord/RimWorld}"
GAME_DIR="${CONCORD_RIMWORLD_INSTALL:-/mnt/games/SteamLibrary/steamapps/common/RimWorld}"
LEGACY_MOD="${CONCORD_LEGACY_MOD:-/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3706851018}"

PROBE_SRC="$SCRIPT_DIR/rimworld"
PROFILE=v17
INSTANCE=rprobe
CONTAINER="gamecrate-rimworld-$PROFILE-$INSTANCE"
RUNS="$HOME/.local/share/gamecrate/rimworld/$PROFILE/instances/$INSTANCE/logs/runs"
LOG=/tmp/concord-waylith-rimworld.log
LEGACY_LOG=/tmp/concord-waylith-rimworld-legacy.log
TIMEOUT="${WAYLITH_RIMPROBE_TIMEOUT:-420}"
STAGE="$(mktemp -d /tmp/concord-waylith-rimprobe.XXXX)"
BACKEND_MODE="${CONCORD_RIMPROBE_BACKEND:-stage}"
FIXTURE_LOG="${CONCORD_RIMPROBE_FIXTURE:-}"
FIXTURE=0
if [ -n "$FIXTURE_LOG" ]; then FIXTURE=1; fi

setup_fail() { echo "SETUP-FAIL: $1" >&2; exit 3; }

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  (cd /tmp && gamecrate stop rimworld "$PROFILE" --instance "$INSTANCE" >/dev/null 2>&1) || true
  rm -rf "$STAGE"
}
trap cleanup EXIT

case "$BACKEND_MODE" in
  stage|none|mismatch) ;;
  *) setup_fail "CONCORD_RIMPROBE_BACKEND='$BACKEND_MODE' is not stage, none or mismatch" ;;
esac

if [ "$FIXTURE" = 1 ]; then
  test -f "$FIXTURE_LOG" || setup_fail "no fixture log at $FIXTURE_LOG"
  LOG="$FIXTURE_LOG"
  LEGACY=0
  echo "== FIXTURE RUN. No game launched, no backend staged, nothing measured. The checks below are"
  echo "== reading $LOG, which a person wrote. This exits 4 and cannot print RESULT: PASS."
else
command -v docker >/dev/null || setup_fail "docker is not on PATH"
command -v gamecrate >/dev/null || setup_fail "gamecrate is not on PATH"
test -f "$GAME_DIR/Assembly-CSharp.dll" || setup_fail "no Assembly-CSharp.dll under $GAME_DIR"
test -f "$RIM_MOD/About/About.xml" || setup_fail "no Concord RimWorld mod at $RIM_MOD"

STAGED_RUNTIME="$RIM_MOD/Current/Assemblies/0Concord.dll"
ADAPTER_CONCORD="$CORE_ROOT/Assemblies/net10.0/Concord.dll"
test -f "$STAGED_RUNTIME" || setup_fail "$STAGED_RUNTIME missing. Build the Concord RimWorld mod first."
test -f "$ADAPTER_CONCORD" || setup_fail "$ADAPTER_CONCORD missing. Build Concord Core first."

sha_of() {
  strings -a "$1" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+[-A-Za-z0-9.]*\+[0-9a-f]{40}' | head -1 | cut -d+ -f2 || true
}

RUNTIME_SHA="$(sha_of "$STAGED_RUNTIME")"
ADAPTER_SHA="$(sha_of "$ADAPTER_CONCORD")"
echo "== sha runtime=${RUNTIME_SHA:0:12} adapter-compiled-against=${ADAPTER_SHA:0:12}"
[ -n "$RUNTIME_SHA" ] || setup_fail "no build sha in $STAGED_RUNTIME, so nothing can be paired"
[ "$RUNTIME_SHA" = "$ADAPTER_SHA" ] \
  || setup_fail "the adapter compiles against Concord $ADAPTER_SHA but the game will load $RUNTIME_SHA. Rebuild both from one Core checkout."

echo "== game $(cat "$GAME_DIR/Version.txt" 2>/dev/null || echo unknown)"

dotnet build "$REPO/Concord.slnx" -c Release --verbosity quiet \
  || setup_fail "the adapter did not build"
ADAPTER="$REPO/src/backends/Concord.Waylith/bin/Release/net10.0/Concord.Waylith.dll"
test -f "$ADAPTER" || setup_fail "$ADAPTER missing after the adapter build"

dotnet build "$PROBE_SRC/Source/ConcordWaylithRimProbe.csproj" -c Release --verbosity quiet \
  -p:GameDir="$GAME_DIR" -p:ConcordDir="$CORE_ROOT/Assemblies/net10.0" \
  || setup_fail "the probe mod did not build"
PROBE_DLL="$PROBE_SRC/Source/bin/probe/ConcordWaylithRimProbe.dll"
test -f "$PROBE_DLL" || setup_fail "$PROBE_DLL is missing after the probe build"

mkdir -p "$STAGE/probe/About" "$STAGE/probe/Assemblies"
cp "$PROBE_SRC/About/About.xml" "$STAGE/probe/About/"
cp "$PROBE_DLL" "$STAGE/probe/Assemblies/"
PROBE_STAGE="$STAGE/probe"

CONCORD_STAGE="$STAGE/concord"
mkdir -p "$CONCORD_STAGE/Current"
cp -a "$RIM_MOD/About" "$CONCORD_STAGE/"
find "$RIM_MOD/Current" -mindepth 1 -maxdepth 1 ! -name Backend -exec cp -a {} "$CONCORD_STAGE/Current/" \;
cp -a "$RIM_MOD/LoadFolders.xml" "$RIM_MOD/.gamecrate.yml" "$CONCORD_STAGE/"
BACKEND_DEST="$CONCORD_STAGE/Current/Backend/Concord.Host.Adapter.dll"

case "$BACKEND_MODE" in
  stage)
    mkdir -p "$(dirname "$BACKEND_DEST")"
    cp "$ADAPTER" "$BACKEND_DEST"
    echo "== backend staged at Current/Backend/, the only path DetourBackendLoader probes"
    ;;
  none)
    echo "== no backend staged, which is every player's ordinary case"
    ;;
  mismatch)
    mkdir -p "$(dirname "$BACKEND_DEST")"
    cp "$ADAPTER" "$BACKEND_DEST"
    WRONG_SHA="${ADAPTER_SHA:0:39}$(if [ "${ADAPTER_SHA: -1}" = 0 ]; then echo 1; else echo 0; fi)"
    python3 - "$BACKEND_DEST" "$ADAPTER_SHA" "$WRONG_SHA" <<'PY' || setup_fail "could not flip the ConcordBuild stamp"
import sys
path, old, new = sys.argv[1], sys.argv[2].encode(), sys.argv[3].encode()
blob = open(path, 'rb').read()
found = blob.count(old)
if found != 1:
    sys.exit("expected exactly one copy of the sha in the adapter, found %d" % found)
open(path, 'wb').write(blob.replace(old, new))
PY
    echo "== backend staged with its ConcordBuild stamp flipped to $WRONG_SHA, runtime is $RUNTIME_SHA"
    ;;
esac

echo "== staged Current/ holds: $(/usr/bin/ls -1 "$CONCORD_STAGE/Current" | tr '\n' ' ')"
if [ -d "$CONCORD_STAGE/Current/Backend" ]; then
  echo "== staged Current/Backend/ holds: $(/usr/bin/ls -1 "$CONCORD_STAGE/Current/Backend" | tr '\n' ' ')"
else
  echo "== staged Current/Backend/ does not exist, so DetourBackendLoader has nothing to find"
fi
if [ "$BACKEND_MODE" = none ] && [ -e "$CONCORD_STAGE/Current/Backend" ]; then
  setup_fail "CONCORD_RIMPROBE_BACKEND=none staged a Current/Backend anyway, so this run is not a negative control and every adapter reading below could be a real backend's"
fi
echo "== probe staged at $PROBE_STAGE, Concord mod staged at $CONCORD_STAGE"

run_probe() {
  local out="$1" extra_mod="$2" rc=0 dir="" before=""
  echo "== run${extra_mod:+ plus $extra_mod}"
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  before="$(/usr/bin/ls -1 "$RUNS" 2>/dev/null | sort | tail -1)"

  local args=(run "$PROFILE" --instance "$INSTANCE"
    --only concordlib.concord --only CryptikLemur.WaylithRimProbe
    --use CryptikLemur.WaylithRimProbe="$PROBE_STAGE"
    --use concordlib.concord="$CONCORD_STAGE"
    --no-build --no-stale-check --replace
    --mode headless --marker '[RPROBE] done' --timeout "$TIMEOUT"
    --docker-arg -e --docker-arg "CONCORD_RIMPROBE_WATCH=${WATCH:-}")
  if [ -n "$extra_mod" ]; then
    args+=(--only "$extra_mod" --use "$extra_mod=$LEGACY_MOD" --only RimWorks.RimLogging)
  fi

  (cd /tmp && gamecrate "${args[@]}") >/dev/null 2>&1 || rc=$?
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true

  dir="$(/usr/bin/ls -1 "$RUNS" 2>/dev/null | sort | tail -1)"
  if [ -z "$dir" ] || [ ! -f "$RUNS/$dir/stdout.log" ]; then
    echo "SETUP-FAIL: gamecrate exited $rc and left no stdout.log under $RUNS. The game never started."
    exit 3
  fi
  if [ "$dir" = "$before" ]; then
    echo "SETUP-FAIL: gamecrate exited $rc and wrote no new run directory. The newest one under $RUNS is still $dir, which this launch did not produce. Reading it would report an older run's numbers as this one's."
    exit 3
  fi

  cp -f "$RUNS/$dir/stdout.log" "$out"
  echo "== run ended, gamecrate exit $rc, log at $out"
}

run_probe "$LOG" ""
fi

if [ "$LEGACY" = 1 ]; then
  test -f "$LEGACY_MOD/About/About.xml" || setup_fail "--legacy was asked for but there is no mod at $LEGACY_MOD"
  LEGACY_ID="$(sed -nE 's:.*<packageId>(.*)</packageId>.*:\1:p' "$LEGACY_MOD/About/About.xml" | head -1)"
  LEGACY_DLL="$(find "$LEGACY_MOD/Assemblies" -maxdepth 1 -name '*.dll' 2>/dev/null | sort | sed -n 1p)"
  [ -n "$LEGACY_DLL" ] || LEGACY_DLL="$(find "$LEGACY_MOD" -name '*.dll' -path '*Assemblies*' | sort | sed -n 1p)"
  LEGACY_ASM="$(basename "${LEGACY_DLL:-}" .dll)"
  [ -n "$LEGACY_ASM" ] || setup_fail "$LEGACY_MOD ships no assembly, so 'does it still load' has nothing to answer"
  echo "== legacy mod $LEGACY_ID ships $LEGACY_ASM.dll"
  WATCH="$LEGACY_ASM" run_probe "$LEGACY_LOG" "$LEGACY_ID"
fi

field() {
  awk -v p="$2" -v k="$3" '
    index($0, "[RPROBE] " p) {
      for (i = 1; i <= NF; i++) {
        n = index($i, "=")
        if (n > 0 && substr($i, 1, n - 1) == k) { print substr($i, n + 1); exit }
      }
    }' "$1"
}

PASSED=0
FAILED=0
INCONCLUSIVE=0
pass() { echo "PASS  $1"; PASSED=$((PASSED + 1)); }
fail() { echo "FAIL  $1"; FAILED=$((FAILED + 1)); }
unsure() { echo "INCON $1"; INCONCLUSIVE=$((INCONCLUSIVE + 1)); }

legacy_binding_errors() {
  grep -aE 'Could not load type|TypeLoadException|MissingMethodException|MissingFieldException|BadImageFormatException' "$1" \
    | grep -av 'backend-fallback' \
    | grep -ai -- "$LEGACY_ASM" || true
}

dump() {
  echo "--- last 40 lines of $1 ---"
  tail -40 "$1" 2>/dev/null || echo "(no log)"
}

if ! grep -q '\[RPROBE\] start' "$LOG" 2>/dev/null; then
  echo "SETUP-FAIL: the run never reached the probe mod. An empty report is not a clean run."
  dump "$LOG"
  exit 3
fi
if ! grep -q '\[RPROBE\] measured' "$LOG" 2>/dev/null; then
  echo "SETUP-FAIL: the run started the probe but never finished measuring, so it died mid-measurement."
  dump "$LOG"
  exit 3
fi
if ! grep -q '\[RPROBE\] done' "$LOG" 2>/dev/null; then
  echo "SETUP-FAIL: the run measured but never reached the deferred report, so the game did not survive mod loading."
  dump "$LOG"
  exit 3
fi

echo
echo "concord   $(grep -m1 '\[RPROBE\] env concord=' "$LOG" | sed 's/.*env //')"
echo "adapter   $(grep -m1 '\[RPROBE\] env adapter ' "$LOG" | sed 's/.*env adapter //')"
echo "backend   before=$(field "$LOG" env backend-before) after=$(field "$LOG" install backend-after)"

BACKEND="$(field "$LOG" install selected)"
PATCHED_BEFORE="$(field "$LOG" install patched-before)"

echo
echo "adapter  $(grep -m1 -a '\[Concord.Waylith\] backend' "$LOG" | sed 's/^.*\[Concord.Waylith\] //' | cut -c1-200)"
echo "install  $PATCHED_BEFORE Concord target(s) were already patched when the probe looked"
echo "install  selected=$BACKEND"
echo "capability $(grep -m1 '\[RPROBE\] install capability=' "$LOG" | sed 's/.*capability=//')"

if [ "$BACKEND" = "body-replacement" ]; then
  pass "the backend selected body replacement, so every result below was measured through it"
else
  fail "the backend selected '$BACKEND', not body replacement, so nothing below tests it"
  grep -E '\[RPROBE\] (install|adapter-log)' "$LOG" | head -12 || true
fi

SOLO_BEFORE="$(field "$LOG" solo before)"
SOLO_AFTER="$(field "$LOG" solo after)"
SOLO_REPLACED="$(field "$LOG" solo replaced)"
GAME_BEFORE="$(field "$LOG" game before)"
GAME_AFTER="$(field "$LOG" game after)"
GAME_REVERTED="$(field "$LOG" game reverted)"
GAME_REPLACED="$(field "$LOG" game replaced)"

echo
echo "mod method   $SOLO_BEFORE -> $SOLO_AFTER (want 11), published-body=$SOLO_REPLACED"
echo "game method  $GAME_BEFORE -> $GAME_AFTER (want 42) -> $GAME_REVERTED after revert (want 2), published-body=$GAME_REPLACED"

if [ "$SOLO_AFTER" = "11" ] && [ "$GAME_AFTER" = "42" ]; then
  pass "a Concord patch changed a mod method and a real Assembly-CSharp method under body replacement"
else
  fail "a Concord patch did not take (mod=$SOLO_AFTER want 11, game=$GAME_AFTER want 42)"
fi

echo "published-body oracle: mod=$SOLO_REPLACED game=$GAME_REPLACED"

if [ "$SOLO_REPLACED" = "true" ] && [ "$GAME_REPLACED" = "true" ]; then
  pass "both targets carry a body the backend published"
else
  fail "a target was not published (mod=$SOLO_REPLACED game=$GAME_REPLACED)"
  grep -aE 'Concord.Waylith|CONC[0-9]{3}' "$LOG" | cut -c1-200 | head -12 || true
fi

if [ "$GAME_REVERTED" = "2" ]; then
  pass "reverting the game-method patch put GenMath.PositiveMod back, so the probe did not leave the game broken"
else
  fail "GenMath.PositiveMod(7,5) reads $GAME_REVERTED after revert, not 2, so a revert under this backend does not restore a game method"
fi

LATE_SOLO="$(field "$LOG" late solo)"
LATE_REPLACED="$(field "$LOG" late solo-replaced)"
LATE_WATCH="$(field "$LOG" late loaded)"

echo
echo "after the rest of boot: solo=$LATE_SOLO (want 11) still-published=$LATE_REPLACED"

if [ "$LATE_SOLO" = "11" ] && [ "$LATE_REPLACED" = "true" ]; then
  pass "every patch still held once the game finished loading defs and reached the deferred queue, with the published body still in place"
else
  fail "a patch stopped holding between the mod ctor and the end of loading (solo=$LATE_SOLO published=$LATE_REPLACED)"
fi

F_VALUE="$(field "$LOG" frame value)"
F_PUB="$(field "$LOG" frame published)"
F_CLAIMED="$(field "$LOG" frame claimed)"
F_RUNS="$(field "$LOG" frame runs)"
F_OWNERS="$(field "$LOG" frame owners)"
F_FRAME_OWNER="$(field "$LOG" frameseen owner)"
F_FRAME_METHOD="$(field "$LOG" frameseen method)"
F_FRAME_OFFSET="$(field "$LOG" frameseen offset)"
F_FRAME_STACK="$(field "$LOG" frameseen stack)"
F_HELD_VALUE="$(field "$LOG" lateframe value)"
F_HELD_PUB="$(field "$LOG" lateframe published)"

echo
echo "published body on a real game method, left in place:"
echo "  GenMath.PositiveMod(9,4)=$F_VALUE (want 1) published=$F_PUB claimed=$F_CLAIMED runs=$F_RUNS owners=$F_OWNERS"
echo "  frame inside the body method=$F_FRAME_METHOD offset=$F_FRAME_OFFSET owner=$F_FRAME_OWNER stack=$F_FRAME_STACK"
echo "  after loading finished value=$F_HELD_VALUE published=$F_HELD_PUB"

if [ -z "$F_VALUE" ] || [ -z "$F_FRAME_OWNER" ]; then
  unsure "the frame step did not report, so a published body on a real game method was not measured at all"
  grep -a '\[RPROBE\] frame' "$LOG" | cut -c1-240 | head -4 || true
elif [ "$F_PUB" != "true" ] || [ "$F_CLAIMED" != "true" ]; then
  fail "the body was not published or not claimed on the real game method (published=$F_PUB claimed=$F_CLAIMED)"
elif [ "$F_FRAME_OWNER" = "<null>" ] || [ "$F_FRAME_METHOD" != "GenMath.PositiveMod" ]; then
  fail "the frame that ran the injection is '$F_FRAME_METHOD' owned by '$F_FRAME_OWNER', not an offset inside GenMath.PositiveMod's published body, so what executed was a wrapper (stack=$F_FRAME_STACK)"
elif [ "${F_RUNS:-0}" -lt 1 ]; then
  fail "the injection never ran on the shared game method (runs=$F_RUNS), so its code is not in the body"
elif [ "$F_VALUE" != "1" ] || [ "$F_HELD_VALUE" != "1" ]; then
  fail "GenMath.PositiveMod(9,4) reads $F_VALUE with the patch live and $F_HELD_VALUE after loading, not 1, so the published body returns the wrong answer to the game"
elif ! printf '%s' "$F_OWNERS" | grep -q 'FramePatch'; then
  fail "the published body's owner list is '$F_OWNERS' and carries no Concord injection owner, so the body in effect is not the one Concord composed"
elif [ "$F_HELD_PUB" != "true" ]; then
  fail "the body was published while the probe watched and gone by the end of loading (published=$F_HELD_PUB)"
else
  pass "a real Assembly-CSharp method stayed PUBLISHED: the injection ran from inside GenMath.PositiveMod's own frame at IL offset $F_FRAME_OFFSET, owners $F_OWNERS, still published after loading finished."
fi

CLAIM_COUNT="$(field "$LOG" registry-counts claims)"
PUBLISHED_COUNT="$(field "$LOG" registry-counts published-bodies)"
CLAIM_TARGETS="$(field "$LOG" registry-claims targets)"

echo
echo "registry: $CLAIM_COUNT target(s) claimed, $PUBLISHED_COUNT body/bodies published"
echo "registry: claimed targets=$CLAIM_TARGETS"

if [ -z "$CLAIM_COUNT" ] || [ -z "$PUBLISHED_COUNT" ]; then
  unsure "the registry step did not report, so what the backend claimed and published is unanswered"
  grep -a '\[RPROBE\] registry' "$LOG" | cut -c1-200 | head -4 || true
elif [ "$PUBLISHED_COUNT" = "0" ]; then
  fail "the registry published no body at all in a run that selected body replacement, so every published-body reading above is about something the registry never recorded"
else
  pass "the registry recorded $CLAIM_COUNT claimed target(s) and $PUBLISHED_COUNT published body/bodies"
fi

HOT_BEFORE="$(field "$LOG" hot through-before)"
HOT_AFTER="$(field "$LOG" hot through-after)"
HOT_LATER="$(field "$LOG" hot through-later)"
HOT_DIRECT="$(field "$LOG" hot direct-after)"
HOT_REVERTED="$(field "$LOG" hot through-reverted)"
HOT_WARMED="$(field "$LOG" hot warmed)"
HOT_INLINING="$(field "$LOG" hot caller-inlining)"

echo
echo "hot caller: $HOT_WARMED calls before the weave, inlining into the caller=$HOT_INLINING"
echo "hot caller: through-before=$HOT_BEFORE through-after=$HOT_AFTER (want 42) through-later=$HOT_LATER direct-after=$HOT_DIRECT through-reverted=$HOT_REVERTED (want 2)"

if [ -z "$HOT_AFTER" ] || [ "$HOT_BEFORE" != "2" ]; then
  unsure "the hot-caller step did not report a compiled-before-the-weave caller (through-before=$HOT_BEFORE), so nothing was asked of one"
elif [ "$HOT_DIRECT" != "42" ]; then
  unsure "the patch itself did not take on this run (direct-after=$HOT_DIRECT, want 42), so what a pre-compiled caller sees is unanswered"
elif [ "$HOT_AFTER" = "42" ]; then
  pass "a caller JIT-compiled $HOT_WARMED calls before the weave picked the new body up on its very next call, with inlining of the target left $HOT_INLINING"
elif [ "$HOT_LATER" = "42" ]; then
  fail "a caller compiled before the weave kept running the old body ($HOT_AFTER) and only picked the new one up after $HOT_WARMED more calls ($HOT_LATER), so a weave is invisible until the caller is recompiled"
else
  fail "a caller compiled before the weave never saw it: through-after=$HOT_AFTER through-later=$HOT_LATER while a fresh direct call reads $HOT_DIRECT. The target is $HOT_INLINING to inline, so the caller is probably running an inlined copy of the original body"
fi

if [ "$HOT_REVERTED" != "2" ] && [ -n "$HOT_REVERTED" ]; then
  fail "the hot caller reads $HOT_REVERTED after the patch was reverted, not 2, so the probe left a hot game method wrong"
fi

HEAP_BAD="$(field "$LOG" heap bad)"
HEAP_BAD_PRE="$(field "$LOG" heap bad-before-gc)"
HEAP_BLOCKS="$(field "$LOG" heap blocks)"
HEAP_GEN2="$(field "$LOG" heap gen2-collections)"
HEAP_LATE="$(field "$LOG" lateheap heap-bad)"
FAULTS="$(grep -anE 'SIGSEGV|SIGBUS|SIGILL|SIGABRT|Segmentation fault|Fatal error\. Internal CLR error|double free|heap corruption|corrupted double-linked list|Assertion failed' "$LOG" 2>/dev/null | head -5 || true)"

echo
echo "heap watch: what is watched is $HEAP_BLOCKS sentinel blocks written before any patch and re-checksummed after a forced blocking compacting gen2 GC taken with a published body live on a hot game method, plus the log for a native fault signature."
echo "heap watch: bad-before-gc=$HEAP_BAD_PRE bad-after-gc=$HEAP_BAD gen2-collections=$HEAP_GEN2 bad-at-end-of-loading=$HEAP_LATE"

if [ -n "$FAULTS" ]; then
  fail "the run died on a native fault, which is what a trip looks like."
  printf '%s\n' "$FAULTS"
elif [ -z "$HEAP_BAD" ]; then
  unsure "no heap sentinel reading (bad='$HEAP_BAD'), so the only thing watched was the fault grep and a quiet log is not an answer"
elif [ "$HEAP_BAD" != "0" ] || [ "$HEAP_BAD_PRE" != "0" ] || [ "$HEAP_LATE" != "0" ]; then
  fail "a sentinel block came back changed (before-gc=$HEAP_BAD_PRE after-gc=$HEAP_BAD end-of-loading=$HEAP_LATE). Nothing in this probe writes to those blocks, so something else did."
else
  pass "$HEAP_BLOCKS sentinel blocks survived a forced compacting gen2 GC with a published body live, and no native fault signature is in the log."
fi

if [ "$LEGACY" = 1 ]; then
  echo
  LEGACY_LOADED="$(field "$LEGACY_LOG" late loaded)"
  LEGACY_ERRORS="$(legacy_binding_errors "$LEGACY_LOG" | grep -ac . || true)"
  echo "legacy mod $LEGACY_ID ($LEGACY_ASM.dll): in-AppDomain=$LEGACY_LOADED, binding errors naming $LEGACY_ASM=$LEGACY_ERRORS"
  echo "scoping: a line has to carry a binding exception AND name $LEGACY_ASM, and must not be one of Concord's own backend-fallback lines, which quote BadImageFormatException while explaining what the runtime would have thrown. That misses a failure whose line names neither the assembly nor a type in it."
  echo "control: the same field reads '$LATE_WATCH' in the main run, which loaded no such mod"

  if ! grep -q '\[RPROBE\] done' "$LEGACY_LOG" 2>/dev/null; then
    unsure "the --legacy run never reached the deferred report, so whether the old mod loads is unanswered"
  elif [ "$LATE_WATCH" != "false" ]; then
    unsure "the watch field reads '$LATE_WATCH' even in a run with no legacy mod, so it cannot tell a loaded mod from an absent one and its answer means nothing"
  elif [ "$LEGACY_LOADED" != "true" ]; then
    fail "$LEGACY_ASM.dll is not in the AppDomain after loading finished, so the 1.6-era mod did NOT load. An empty error count here proves nothing."
    grep -aiE "$LEGACY_ASM" "$LEGACY_LOG" | cut -c1-200 | head -5 || true
  elif [ "${LEGACY_ERRORS:-0}" = 0 ]; then
    pass "the 1.6-era mod built against an older Concord.Ref is loaded in the AppDomain with no binding error"
  else
    unsure "the 1.6-era mod loaded but logged $LEGACY_ERRORS binding error(s) that name $LEGACY_ASM. Read $LEGACY_LOG before blaming Concord: this mod does not declare 1.7 support, so a missing game member is not a Concord result."
    legacy_binding_errors "$LEGACY_LOG" | cut -c1-200 | head -5 || true
  fi
else
  echo
  echo "note  the 1.6-era-mod question was not run. Pass --legacy to run it."
fi

echo
echo "== $PASSED passed, $FAILED failed, $INCONCLUSIVE inconclusive"
if [ "$KEEP_LOGS" = 1 ]; then
  echo "== log kept at $LOG"
fi

if [ "$FIXTURE" = 1 ]; then
  echo "RESULT: FIXTURE, $PASSED passed and $FAILED failed against logs nobody measured. Not a run."
  exit 4
fi
if [ "$FAILED" -gt 0 ]; then
  echo "RESULT: FAIL"
  exit 1
fi
if [ "$INCONCLUSIVE" -gt 0 ]; then
  echo "RESULT: INCONCLUSIVE"
  exit 2
fi
echo "RESULT: PASS"
