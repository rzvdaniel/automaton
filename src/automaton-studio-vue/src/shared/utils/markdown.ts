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

function splitRow(line: string): string[] {
  let row = line.trim()
  if (row.startsWith('|')) row = row.slice(1)
  if (row.endsWith('|') && !row.endsWith('\\|')) row = row.slice(0, -1)
  return row.split(/(?<!\\)\|/).map((cell) => cell.trim().replace(/\\\|/g, '|'))
}

function isTableDivider(line: string): boolean {
  const trimmed = line.trim()
  return trimmed.includes('-') && /^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$/.test(trimmed) && (trimmed.includes('|'))
}

function renderTable(header: string[], aligns: string[], rows: string[][]): string {
  const cell = (tag: string, value: string, index: number) =>
    `<${tag}${aligns[index] ? ` style="text-align:${aligns[index]}"` : ''}>${renderInlineMarkdown(value)}</${tag}>`
  const head = `<tr>${header.map((value, i) => cell('th', value, i)).join('')}</tr>`
  const body = rows.map((row) => `<tr>${header.map((_, i) => cell('td', row[i] ?? '', i)).join('')}</tr>`).join('')
  return `<div class="table-scroll"><table><thead>${head}</thead><tbody>${body}</tbody></table></div>`
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

  for (let index = 0; index < lines.length; index++) {
    const rawLine = lines[index]!
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

    if (line.includes('|') && index + 1 < lines.length && isTableDivider(lines[index + 1]!)) {
      const header = splitRow(line)
      const dividers = splitRow(lines[index + 1]!)
      if (header.length === dividers.length) {
        flushParagraph()
        flushList()
        const aligns = dividers.map((d) => (d.startsWith(':') && d.endsWith(':') ? 'center' : d.endsWith(':') ? 'right' : d.startsWith(':') ? 'left' : ''))
        const rows: string[][] = []
        index += 2
        while (index < lines.length && lines[index]!.trim() && lines[index]!.includes('|')) rows.push(splitRow(lines[index++]!))
        index--
        html.push(renderTable(header, aligns, rows))
        continue
      }
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
