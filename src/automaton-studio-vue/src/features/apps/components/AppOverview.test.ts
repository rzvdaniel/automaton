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

const mountOverview = (extra: Partial<App>, more: Record<string, unknown> = {}) => mount(AppOverview, {
  props: {
    ...more,
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

  it('shows why the app was paused next to the Paused status, and nothing once it runs again', async () => {
    const paused = mountOverview({ isPaused: true, pausedReason: 'Sends spam' })
    await flushPromises()
    expect(paused.get('[data-testid="paused-reason"]').text()).toContain('Sends spam')
    const running = mountOverview({ isPaused: false, pausedReason: null })
    await flushPromises()
    expect(running.find('[data-testid="paused-reason"]').exists()).toBe(false)
  })

  it('shows the short failure line and keeps the full log behind Show details', async () => {
    const getUpdate = vi.mocked(appsService.getCatalogUpdate)
    getUpdate.mockResolvedValue({ available: true, installedCommit: 'a1', latestCommit: 'a1', edited: false, deployFailed: true, deployError: 'Build failed:\nTraceback (most recent call last):\nValueError: bad pin', newSecrets: [], newSettings: [], newPermissions: [] } as never)
    const wrapper = mountOverview({ installedFromRepo: 'o/apps' } as Partial<App>)
    await flushPromises()
    expect(wrapper.get('[data-testid="update-deploy-error"]').text()).toContain('Build failed: ValueError: bad pin')
    expect(wrapper.text()).not.toContain('Traceback')
    await wrapper.get('[data-testid="update-deploy-details-toggle"]').trigger('click')
    expect(wrapper.get('[data-testid="update-deploy-details"]').text()).toContain('Traceback (most recent call last)')
  })
})

describe('AppOverview waiting permissions', () => {
  it('shows a notice with Approve and Deny near the top for an administrator', async () => {
    const wrapper = mountOverview({}, { waitingPermissions: ['notify:email'] })
    await flushPromises()
    const notice = wrapper.get('[data-testid="permission-notice"]')
    expect(notice.text()).toContain('notify:email')
    await notice.get('[aria-label="Approve notify:email"]').trigger('click')
    await notice.get('[aria-label="Deny notify:email"]').trigger('click')
    expect(wrapper.emitted('decide-permission')).toEqual([['notify:email', true], ['notify:email', false]])
  })

  it('shows no notice or buttons when nothing is given (owner without admin rights)', async () => {
    const wrapper = mountOverview({})
    await flushPromises()
    expect(wrapper.find('[data-testid="permission-notice"]').exists()).toBe(false)
  })
})
