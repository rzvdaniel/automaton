import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppsView from './AppsView.vue'

const host = vi.hoisted(() => ({ value: '' }))

vi.mock('@/config/resolveStoreConfig', async (orig) => ({
  ...(await orig<object>()),
  serverHostName: () => host.value,
}))
vi.mock('@/config/publicAppConfig', () => ({
  publicAppConfig: { apiBaseUrl: 'https://api.test/', studioUrl: 'https://studio.test/', storeTitle: 'Store' },
  getPublishedAppUrl: (url: string) => `https://apps.test/${url}`,
}))

async function heading(hostName: string) {
  host.value = hostName
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => [] })))
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:p(.*)*', component: AppsView }] })
  await router.push('/')
  const container = document.createElement('div')
  const vue = createApp({ render: () => h(AppsView) })
  vue.use(router)
  vue.mount(container)
  await new Promise((resolve) => setTimeout(resolve, 20))
  const text = container.querySelector('h1')?.textContent?.replace(/\s+/g, ' ').trim()
  vue.unmount()
  return text
}

afterEach(() => vi.unstubAllGlobals())

describe('AppsView headline', () => {
  it('separates the host name from the text before it', async () => {
    expect(await heading('automaton.monster')).toBe('Apps running on automaton.monster')
  })

  it('says this server without a host', async () => {
    expect(await heading('')).toBe('Apps running on this server')
  })
})
