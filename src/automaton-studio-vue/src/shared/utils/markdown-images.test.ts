import { describe, expect, it } from 'vitest'
import { renderMarkdown } from './markdown'

describe('markdown images', () => {
  it('renders a relative image for the Docs tab to load', () => {
    expect(renderMarkdown('![Plans](plans.png)')).toContain('<img data-docs-src="plans.png" alt="Plans">')
  })
  it('does not load remote or scheme images', () => {
    const html = renderMarkdown('![x](https://e.com/a.png) ![y](javascript:alert(1)) ![z](//e.com/a.png)')
    expect(html).not.toContain('<img')
  })
})
