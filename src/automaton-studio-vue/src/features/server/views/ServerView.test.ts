// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import ServerView from './ServerView.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import { installService } from '@/features/install/services/install.service'
import { serverHealthService } from '../services/server-health.service'
import { serverCleanupService } from '../services/server-cleanup.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { ServerCleanup, ServerHealth } from '../types/server.types'

const cleanupBody = (): ServerCleanup => ({
  settings: { timeUtc: '05:00', keepDays: 7, journalLimitMb: 200 },
  nextRunAtUtc: '2026-11-14T05:00:00Z',
  runs: [{
    id: 'c1', runnerName: 'runner-01', startedAtUtc: '2026-11-13T05:12:00Z', finishedAtUtc: '2026-11-13T05:13:00Z',
    result: 'Succeeded', bytesFreed: 1.8 * 1024 ** 3, trigger: 'Scheduled', message: null,
  }],
})

vi.mock('@/features/runners/services/runners.service', () => ({ runnersService: { list: vi.fn() } }))
vi.mock('@/features/install/services/install.service', () => ({ installService: { getUpdate: vi.fn(), startUpdate: vi.fn() } }))
vi.mock('../services/server-health.service', () => ({ serverHealthService: { get: vi.fn() } }))
vi.mock('../services/server-cleanup.service', () => ({
  serverCleanupService: { get: vi.fn(), saveSettings: vi.fn(), run: vi.fn() },
}))
vi.mock('@/features/runners/components/SelfHostedRunnerInstallDialog.vue', () => ({
  default: { props: ['visible'], template: '<div data-testid="install-dialog" :data-visible="visible" />' },
}))

const GIB = 1024 ** 3
const health = (updateAvailable: boolean): ServerHealth => ({
  generatedAtUtc: '2026-11-08T10:00:00Z',
  server: { version: 'server-v1', latestVersion: updateAvailable ? 'server-v2' : 'server-v1', updateAvailable },
  host: {
    measuredAtUtc: '2026-11-08T10:00:00Z', osName: 'Ubuntu', osVersion: '24.04', memoryTotalBytes: 8 * GIB, memoryAvailableBytes: 4 * GIB,
    diskTotalBytes: 100 * GIB, diskAvailableBytes: 50 * GIB, updatesPending: 3, securityUpdates: 1, rebootRequired: false,
  },
  hostAgeSeconds: 10,
  certificates: [{ host: 'studio.example.test', notAfterUtc: null, daysRemaining: 60, error: null }],
  backup: { available: true, lastSuccessAt: '2026-11-08T03:00:00Z', lastSuccessAgeHours: 7, lastError: null },
})
const runner = (overrides = {}) => ({
  id: 'r1', name: 'runner-01', status: 'Online', isHostedRunner: false, applicationProfile: 'MachineManagement',
  operatingSystem: 'Ubuntu 24.04', appCount: 3, version: '0.2.6', upgradeAvailable: false, ...overrides,
})

const mountView = (roles: string[] = ['User']) => {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = { roles } as never
  return mount(ServerView, {
    attachTo: document.body,
    global: { plugins: [pinia, PrimeVue, ConfirmationService, ToastService], directives: { tooltip: {} } },
  })
}

