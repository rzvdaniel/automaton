#!/usr/bin/env python3
"""Publish a proof page as the `proof` artifact on a GitHub issue.

Usage: python3 proof.py <issue-number> proof.html [--repo owner/name]

Screenshots go inside the page: write <img src="shots/name.png" alt="..."> with a path relative
to the page, and each one is shrunk to a WebP at most 1000 px wide and embedded, so the proof
shows it with no other hosting (needs Pillow: pip install pillow). GitHub's preview does not load
images from other sites, so an <img src="https://..."> becomes a link instead. An existing
`proof` artifact on the issue is replaced. Requires the gh CLI.
"""
import base64
import io
import json
import os
import re
import subprocess
import sys
import tempfile
from html import escape
from pathlib import Path

HINT = ('<p id="proof-hint" style="color:#666">Links: the preview blocks a plain click, '
        'so right-click a link and choose Open in new tab.</p>')
LIMIT = 1024 * 1024  # text plus a handful of 20-100 KB screenshots


def link_images(html: str) -> str:
    def replace(match: re.Match) -> str:
        tag, src = match.group(0), match.group(1)
        alt = re.search(r'\balt="([^"]*)"', tag)
        label = escape(alt.group(1) if alt and alt.group(1) else src.rsplit("/", 1)[-1])
        return f'<a href="{src}" target="_blank" rel="noopener">{label}</a>'

    html = re.sub(r'<img\b[^>]*?\bsrc=["\'](https?://[^"\']+)["\'][^>]*>', replace, html)
    if "<a " in html and 'id="proof-hint"' not in html:
        body = re.search(r"<body[^>]*>", html)
        html = html[:body.end()] + HINT + html[body.end():] if body else HINT + html
    return html


def embed_images(html: str, base: Path) -> str:
    """Replace each <img src="local/path"> with a small embedded WebP."""
    def replace(match: re.Match) -> str:
        src = match.group(2)
        if src.startswith(("http:", "https:", "data:")):
            return match.group(0)
        try:
            from PIL import Image
        except ImportError:
            sys.exit("Embedding screenshots needs Pillow: pip install pillow")
        try:
            image = Image.open(base / src).convert("RGB")
        except OSError as error:
            sys.exit(f"Cannot read screenshot {src}: {error}")
        image.thumbnail((1000, 4000))
        buffer = io.BytesIO()
        image.save(buffer, "WEBP", quality=70)
        data = base64.b64encode(buffer.getvalue()).decode()
        return f"{match.group(1)}data:image/webp;base64,{data}{match.group(3)}"

    return re.sub(r"""(<img\b[^>]*?\bsrc=["'])([^"']+)(["'])""", replace, html)


def gh(*args: str) -> str:
    return subprocess.run(["gh", *args], check=True, capture_output=True, text=True).stdout


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    issue, page = sys.argv[1], Path(sys.argv[2])
    repo = sys.argv[sys.argv.index("--repo") + 1] if "--repo" in sys.argv else "rzvdaniel/automaton"
    html = embed_images(link_images(page.read_text(encoding="utf-8")), page.parent)
    if len(html.encode()) > LIMIT:
        print("Proof page is over 1 MB; keep fewer screenshots (the viewport, not the full page).", file=sys.stderr)
        return 1
    base = f"repos/{repo}/issues/{issue}/artifacts"
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as handle:
        handle.write(html)
    try:
        created = json.loads(gh("api", "-X", "POST", base, "-f", "name=proof", "-f", "type=embedded_html",
                                "-F", f"body=@{handle.name}"))
        listed = gh("api", "--paginate", "--slurp", base)
        for item in (entry for page_items in json.loads(listed) for entry in page_items):
            if item.get("name") == "proof" and item["number"] != created["number"]:
                gh("api", "-X", "DELETE", f"{base}/{item['number']}")
    except subprocess.CalledProcessError as error:
        print(f"gh failed: {error.stderr.strip() or error}", file=sys.stderr)
        return 1
    finally:
        os.unlink(handle.name)
    print(f"https://github.com/{repo}/issues/{issue}  (artifact {created['number']}, {len(html.encode())} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
