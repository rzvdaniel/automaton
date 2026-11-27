#!/usr/bin/env bash
# Offline test of verify-trigger.sh with a fake gh. Run: bash .agents/tools/test-verify-trigger.sh
set -euo pipefail
d=$(dirname "$(readlink -f "$0")"); t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
printf '#!/usr/bin/env bash\necho "$VERIFY_N"\n' > "$t/gh"; chmod +x "$t/gh"
export AGENT_GH=$t/gh VERIFY_VERSION_FILE=$t/version VERIFY_STATE_FILE=$t/state/v VERIFY_N=2
v() { bash "$d/verify-trigger.sh" "$@"; }
[[ $(v check) == skip:* ]] || { echo "FAIL: no version file"; exit 1; }
echo server-v0.2.197 > "$t/version"
[[ $(v check) == "run server-v0.2.197" ]] || { echo "FAIL: new version"; exit 1; }
VERIFY_N=0; export VERIFY_N
[[ $(v check) == "skip: nothing in Verify" ]] || { echo "FAIL: empty Verify"; exit 1; }
VERIFY_N=3; export VERIFY_N
v done
echo server-v0.2.198 > "$t/version"; v done server-v0.2.197; [[ $(cat "$t/state/v") == server-v0.2.197 ]] || { echo "FAIL: done with version"; exit 1; }
echo server-v0.2.197 > "$t/version"
[[ $(v check) == "skip: server-v0.2.197 already verified" ]] || { echo "FAIL: verified"; exit 1; }
echo server-v0.2.198 > "$t/version"
[[ $(v check) == "run server-v0.2.198" ]] || { echo "FAIL: next version"; exit 1; }
echo ok
