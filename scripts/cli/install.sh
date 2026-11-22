#!/bin/sh
# Automaton CLI installer: downloads the self-contained `automaton` binary, no .NET needed.
# Usage:
#   curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
# Options (environment): AUTOMATON_VERSION=server-v0.2.90 (default: latest), AUTOMATON_INSTALL_DIR (default: ~/.local/bin)

set -eu

repo="rzvdaniel/automaton"
dir="${AUTOMATON_INSTALL_DIR:-$HOME/.local/bin}"

case "$(uname -s)" in
  Linux) os=linux ;;
  Darwin) os=osx ;;
  *) echo "Unsupported system: $(uname -s). On Windows use install.ps1." >&2; exit 1 ;;
esac
case "$(uname -m)" in
  x86_64 | amd64) arch=x64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *) echo "Unsupported CPU: $(uname -m)" >&2; exit 1 ;;
esac
asset="automaton-$os-$arch"

if [ -n "${AUTOMATON_VERSION:-}" ]; then
  base="https://github.com/$repo/releases/download/$AUTOMATON_VERSION"
else
  base="https://github.com/$repo/releases/latest/download"
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Downloading $asset..."
curl -fsSL "$base/$asset" -o "$tmp/automaton"
curl -fsSL "$base/$asset.sha256" -o "$tmp/automaton.sha256"

expected="$(cut -d ' ' -f 1 "$tmp/automaton.sha256")"
if command -v sha256sum >/dev/null 2>&1; then
  actual="$(sha256sum "$tmp/automaton" | cut -d ' ' -f 1)"
else
  actual="$(shasum -a 256 "$tmp/automaton" | cut -d ' ' -f 1)"
fi
if [ "$expected" != "$actual" ]; then
  echo "Checksum mismatch; nothing was installed." >&2
  exit 1
fi

mkdir -p "$dir"
chmod +x "$tmp/automaton"
mv "$tmp/automaton" "$dir/automaton"

echo "Installed $("$dir/automaton" --version | head -1) to $dir/automaton"
case ":$PATH:" in
  *":$dir:"*) ;;
  *) echo "Add $dir to your PATH, for example: export PATH=\"\$PATH:$dir\"" ;;
esac
echo "Next: automaton config --server https://your-automaton-server --key ak_execute_xxx"
