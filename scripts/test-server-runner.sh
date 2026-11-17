#!/usr/bin/env bash
# Root helper for the test server only: lets the unprivileged agent user install and delete a throwaway Runner.
# Install as root:  install -m 0755 -o root -g root scripts/test-server-runner.sh /opt/automaton-server/test-server-runner.sh
# sudoers (one line per verb, no wildcards beyond the id):
#   agent ALL=(root) NOPASSWD: /usr/bin/bash /opt/automaton-server/test-server-runner.sh prepare sh_*
#   agent ALL=(root) NOPASSWD: /usr/bin/bash /opt/automaton-server/test-server-runner.sh remove sh_*
# The id is validated here, so a sudoers wildcard cannot reach any other path.
set -euo pipefail

root=/opt/automaton/self-hosted-runners
verb=${1:-}
id=${2:-}
owner=${SUDO_USER:-}

[[ $# -eq 2 ]] || { echo "usage: $0 prepare|remove sh_<32 hex>" >&2; exit 2; }
[[ "$id" =~ ^sh_[0-9a-f]{32}$ ]] || { echo "invalid installation id" >&2; exit 2; }
[[ -n "$owner" && "$owner" != root ]] || { echo "run through sudo as the agent user" >&2; exit 2; }

case "$verb" in
  prepare)
    # The install command from Studio/CLI then runs as the agent user (docker group) and writes inside this folder.
    install -d -m 0755 -o root -g root "$root"
    install -d -m 0755 -o "$owner" -g "$(id -gn "$owner")" "$root/$id"
    ;;
  remove)
    # The Runner container chowns its config to another uid, so only root can delete the folder.
    [[ -d "$root/$id" && ! -L "$root/$id" ]] || { echo "no such installation folder" >&2; exit 1; }
    rm -rf --one-file-system -- "${root:?}/$id"
    ;;
  *)
    echo "unknown verb: $verb" >&2; exit 2 ;;
esac
