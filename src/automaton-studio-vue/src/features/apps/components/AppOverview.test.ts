// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppOverview from './AppOverview.vue'
import type { App } from '../types/apps.types'

vi.mock('../services/apps.service', () => ({
  appsService: new Proxy({}, { get: () => vi.fn().mockResolvedValue([]) }),
}))

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
