#!/usr/bin/env bash

set -euo pipefail

CONCORD_INGAME_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONCORD_REPO_ROOT="$(cd "$CONCORD_INGAME_DIR/../.." && pwd)"
CONCORD_SUPPORTED_VERSIONS=(1.1 1.2 1.3 1.4 1.5 1.6 1.7)
CONCORD_DLC_FIRST_VERSION=(royalty:1.1 ideology:1.3 biotech:1.4 anomaly:1.5 odyssey:1.6)
CONCORD_SCENARIO_LOG=""
CONCORD_DLC_DROPS=()

concord_dlc_drops() {
  local version="$1" entry
  CONCORD_DLC_DROPS=()
  for entry in "${CONCORD_DLC_FIRST_VERSION[@]}"; do
    if [ "${entry##*:1.}" -gt "${version#1.}" ]; then
      CONCORD_DLC_DROPS+=(--without "ludeon.rimworld.${entry%%:*}")
    fi
  done
}

concord_steam_branch() {
  case "$1" in
    1.1) echo "version-1.1" ;;
    1.2) echo "version-1.2" ;;
    1.3) echo "version-1.3-latest" ;;
    1.4) echo "version-1.4.3901" ;;
    1.5) echo "version-1.5" ;;
    1.6) echo "public" ;;
    1.7) echo "unstable" ;;
  esac
}

concord_game_image() {
  if [ -n "${CONCORD_GAME_IMAGE:-}" ]; then echo "$CONCORD_GAME_IMAGE"; return 0; fi
  concord_check_version "$1"
  local found
  found=$(docker images --format '{{.Repository}}:{{.Tag}}' \
    | grep -E "^gamecrate/rimworld-game:${1//./\\.}(\$|-)" | sort | head -1)
  if [ -z "$found" ]; then
    echo "no gamecrate image for rimworld $1. build it with:" >&2
    echo "  gamecrate steam build --game rimworld --beta $(concord_steam_branch "$1") --variant linux" >&2
    return 2
  fi
  echo "$found"
}

concord_check_version() {
  local v
  for v in "${CONCORD_SUPPORTED_VERSIONS[@]}"; do
    [ "$v" = "$1" ] && return 0
  done
  echo "unsupported version $1 (supported: ${CONCORD_SUPPORTED_VERSIONS[*]})" >&2
  return 2
}

concord_build() {
  concord_check_version "$1"
  dotnet build "$CONCORD_REPO_ROOT/Source/ConcordRimWorld" -c Release -p:GameVersion="$1" --verbosity quiet
  local staged="$CONCORD_REPO_ROOT/$1/Assemblies/0Concord.dll"
  test -f "$staged" || { echo "FAIL: no $staged after the $1 build"; return 1; }
}

probe_build() {
  concord_check_version "$2"
  dotnet build "$1" -c Release -p:GameVersion="$2" -p:TreatWarningsAsErrors=true --verbosity quiet
}

scenario_run() {
  local version="$1" scenario="$2" marker="$3" timeout="$4"
  shift 4
  concord_check_version "$version"
  local image only=() mod
  image="$(concord_game_image "$version")"
  for mod in "$@"; do only+=(--only "$mod"); done
  concord_dlc_drops "$version"

  local instance="$scenario-$version"
  local plan logs_dir before newest status=0
  cd "$CONCORD_REPO_ROOT"
  plan=$(gamecrate run concord --print-plan --json --instance "$instance" --image "$image" --no-build "${only[@]}" "${CONCORD_DLC_DROPS[@]}")
  logs_dir=$(jq -r .logsDirHost <<<"$plan")
  before=$(find "$logs_dir/runs" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -1)

  gamecrate run concord \
    --instance "$instance" \
    --image "$image" \
    --mode headless \
    --no-build \
    --plain \
    --marker "$marker" \
    --timeout "$timeout" \
    --use "concordlib.concord=$CONCORD_REPO_ROOT" \
    "${only[@]}" "${CONCORD_DLC_DROPS[@]}" || status=$?

  newest=$(find "$logs_dir/runs" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -1)
  CONCORD_SCENARIO_LOG=""
  if [ -n "$newest" ] && [ "$newest" != "$before" ]; then
    local candidate
    for candidate in "$newest/Player.log" "$newest/stdout.log"; do
      if [ -s "$candidate" ]; then CONCORD_SCENARIO_LOG="$candidate"; break; fi
    done
  fi
  return "$status"
}

scenario_log() { echo "$CONCORD_SCENARIO_LOG"; }
