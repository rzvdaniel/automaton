#!/usr/bin/env bash
set -euo pipefail

cat >/dev/null
printf '{"hostname":"%s","os":"%s","architecture":"%s"}\n' \
  "$(hostname)" \
  "$(uname -s)" \
  "$(uname -m)"