describe('ServerView', () => {
  beforeEach(() => {
    vi.mocked(runnersService.list).mockReset().mockResolvedValue([runner()] as never)
    vi.mocked(serverHealthService.get).mockReset().mockResolvedValue(health(false))
    vi.mocked(installService.getUpdate).mockReset().mockResolvedValue({
      currentVersion: 'server-v1', triggerAvailable: true, run: null, log: null, stale: false, requestPending: false, requestAgeSeconds: null, hint: null,
    })
    vi.mocked(installService.startUpdate).mockReset().mockResolvedValue()
    vi.mocked(serverCleanupService.get).mockReset().mockResolvedValue(cleanupBody())
    vi.mocked(serverCleanupService.saveSettings).mockReset().mockResolvedValue(cleanupBody())
    vi.mocked(serverCleanupService.run).mockReset().mockResolvedValue(cleanupBody())
  })

  it('shows the cleanup only to system administrators', async () => {
    for (const roles of [['User'], ['Admin']]) {
      const plain = mountView(roles)
      await flushPromises()
      expect(plain.find('[data-testid="cleanup-section"]').exists()).toBe(false)
      plain.unmount()
    }
    expect(serverCleanupService.get).not.toHaveBeenCalled()

    const wrapper = mountView(['Admin', 'SysAdmin'])
    await flushPromises()
    expect(wrapper.get('[data-testid="cleanup-last"]').text()).toContain('2026-11-13 05:12 UTC')
    expect(wrapper.get('[data-testid="cleanup-last"]').text()).toContain('freed 1.8 GB')
    expect(wrapper.get('[data-testid="cleanup-next"]').text()).toContain('2026-11-14 05:00 UTC')
    wrapper.unmount()
  })

  it('saves the settings and runs the cleanup now, then reloads', async () => {
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    await wrapper.get('[data-testid="cleanup-time"]').setValue('06:30')
    await wrapper.get('[data-testid="cleanup-section"] form').trigger('submit')
    await flushPromises()
    expect(serverCleanupService.saveSettings).toHaveBeenCalledWith({ timeUtc: '06:30', keepDays: 7, journalLimitMb: 200 })
    expect(serverCleanupService.get).toHaveBeenCalledTimes(2)

    await wrapper.get('[data-testid="cleanup-run"]').trigger('click')
    await flushPromises()
    expect(serverCleanupService.run).toHaveBeenCalled()
    expect(serverCleanupService.get).toHaveBeenCalledTimes(3)
    wrapper.unmount()
  })

  it('shows the server error inline when Run now fails', async () => {
    vi.mocked(serverCleanupService.run).mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { error: 'No Runner is online.' } } })
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    await wrapper.get('[data-testid="cleanup-run"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="cleanup-error"]').text()).toContain('No Runner is online.')
    wrapper.unmount()
  })

  it('shows the health tiles and each Runner with OS, apps and version', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.get('[data-testid="tile-disk"]').text()).toContain('50% used')
    expect(wrapper.get('[data-testid="tile-updates"]').text()).toContain('3 pending')
    expect(wrapper.get('[data-testid="tile-backup"]').text()).toContain('7 hours ago')
    expect(wrapper.get('[data-testid="runner-os"]').text()).toBe('Ubuntu 24.04')
    expect(wrapper.get('[data-testid="runner-apps"]').text()).toBe('3')
    expect(wrapper.get('[data-testid="runner-version"]').text()).toBe('0.2.6')
    expect(wrapper.find('[data-testid="update-available"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('offers an Update button only when a newer release exists and starts it after confirmation', async () => {
    vi.mocked(serverHealthService.get).mockResolvedValue(health(true))
    const wrapper = mountView()
    await flushPromises()
    const banner = wrapper.get('[data-testid="update-available"]')
    expect(banner.text()).toContain('server-v2')
    expect(banner.text()).toContain('server-v1')

    await banner.findAll('button').find((button) => button.text() === 'Update')!.trigger('click')
    expect(installService.startUpdate).not.toHaveBeenCalled()
    await wrapper.findAll('button').find((button) => button.text().includes('Update now'))!.trigger('click')
    await flushPromises()
    expect(installService.startUpdate).toHaveBeenCalledWith('latest')
    expect(wrapper.find('[data-testid="update-running"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('still lists Runners for an administrator who cannot read server health', async () => {
    vi.mocked(serverHealthService.get).mockRejectedValue({ isAxiosError: true, response: { status: 403 } })
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-testid="health-denied"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="health-tiles"]').exists()).toBe(false)
    expect(wrapper.findAll('[data-testid="runner-row"]')).toHaveLength(1)
    wrapper.unmount()
  })

  it('shows Add a Runner and an empty message when there are no Runners', async () => {
    vi.mocked(runnersService.list).mockResolvedValue([])
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-testid="runners-empty"]').exists()).toBe(true)
    await wrapper.get('[data-testid="add-runner"]').trigger('click')
    expect(wrapper.get('[data-testid="install-dialog"]').attributes('data-visible')).toBe('true')
    wrapper.unmount()
  })
})
