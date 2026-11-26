import { describe, expect, it } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import StoreHeader from './StoreHeader.vue'

describe('StoreHeader', () => {
  it('shows the website menu with Live apps current, and the logo links to the website', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })
    await router.push('/')
    const container = document.createElement('div')
    createApp({ render: () => h(StoreHeader) }).use(router).mount(container)

    const links = [...container.querySelectorAll('nav a.link')].map((a) => a.textContent?.trim())
    expect(links).toEqual(['Product', 'Use cases', 'Catalog', 'Live apps', 'Docs', 'GitHub'])
    expect(container.querySelector('a[aria-current="page"]')?.textContent?.trim()).toBe('Live apps')
    expect(container.querySelector('a.brand')?.getAttribute('href')).toBe('https://automaton.monster/')
    expect(container.textContent).not.toContain('App Store')
    expect(container.textContent).toContain('Get started')
  })
})
