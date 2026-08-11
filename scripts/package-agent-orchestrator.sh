#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-1.0.0}"
OUTPUT="${2:-$ROOT/artifacts/agent-orchestrator}"
REVISION="${SOURCE_REVISION_ID:-$(git -C "$ROOT" rev-parse HEAD)}"
[[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]] || { echo "Invalid version." >&2; exit 2; }
[[ "$REVISION" =~ ^[0-9a-fA-F]{7,64}$ ]] || { echo "Invalid source revision." >&2; exit 2; }
for command in dotnet git tar gzip sha256sum; do command -v "$command" >/dev/null || { echo "Missing prerequisite: $command" >&2; exit 3; }; done

mkdir -p "$OUTPUT"
STAGING="$(mktemp -d)"
trap 'rm -rf -- "$STAGING"' EXIT
PUBLISH="$STAGING/Automaton.AgentOrchestrator-$VERSION-linux-x64"
dotnet publish "$ROOT/src/Automaton.AgentOrchestrator/Automaton.AgentOrchestrator.csproj" -c Release -r linux-x64 --self-contained false -o "$PUBLISH" \
  -p:Version="$VERSION" -p:SourceRevisionId="$REVISION" -p:ContinuousIntegrationBuild=true
find "$PUBLISH" -type f -name '*.pdb' -delete
test -f "$PUBLISH/Automaton.AgentOrchestrator"
test -f "$PUBLISH/Automaton.AgentOrchestrator.runtimeconfig.json"
if find "$PUBLISH" -type f \( -name '*.env' -o -name 'WORKFLOW.md' -o -name '*Tests*' \) | grep -q .; then
  echo "Release contains forbidden configuration, secret, or test files." >&2; exit 4
fi
ARCHIVE="$OUTPUT/automaton-agent-orchestrator-$VERSION-linux-x64.tar.gz"
EPOCH="${SOURCE_DATE_EPOCH:-$(git -C "$ROOT" show -s --format=%ct "$REVISION")}"
tar --sort=name --mtime="@$EPOCH" --owner=0 --group=0 --numeric-owner -C "$PUBLISH" -cf - . | gzip -n > "$ARCHIVE"
sha256sum "$ARCHIVE" > "$ARCHIVE.sha256"
echo "$ARCHIVE"
