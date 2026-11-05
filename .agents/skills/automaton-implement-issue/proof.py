#!/usr/bin/env python3
"""Publish a proof page as the `proof` artifact on a GitHub issue.

Usage: python3 proof.py <issue-number> proof.html [--repo owner/name]

Screenshots live on the `pr-screenshots` branch and the proof links to them: GitHub's artifact
preview does not load images from other sites. Any <img src="https://..."> left in the page is
turned into a link to that image. An existing `proof` artifact on the issue is replaced.
Requires the gh CLI.
"""
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

HINT = ('<p id="proof-hint" style="color:#666">Screenshots are links: the preview blocks a plain click, '
        'so right-click a link and choose Open in new tab.</p>')
LIMIT = 1024 * 1024  # a proof is text and links; embedded images do not belong here


def link_images(html: str) -> str:
    def replace(match: re.Match) -> str:
        tag, src = match.group(0), match.group(1)
        alt = re.search(r'\balt="([^"]*)"', tag)
        label = alt.group(1) if alt and alt.group(1) else src.rsplit("/", 1)[-1]
        return f'<a href="{src}" target="_blank" rel="noopener">{label}</a>'

    return re.sub(r'<img\b[^>]*?\bsrc="(https?://[^"]+)"[^>]*>', replace, html)


def gh(*args: str) -> str:
    return subprocess.run(["gh", *args], check=True, capture_output=True, text=True).stdout


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    issue, page = sys.argv[1], Path(sys.argv[2])
    repo = sys.argv[sys.argv.index("--repo") + 1] if "--repo" in sys.argv else "rzvdaniel/automaton"
    html = link_images(page.read_text(encoding="utf-8"))
    if "data:image" in html or len(html.encode()) > LIMIT:
        print("Proof page embeds images or is over 1 MB; link screenshots on the pr-screenshots branch instead.",
              file=sys.stderr)
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
