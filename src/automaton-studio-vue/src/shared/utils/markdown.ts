function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

/** Links keep http(s), mailto, in-page and relative targets; any other scheme (javascript:, data:) shows as plain text. */
function isSafeHref(href: string): boolean {
  // Browsers ignore leading spaces and strip tabs and line breaks inside a URL, so judge the link as they read it.
  const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(href.replace(/&#39;|&quot;|&amp;/g, '').split('').filter((char) => char.charCodeAt(0) > 32 && char.charCodeAt(0) !== 127).join(''))
  return !scheme || ['http', 'https', 'mailto'].includes(scheme[1]!.toLowerCase())
}

function renderInlineMarkdown(value: string): string {
  let html = escapeHtml(value)

  // Images: only relative ones render (as data-docs-src, which the Docs tab loads with the user's sign-in); others show their alt text.
  html = html.replace(/!\[([^\]]*)\]\(([^)\s]+)\)/g, (_match, alt: string, src: string) =>
    /^[a-z][a-z0-9+.-]*:|^\/\//i.test(src) ? alt : `<img data-docs-src="${src}" alt="${alt}">`)
  html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_match, text: string, href: string) =>
    isSafeHref(href) ? `<a href="${href}" target="_blank" rel="noopener noreferrer">${text}</a>` : text)
  html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
  html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>')
  html = html.replace(/`([^`]+)`/g, '<code>$1</code>')

  return html
}

export function renderMarkdown(markdown: string): string {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  const html: string[] = []
  let paragraphLines: string[] = []
  let listItems: string[] = []

  function flushParagraph() {
    if (paragraphLines.length === 0) return
    html.push(`<p>${renderInlineMarkdown(paragraphLines.join(' '))}</p>`)
    paragraphLines = []
  }

  function flushList() {
    if (listItems.length === 0) return
    html.push(`<ul>${listItems.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join('')}</ul>`)
    listItems = []
  }

  let codeLines: string[] | null = null

  for (const rawLine of lines) {
    const line = rawLine.trim()

    if (line.startsWith('```')) {
      if (codeLines === null) {
        flushParagraph()
        flushList()
        codeLines = []
      } else {
        html.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`)
        codeLines = null
      }
      continue
    }
    if (codeLines !== null) {
      codeLines.push(rawLine)
      continue
    }

    if (!line) {
      flushParagraph()
      flushList()
      continue
    }

    if (line.startsWith('# ')) {
      flushParagraph()
      flushList()
      html.push(`<h1>${renderInlineMarkdown(line.slice(2))}</h1>`)
      continue
    }

    if (line.startsWith('## ')) {
      flushParagraph()
      flushList()
      html.push(`<h2>${renderInlineMarkdown(line.slice(3))}</h2>`)
      continue
    }

    if (line.startsWith('### ')) {
      flushParagraph()
      flushList()
      html.push(`<h3>${renderInlineMarkdown(line.slice(4))}</h3>`)
      continue
    }

    if (line.startsWith('- ')) {
      flushParagraph()
      listItems.push(line.slice(2))
      continue
    }

    flushList()
    paragraphLines.push(line)
  }

  if (codeLines !== null) html.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`)
  flushParagraph()
  flushList()

  return html.join('\n')
}
