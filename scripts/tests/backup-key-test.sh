#!/usr/bin/env bash
# The private backup key stays off the server: with backup-recipient present and backup-key absent, backup.sh never
# creates a key pair and keeps the same recipient; a first key is shown once and no private key stays on disk.
# Runs initialize_backup_key from docker/install/backup.sh against a fake docker; touches only a temp directory.
#   scripts/tests/backup-key-test.sh
set -euo pipefail
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
fail() { echo "FAIL: $*" >&2; exit 1; }

mkdir "$work/bin"
cat > "$work/bin/docker" <<'WRAP'
#!/usr/bin/env bash
echo "$*" >> "$FAKE_DOCKER_LOG"
case "$*" in
  *age-keygen\ -o*) while [[ $1 != -v ]]; do shift; done; dir=${2%%:/keys*}
     printf '# public key: age1newrecipient0000\nAGE-SECRET-KEY-1TESTSECRET\n' > "$dir/.backup-key.partial"
     echo 'Public key: age1newrecipient0000' >&2 ;;
  *age-keygen\ -y*) echo age1fromidentity0000 ;;
esac
WRAP
chmod +x "$work/bin/docker"
export PATH="$work/bin:$PATH" FAKE_DOCKER_LOG="$work/docker.log"

source <(sed -n '/^initialize_backup_key() {/,/^}/p' "$repo/docker/install/backup.sh")
age_image=example.invalid/age@sha256:$(printf 'a%.0s' {1..64})

# Existing server: recipient present, private key already removed.
config_dir=$work/existing; mkdir -p "$config_dir/secrets"
echo age1existingrecipient0000 > "$config_dir/backup-recipient"
recipient_file=$config_dir/backup-recipient identity_file=$config_dir/secrets/backup-key
for _ in 1 2; do out=$(initialize_backup_key 2>&1) || fail "existing recipient without key was rejected: $out"; done
[[ $(cat "$recipient_file") == age1existingrecipient0000 ]] || fail 'the recipient changed'
[[ ! -e $identity_file ]] || fail 'a private key appeared on disk'
! grep -q age-keygen "$work/docker.log" 2>/dev/null || fail 'a key pair was generated'

# Unattended run (nightly, Studio, update) with no key at all: never prints the key, keeps it 0600 on disk.
config_dir=$work/unattended; mkdir -p "$config_dir"
recipient_file=$config_dir/backup-recipient identity_file=$config_dir/secrets/backup-key
out=$(initialize_backup_key 2>&1) || fail "unattended generation failed: $out"
! grep -q AGE-SECRET-KEY <<< "$out" || fail 'an unattended run printed the private key'
[[ $(stat -c %a "$identity_file") == 600 ]] || fail 'the unattended key is not 0600 on disk'

# Fresh install (installer sets the flag): key shown once, nothing private left on disk.
export AUTOMATON_SHOW_BACKUP_KEY=1
config_dir=$work/fresh; mkdir -p "$config_dir"
recipient_file=$config_dir/backup-recipient identity_file=$config_dir/secrets/backup-key
out=$(initialize_backup_key 2>&1) || fail "fresh key generation failed: $out"
grep -q AGE-SECRET-KEY-1TESTSECRET <<< "$out" || fail 'the key was not shown'
grep -qi 'save' <<< "$out" || fail 'no "save this now" notice'
[[ $(cat "$recipient_file") == age1newrecipient0000 ]] || fail 'wrong recipient written'
[[ -z $(grep -rl AGE-SECRET-KEY "$config_dir" 2>/dev/null || true) ]] || fail 'a private key stayed on disk'
out2=$(initialize_backup_key 2>&1) || fail 'second run failed'
! grep -q AGE-SECRET-KEY <<< "$out2" || fail 'the key was shown twice'
echo 'backup-key-test: ok'
