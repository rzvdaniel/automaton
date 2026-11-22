#!/usr/bin/env bash
# Runs docker/production-host/automaton-deploy-website with fake curl and docker and checks what it does.
# Touches nothing real: no network, no Docker.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
sha=0123456789abcdef0123456789abcdef01234567
mkdir -p "$tmp/bin" "$tmp/src/x/docker"
touch "$tmp/src/x/docker/Dockerfile.website" "$tmp/env" "$tmp/compose.yml"
tar -czf "$tmp/src.tar.gz" -C "$tmp/src" x

cat > "$tmp/bin/curl" <<'FAKE'
#!/usr/bin/env bash
url="${*: -1}"
echo "curl $url" >> "$FAKE_LOG"
case "$url" in
  */commits/server-v*) echo "{\"sha\": \"$FAKE_SHA\"}" ;;
  */compare/main...*) echo "{\"status\": \"$FAKE_STATUS\"}" ;;
  */archive/*.tar.gz) cat "$FAKE_TARBALL" ;;
  *) exit 22 ;;
esac
FAKE
cat > "$tmp/bin/docker" <<'FAKE'
#!/usr/bin/env bash
echo "docker $* | ctx_has_dockerfile=$([[ -f $AUTOMATON_PRODUCTION_BUILD_CONTEXT/docker/Dockerfile.website ]] && echo yes || echo no) tag=$AUTOMATON_PRODUCTION_IMAGE_TAG" >> "$FAKE_LOG"
FAKE
if ! python3 -c 1 2>/dev/null; then printf '#!/usr/bin/env bash\nexec python "$@"\n' > "$tmp/bin/python3"; fi
chmod +x "$tmp/bin/"*

# A copy of the wrapper pointing at the fakes; the real one hardcodes root-owned paths and its own PATH.
sed -e "s#^PATH=.*#PATH=\"$tmp/bin:$PATH\"#" -e "s#^env_file=.*#env_file=$tmp/env#" -e "s#^compose_file=.*#compose_file=$tmp/compose.yml#" \
  "$repo/docker/production-host/automaton-deploy-website" > "$tmp/wrapper"
export FAKE_LOG="$tmp/log" FAKE_SHA="$sha" FAKE_STATUS=behind FAKE_TARBALL="$tmp/src.tar.gz"

bash -n "$repo/docker/production-host/automaton-deploy-website"
bash "$tmp/wrapper" 0.2.170
cat "$FAKE_LOG"
base="docker compose -p automaton-prod --env-file $tmp/env -f $tmp/compose.yml"
grep -qx "curl https://github.com/rzvdaniel/automaton/archive/$sha.tar.gz" "$FAKE_LOG"   # by sha, never by tag name
! grep -q "archive/refs/tags" "$FAKE_LOG"
grep -qx "$base build website-green | ctx_has_dockerfile=yes tag=0.2.170" "$FAKE_LOG"
grep -qx "$base up -d --no-deps --no-build --wait website-green | ctx_has_dockerfile=yes tag=0.2.170" "$FAKE_LOG"

: > "$FAKE_LOG"
if FAKE_STATUS=diverged bash "$tmp/wrapper" 0.2.170 2>/dev/null; then echo "commit not on main accepted"; exit 1; fi
if FAKE_STATUS=ahead bash "$tmp/wrapper" 0.2.170 2>/dev/null; then echo "ahead commit accepted"; exit 1; fi
if FAKE_SHA=bogus bash "$tmp/wrapper" 0.2.170 2>/dev/null; then echo "bad sha accepted"; exit 1; fi
if bash "$tmp/wrapper" '0.2.1;x' 2>/dev/null; then echo "bad version accepted"; exit 1; fi
! grep -q '^docker' "$FAKE_LOG"
echo PASS
