import { describe, expect, it } from 'vitest'
import { createApp, h } from 'vue'
import AppMark from './AppMark.vue'

function render(props: { id: string; name: string; imageUrl?: string }) {
  const container = document.createElement('div')
  createApp({ render: () => h(AppMark, props) }).mount(container)
  return container
}

describe('AppMark', () => {
  it('draws the first letter on a colour that is stable for the id', () => {
    const first = render({ id: 'abc', name: 'notes' })
    const second = render({ id: 'abc', name: 'notes' })
    expect(first.textContent).toBe('N')
    expect(first.innerHTML).toBe(second.innerHTML)
  })

  it('shows the image when the app has one', () => {
    const container = render({ id: 'abc', name: 'notes', imageUrl: 'https://x.test/a.png' })
    expect(container.querySelector('img')?.getAttribute('src')).toBe('https://x.test/a.png')
  })
})
