// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppOverview from './AppOverview.vue'
import type { App } from '../types/apps.types'

const spies = vi.hoisted(() => new Map<string | symbol, ReturnType<typeof vi.fn>>())
vi.mock('../services/apps.service', () => ({
  appsService: new Proxy({}, {
    get: (_, key) => {
      if (!spies.has(key)) spies.set(key, vi.fn().mockResolvedValue([]))
      return spies.get(key)
    },
  }),
}))
import { appsService } from '../services/apps.service'

const mountOverview = (extra: Partial<App>) => mount(AppOverview, {
  props: {
    app: { id: 'app-1', name: 'Backup', isPaused: false, canUpdate: true, ...extra } as App,
    targets: [], executions: [], schedules: [], actions: [], address: '', publicAddress: '', privateAddress: '', sharing: null,
  },
  global: { plugins: [PrimeVue, ToastService], stubs: { RouterLink: true } },
})

describe('AppOverview restart', () => {
  it('offers Restart for an app with a container', async () => {
    const wrapper = mountOverview({})
    await flushPromises()
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="pause-card"]').text()).toContain('Restart loads changed settings')
  })

  it('offers no Restart for an Actions-only app but keeps Pause', async () => {
    const wrapper = mountOverview({ actionsOnly: true })
    await flushPromises()
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="pause-card"]').text()).toContain('apply on the next run')
    expect(wrapper.get('[data-testid="pause-card"]').text()).toContain('Pause')
  })
})

describe('AppOverview catalog update', () => {
  it('asks for an update even when no install source is recorded yet (#1080)', async () => {
    const getUpdate = vi.mocked(appsService.getCatalogUpdate)
    getUpdate.mockClear()
    getUpdate.mockResolvedValue({ available: false } as never)
    mountOverview({ installedFromRepo: null } as Partial<App>)
    await flushPromises()
    expect(getUpdate).toHaveBeenCalledWith('app-1')
  })

  it('does not ask when the viewer cannot update the app', async () => {
    const getUpdate = vi.mocked(appsService.getCatalogUpdate)
    getUpdate.mockClear()
    mountOverview({ canUpdate: false })
    await flushPromises()
    expect(getUpdate).not.toHaveBeenCalled()
  })
})
