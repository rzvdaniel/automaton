import { describe, expect, it } from 'vitest'
import { renderMarkdown } from './markdown'

describe('markdown tables', () => {
  const md = '| Plan | Runs | Cost |\n| :--- | :---: | ---: |\n| Free | `daily` | 0 |\n| Pro | **hourly** | 5 |\n\nAfter.'
  it('renders a header row, body rows and alignment inside a scroll box', () => {
    const html = renderMarkdown(md)
    expect(html).toContain('<div class="table-scroll"><table><thead>')
    expect(html).toContain('<th style="text-align:left">Plan</th>')
    expect(html).toContain('<th style="text-align:center">Runs</th>')
    expect(html).toContain('<td style="text-align:right">5</td>')
    expect(html).toContain('<td style="text-align:center"><code>daily</code></td>')
    expect(html).toContain('<strong>hourly</strong>')
    expect(html).toContain('<p>After.</p>')
    expect(html).not.toContain('| ---')
  })
  it('escapes cell html and keeps non-tables as paragraphs', () => {
    expect(renderMarkdown('| a |\n| - |\n| <b> |')).toContain('&lt;b&gt;')
    expect(renderMarkdown('a | b')).toBe('<p>a | b</p>')
  })
})
