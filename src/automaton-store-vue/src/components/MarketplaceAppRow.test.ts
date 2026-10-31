import { describe, expect, it } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import MarketplaceAppRow from './MarketplaceAppRow.vue'
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
})
