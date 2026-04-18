#!/usr/bin/env bash
# Build self-contained single-file binaries for all platforms.
# Run from the repository root:
#   chmod +x scripts/cli/publish-binaries.sh && ./scripts/cli/publish-binaries.sh

set -e

PROJECT="src/Automaton.Cli/Automaton.Cli.csproj"
OUT="artifacts/cli"

RIDS=(
    "win-x64"
    "win-arm64"
    "linux-x64"
    "linux-arm64"
    "osx-x64"
    "osx-arm64"
)

echo "Building Automaton CLI binaries..."
mkdir -p "$OUT"

for RID in "${RIDS[@]}"; do
    echo "  → $RID"
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:EnableCompressionInSingleFile=true \
        -o "$OUT/$RID" \
        --nologo -q

    # Rename to automaton / automaton.exe
    if [[ "$RID" == win-* ]]; then
        mv "$OUT/$RID/automaton.exe" "$OUT/automaton-$RID.exe" 2>/dev/null || true
    else
        mv "$OUT/$RID/automaton"     "$OUT/automaton-$RID"     2>/dev/null || true
    fi

    rm -rf "$OUT/$RID"
done

echo ""
echo "Binaries written to $OUT/:"
ls -lh "$OUT/"
