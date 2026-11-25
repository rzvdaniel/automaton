"""Build an editable article artifact page from a draft folder.

Usage: python build-article.py <folder>   (folder holds draft.md and <nn>-<what>.png)
Writes <folder>/article.html; publish it with the Artifact tool, capabilities {"artifact": {}, "downloads": true}.
Markdown subset: # / ## headings, paragraphs, **bold**, `code`, [text](url), <url>, ![alt](file.png), TODO(owner) lines.
"""
import base64, html, json, pathlib, re, sys

HERE = pathlib.Path(__file__).parent


def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r"`([^`]+)`", r"<code>\1</code>", t)
    t = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", t)
    t = re.sub(r"\[([^\]]+)\]\((https?://[^)]+)\)", r'<a href="\2">\1</a>', t)
    t = re.sub(r"&lt;(https?://[^&]+)&gt;", r'<a href="\1">\1</a>', t)
    return t


def convert(md):
    out, title = [], ""
    for block in re.split(r"\n\s*\n", md.strip()):
        b = block.strip()
        if b.startswith("# "):
            title = b[2:].strip()
            out.append(f"<h1>{inline(title)}</h1>")
        elif b.startswith("## "):
            out.append(f"<h2>{inline(b[3:])}</h2>")
        elif m := re.fullmatch(r"!\[([^\]]*)\]\(([^)]+)\)", b):
            alt, src = m.groups()
            out.append(f'<figure><img data-img="{html.escape(src)}" alt="{html.escape(alt)}"><figcaption>{inline(alt)}</figcaption></figure>')
        elif b.startswith("TODO(owner)"):
            out.append(f'<p class="todo">{inline(b)}</p>')
        else:
            out.append(f"<p>{inline(' '.join(b.splitlines()))}</p>")
    return title, "\n".join(out)


def main(folder):
    folder = pathlib.Path(folder)
    title, body = convert((folder / "draft.md").read_text(encoding="utf-8"))
    images = {}
    for name in re.findall(r'data-img="([^"]+)"', body):
        p = folder / html.unescape(name)
        if p.exists():
            images[name] = "data:image/png;base64," + base64.b64encode(p.read_bytes()).decode()
    page = (HERE / "article-template.html").read_text(encoding="utf-8")
    page = page.replace("__TITLE__", html.escape(title or folder.name)).replace("__BODY__", body)
    page = page.replace("__IMAGES__", json.dumps(images).replace("<", "\\u003c"))
    (folder / "article.html").write_text(page, encoding="utf-8")
    print(folder / "article.html", f"{len(page) // 1024} KB, {len(images)} images")


if __name__ == "__main__":
    main(sys.argv[1])
