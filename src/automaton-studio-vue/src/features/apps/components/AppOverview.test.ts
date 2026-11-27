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

describe('AppOverview layout', () => {
  it('no longer carries the Restart/Pause card, Addresses, Permissions or What it needs', async () => {
    const wrapper = mountOverview({})
    await flushPromises()
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="pause-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="addresses"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="permissions-card"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('What it needs')
    expect(wrapper.get('[data-testid="installed-from"]').text()).toContain('Deployed with the CLI')
  })

  it('shows the Disk card with the app total and the disk it lives on', async () => {
    vi.mocked(appsService.getDisk).mockResolvedValue({ appId: 'app-1', totalBytes: 244 * 1024 ** 2, imageBytes: 143 * 1024 ** 2, filesBytes: 101 * 1024 ** 2, hostDiskTotalBytes: 39 * 1024 ** 3, hostDiskAvailableBytes: 32 * 1024 ** 3 } as never)
    const wrapper = mountOverview({})
    await flushPromises()
    expect(wrapper.get('[data-testid="disk-total"]').text()).toBe('244 MB')
    expect(wrapper.get('[data-testid="host-disk"]').text()).toContain('warning at 80%')
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

describe('AppOverview never-deployed app (#1844)', () => {
  const notDeployed = [{ id: 't', isDefault: true, status: 'NotDeployed' }]
  const withSecret = { application: { secrets: [{ name: 'HEAD_COUNTER_TOKEN' }, { name: 'OPT', optional: true }] } } as Partial<App>
  const mountNotDeployed = () => (vi.mocked(appsService.getCatalogUpdate).mockResolvedValue({ available: false } as never), mount(AppOverview, {
    props: { app: { id: 'app-1', name: 'Head', isPaused: false, canUpdate: true, ...withSecret } as App, targets: notDeployed as never, executions: [], schedules: [], actions: [], address: '', sharing: null },
    global: { plugins: [PrimeVue, ToastService], stubs: { RouterLink: true } },
  }))

  it('disables Deploy with the reason while a required secret is missing', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    const wrapper = mountNotDeployed()
    await flushPromises()
    expect(wrapper.get('[data-testid="deploy-blocked"]').text()).toContain('HEAD_COUNTER_TOKEN')
    expect(wrapper.get('[data-testid="deploy-button"]').attributes('disabled')).toBeDefined()
  })

  it('deploys the stored source once the secret is saved', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([{ name: 'HEAD_COUNTER_TOKEN', version: 1, updatedAtUtc: '' }])
    vi.mocked(appsService.deployStoredSource).mockResolvedValue({ operationId: 'op', status: 'Pending' } as never)
    vi.mocked(appsService.getDeploymentOperation).mockResolvedValue({ status: 'Succeeded' } as never)
    const wrapper = mountNotDeployed()
    await flushPromises()
    expect(wrapper.find('[data-testid="deploy-blocked"]').exists()).toBe(false)
    await wrapper.get('[data-testid="deploy-button"]').trigger('click')
    await flushPromises()
    expect(appsService.deployStoredSource).toHaveBeenCalledWith('app-1')
    expect(wrapper.emitted('targets-changed')).toBeTruthy()
  })

  it('asks for the secrets an update adds and saves them before updating', async () => {
    vi.mocked(appsService.getCatalogUpdate).mockResolvedValue({ available: true, installedCommit: 'a', latestCommit: 'b', edited: false, newSecrets: ['AGENT_GH_TOKEN'], newSettings: [], newPermissions: [] } as never)
    vi.mocked(appsService.applyCatalogUpdate).mockResolvedValue({ operationId: 'op', status: 'Pending' } as never)
    const wrapper = mountOverview({})
    await flushPromises()
    await wrapper.get('[data-testid="update-secret-AGENT_GH_TOKEN"]').setValue('v')
    await wrapper.findAll('button').find((b) => b.text() === 'Update')!.trigger('click')
    await flushPromises()
    expect(appsService.setSecret).toHaveBeenCalledWith('app-1', 'AGENT_GH_TOKEN', 'v')
    expect(appsService.applyCatalogUpdate).toHaveBeenCalled()
  })
})
