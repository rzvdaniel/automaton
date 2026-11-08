import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppMarketplaceDetailView from './AppMarketplaceDetailView.vue'

vi.mock('@/config/publicAppConfig', () => ({
  publicAppConfig: { apiBaseUrl: 'https://api.test/', studioUrl: 'https://studio.test/', storeTitle: 'Store' },
  getPublishedAppUrl: (url: string) => `https://apps.test/${url}`,
}))

async function mountPage(app: object) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => (String(url).endsWith('/creator-apps') ? [] : app),
  })))
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:p(.*)*', component: AppMarketplaceDetailView }] })
  await router.push('/apps/x')
  const container = document.createElement('div')
  const vue = createApp({ render: () => h(AppMarketplaceDetailView) })
  vue.use(router)
  vue.mount(container)
  await new Promise((resolve) => setTimeout(resolve, 20))
  return { container, vue }
}

afterEach(() => vi.unstubAllGlobals())

describe('AppMarketplaceDetailView', () => {
  it('shows a generated mark and no gallery for an app without images', async () => {
    const { container, vue } = await mountPage({
      id: 'x', name: 'Notes', canLaunch: false, canRemix: false, viewCount: 0, forkCount: 0, isForkable: false,
      hasApplicationSource: false, screenshots: [], relatedApps: [], forksAndRemixes: [],
    })
    expect(container.querySelector('h1')?.textContent).toBe('Notes')
    expect(container.querySelector('.hero.plain')).not.toBeNull()
    expect(container.querySelector('.gallery')).toBeNull()
    expect(container.querySelector('.title .app-mark')?.textContent).toBe('N')
    vue.unmount()
  })
})
