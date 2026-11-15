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
    expect(container.querySelector('.feats')).toBeNull()
    expect(container.textContent).not.toContain('Run it on your own server')
    vue.unmount()
  })

  it('shows the own-server card for catalog apps only', async () => {
    const base = { id: 'x', name: 'Harbour', canLaunch: false, canRemix: false, viewCount: 0, forkCount: 0, isForkable: false, hasApplicationSource: false, screenshots: [], relatedApps: [], forksAndRemixes: [] }
    const a = await mountPage({ ...base, catalogOrigin: { catalog: 'rzvdaniel/automaton-apps', appPath: 'wallpapers/harbour' } })
    expect(a.container.querySelector('.own-server')?.textContent).toContain('install Harbour from the Automaton catalog')
    a.vue.unmount()
    const b = await mountPage({ ...base, catalogOrigin: null })
    expect(b.container.querySelector('.own-server')).toBeNull()
    b.vue.unmount()
  })

  it('shows tags as chips, two-column facts and a caption for the selected screenshot', async () => {
    const { container, vue } = await mountPage({
      id: 'x', name: 'Harbour', canLaunch: false, canRemix: false, viewCount: 2, forkCount: 0, isForkable: false, category: 'Games',
      tags: 'Weather, Sound', hasApplicationSource: false, relatedApps: [], forksAndRemixes: [],
      screenshots: [
        { id: 'a', imageUrl: 'https://img.test/a.png', caption: 'Day', sortOrder: 1 },
        { id: 'b', imageUrl: 'https://img.test/b.png', caption: 'Night', sortOrder: 2 },
      ],
    })
    expect([...container.querySelectorAll('.feat')].map((e) => e.textContent)).toEqual(['Weather', 'Sound'])
    expect(container.querySelector('.facts')?.textContent).toContain('Games')
    expect(container.querySelector('.cap')?.textContent).toBe('1 / 2 · Day')
    container.querySelectorAll<HTMLButtonElement>('.thumb')[1]!.click()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(container.querySelector('.cap')?.textContent).toBe('2 / 2 · Night')
    vue.unmount()
  })
})
