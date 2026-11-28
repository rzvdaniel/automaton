#!/usr/bin/env bash
# agent-tools.sh list: JSON of every agent tool adapter in agents/*.sh with its check result and models (for Agent Keeper).
# Only tools with an adapter are listed; there is no discovery of anything else.
set -uo pipefail
dir=$(cd "$(dirname "$0")/agents" && pwd)
[ "${1:-}" = list ] || { echo "usage: agent-tools.sh list" >&2; exit 2; }
esc() { local s=${1//\\/\\\\}; s=${s//\"/\\\"}; printf '%s' "$s"; }
out=; sep=
for f in "$dir"/*.sh; do
  [ -f "$f" ] || continue
  name=$(basename "$f" .sh)
  if timeout 60 bash "$f" check >/dev/null 2>&1; then ok=true; else ok=false; fi
  models=; msep=
  if [ "$ok" = true ]; then
    while IFS=$'\t' read -r id label; do
      [ -n "$id" ] || continue
      models+="$msep{\"id\":\"$(esc "$id")\",\"label\":\"$(esc "${label:-$id}")\"}"; msep=,
    done < <(timeout 60 bash "$f" models 2>/dev/null)
  fi
  out+="$sep{\"tool\":\"$name\",\"signedIn\":$ok,\"models\":[$models]}"; sep=,
done
printf '[%s]\n' "$out"
