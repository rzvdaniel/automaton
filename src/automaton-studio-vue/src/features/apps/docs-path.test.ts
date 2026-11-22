import { describe, expect, it } from 'vitest'
import { resolveDocsPath } from './docs-path'
import { renderMarkdown } from '@/shared/utils/markdown'

describe('docs links', () => {
  it('resolves a relative link next to the current page', () => {
    expect(resolveDocsPath('docs/README.md', 'setup.md')).toBe('docs/setup.md')
    expect(resolveDocsPath('docs/guide/a.md', '../b.md#top')).toBe('docs/b.md')
    expect(resolveDocsPath('docs/README.md', './guide/a.md')).toBe('docs/guide/a.md')
  })

  it('refuses links that leave docs/', () => {
    expect(resolveDocsPath('docs/README.md', '../automaton.json')).toBeNull()
    expect(resolveDocsPath('docs/README.md', '#section')).toBeNull()
  })
})

describe('markdown for docs', () => {
  it('drops javascript: links but keeps http and relative ones', () => {
    const html = renderMarkdown('[a](javascript:alert(1)) [b](https://x.test) [c](other.md)')
    expect(html).not.toContain('javascript:')
    expect(html).toContain('href="https://x.test"')
    expect(html).toContain('href="other.md"')
  })

  it('drops javascript: links hidden by spaces or tabs', () => {
    expect(renderMarkdown('[a]( javascript:alert(1)) [b](java\tscript:alert(1))')).not.toContain('href')
  })

  it('renders fenced code as escaped text', () => {
    expect(renderMarkdown('```\nautomaton docs <app>\n```')).toBe('<pre><code>automaton docs &lt;app&gt;</code></pre>')
  })
})
