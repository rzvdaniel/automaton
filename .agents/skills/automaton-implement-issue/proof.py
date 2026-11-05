#!/usr/bin/env python3
"""Publish a proof page as the `proof` artifact on a GitHub issue.

Usage: python3 proof.py <issue-number> proof.html [--repo owner/name]

Images in proof.html may be local paths or https URLs (for example the pr-screenshots branch).
GitHub's artifact preview blocks images from other sites, so every <img src> is embedded as a
data: URI, and clicking an image enlarges it (CSS only; click outside it to close).
An existing `proof` artifact on the issue is replaced. Requires the gh CLI.
"""
import base64
import json
import mimetypes
import pathlib
import re
import subprocess
import sys
import tempfile
import urllib.request

ZOOM_STYLE = """<style id="proof-zoom">
img[tabindex]{cursor:zoom-in;max-width:100%}
img[tabindex]:focus{position:fixed;inset:0;margin:auto;max-width:96vw;max-height:96vh;width:auto;height:auto;
z-index:1000;cursor:zoom-out;box-shadow:0 0 0 100vmax rgba(0,0,0,.8);outline:none}
</style>"""
LIMIT = 8 * 1024 * 1024  # keep pages small enough to load quickly; shrink screenshots if this trips


def embed(html: str, base: pathlib.Path) -> str:
    def replace(match: re.Match) -> str:
        src = match.group(2)
        if src.startswith("data:"):
            return match.group(0)
        if src.startswith(("http://", "https://")):
            with urllib.request.urlopen(src, timeout=30) as response:
                data = response.read()
                kind = response.headers.get_content_type()
        else:
            path = (base / src).resolve()
            data = path.read_bytes()
            kind = mimetypes.guess_type(path.name)[0] or "image/png"
        return f'{match.group(1)}data:{kind};base64,{base64.b64encode(data).decode()}"'

    return re.sub(r'(<img\b[^>]*?\bsrc=")([^"]+)"', replace, html)


def gh(*args: str) -> str:
    return subprocess.run(["gh", *args], check=True, capture_output=True, text=True).stdout


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    issue, page = sys.argv[1], pathlib.Path(sys.argv[2])
    repo = sys.argv[sys.argv.index("--repo") + 1] if "--repo" in sys.argv else "rzvdaniel/automaton"
    html = embed(page.read_text(encoding="utf-8"), page.parent)
    if len(html.encode()) > LIMIT:
        print(f"Proof page is {len(html.encode())} bytes; shrink the screenshots (limit {LIMIT}).", file=sys.stderr)
        return 1
    base = f"repos/{repo}/issues/{issue}/artifacts"
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as handle:
        handle.write(html)
    created = json.loads(gh("api", "-X", "POST", base, "-f", "name=proof", "-f", "type=embedded_html",
                            "-F", f"body=@{handle.name}"))
    for item in json.loads(gh("api", base)):
        if item.get("name") == "proof" and item["number"] != created["number"]:
            gh("api", "-X", "DELETE", f"{base}/{item['number']}")
    print(f"https://github.com/{repo}/issues/{issue}  (artifact {created['number']}, {len(html.encode())} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
