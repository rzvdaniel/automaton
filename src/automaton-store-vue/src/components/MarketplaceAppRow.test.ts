import { describe, expect, it, vi } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import MarketplaceAppRow from './MarketplaceAppRow.vue'
import type { MarketplaceAppCard } from '@/types/marketplace'

vi.mock('@/config/publicAppConfig', () => ({ publicAppConfig: { studioUrl: 'https://studio.test/', apiBaseUrl: 'https://api.test/' } }))

async function render(app: Partial<MarketplaceAppCard>) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { render: () => null } },
      { path: '/apps/:appId', component: { render: () => null } },
    ],
  })
  await router.push('/')
  const vue = createApp({ render: () => h(MarketplaceAppRow, { app: { id: 'x', name: 'X', canLaunch: true, ...app } as MarketplaceAppCard }) })
  vue.use(router)
  const container = document.createElement('div')
  document.body.append(container)
  vue.mount(container)
  return { container, router, vue }
}

describe('MarketplaceAppRow', () => {
  it.each([true, false])('links the card to store details when canLaunch is %s', async (canLaunch) => {
    const { container, router, vue } = await render({ id: 'example', name: 'Example', canLaunch })
    const link = container.querySelector<HTMLAnchorElement>('a.name')!
    expect(link.getAttribute('href')).toBe('/apps/example')
    expect(link.hasAttribute('target')).toBe(false)
    link.click()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(router.currentRoute.value.path).toBe('/apps/example')
    vue.unmount()
    container.remove()
  })

  it('shows the cover image when there is one', async () => {
    const { container, vue } = await render({ thumbnailUrl: 'https://img.test/c.png' })
    expect(container.querySelector('img.cover')?.getAttribute('src')).toBe('https://img.test/c.png')
    expect(container.querySelector('.placeholder')).toBeNull()
    vue.unmount()
    container.remove()
  })

  it('shows a tinted tile with the name when there is no cover', async () => {
    const { container, vue } = await render({ name: 'Untranslatable' })
    expect(container.querySelector('img.cover')).toBeNull()
    expect(container.querySelector('.placeholder')?.textContent).toBe('Untranslatable')
    vue.unmount()
    container.remove()
  })

  it('shows the creator link, category and an installable badge', async () => {
    const { container, vue } = await render({
      canLaunch: false, isForkable: true, creatorDisplayName: 'Ana', creatorPublicUsername: 'ana', category: 'Games',
    })
    expect(container.querySelector('a.creator')?.getAttribute('href')).toBe('/creators/ana')
    expect(container.textContent).toContain('Games')
    expect(container.textContent).toContain('Installable')
    expect(container.textContent).not.toContain('Open to use')
    vue.unmount()
    container.remove()
  })
})
