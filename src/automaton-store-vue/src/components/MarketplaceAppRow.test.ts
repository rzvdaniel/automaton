import { describe, expect, it } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { vi } from 'vitest'
import MarketplaceAppRow from './MarketplaceAppRow.vue'

vi.mock('@/config/publicAppConfig', () => ({ publicAppConfig: { studioUrl: 'https://studio.test/' } }))
import type { MarketplaceAppCard } from '@/types/marketplace'

describe('MarketplaceAppRow', () => {
  it.each([true, false])('opens store details when canLaunch is %s', async (canLaunch) => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { render: () => null } },
        { path: '/apps/:appId', component: { render: () => null } },
      ],
    })
    await router.push('/')
    const app = createApp({
      render: () => h(MarketplaceAppRow, {
        app: { id: 'example', name: 'Example', canLaunch, publicUrl: 'example' } as MarketplaceAppCard,
      }),
    })
    app.use(router)
    const container = document.createElement('div')
    document.body.append(container)
    app.mount(container)
    try {
      const open = container.querySelector<HTMLAnchorElement>('a.btn')!
      expect(open.getAttribute('href')).toBe('/apps/example')
      expect(open.hasAttribute('target')).toBe(false)
      open.click()
      await new Promise((resolve) => setTimeout(resolve, 0))
      expect(router.currentRoute.value.path).toBe('/apps/example')
    } finally {
      app.unmount()
      container.remove()
    }
  })

  async function render(app: Partial<MarketplaceAppCard>) {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:p(.*)*', component: { render: () => null } }] })
    await router.push('/')
    const vue = createApp({ render: () => h(MarketplaceAppRow, { app: { id: 'x', name: 'X', canLaunch: true, ...app } as MarketplaceAppCard }) })
    vue.use(router)
    const container = document.createElement('div')
    vue.mount(container)
    return { text: container.textContent ?? '', buttons: [...container.querySelectorAll('a.btn')].map((a) => a.textContent), vue }
  }

  it('shows Open and Install for a forkable web app, and only Open for a plain one', async () => {
    const web = await render({ isForkable: true, hasWebPage: true })
    expect(web.buttons).toEqual(['Open', 'Install'])
    expect(web.text).toContain('Open to use')
    web.vue.unmount()
    const plain = await render({ isForkable: false, hasWebPage: true })
    expect(plain.buttons).toEqual(['Open'])
    expect(plain.text).not.toContain('Installable')
    plain.vue.unmount()
  })
})
