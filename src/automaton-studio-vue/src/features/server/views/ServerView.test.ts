// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import ServerView from './ServerView.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import { serverConsoleService } from '../services/server-console.service'
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
  }, {
    id: 'c2', runnerName: 'Renamed Twice', startedAtUtc: '2026-11-13T05:12:00Z', finishedAtUtc: '2026-11-13T05:12:01Z',
    result: 'Failed', bytesFreed: 0, trigger: 'Scheduled', message: 'Runner too old',
  }],
})

vi.mock('@/features/runners/services/runners.service', () => ({ runnersService: { list: vi.fn() } }))
vi.mock('@/features/install/services/install.service', () => ({ installService: { getUpdate: vi.fn(), startUpdate: vi.fn() } }))
vi.mock('../services/server-console.service', () => ({ serverConsoleService: { get: vi.fn(), install: vi.fn() } }))
vi.mock('../services/server-health.service', () => ({ serverHealthService: { get: vi.fn() } }))
vi.mock('../services/server-cleanup.service', () => ({
  serverCleanupService: { get: vi.fn(), saveSettings: vi.fn(), run: vi.fn() },
}))
vi.mock('@/features/runners/components/SelfHostedRunnerInstallDialog.vue', () => ({
  default: { props: ['visible'], template: '<div data-testid="install-dialog" :data-visible="visible" />' },
}))

const route = vi.hoisted(() => ({ params: {} as Record<string, string> }))
vi.mock('vue-router', () => ({ useRoute: () => route }))

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

const mountView = (roles: string[] = ['User'], tab?: 'runners') => {
  route.params = tab ? { tab } : {}
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = { roles } as never
  return mount(ServerView, {
    attachTo: document.body,
    global: { plugins: [pinia, PrimeVue, ConfirmationService, ToastService], directives: { tooltip: {} }, stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } },
  })
}

describe('ServerView', () => {
  beforeEach(() => {
    route.params = {}
    vi.mocked(runnersService.list).mockReset().mockResolvedValue([runner()] as never)
    vi.mocked(serverConsoleService.install).mockReset().mockResolvedValue({ appId: 'sc1', running: false })
    vi.mocked(serverConsoleService.get).mockReset().mockResolvedValue({ appId: null, running: false })
    vi.mocked(serverHealthService.get).mockReset().mockResolvedValue(health(false))
    vi.mocked(installService.getUpdate).mockReset().mockResolvedValue({
      currentVersion: 'server-v1', triggerAvailable: true, run: null, log: null, stale: false, requestPending: false, requestAgeSeconds: null, hint: null,
    })
    vi.mocked(installService.startUpdate).mockReset().mockResolvedValue()
    vi.mocked(serverCleanupService.get).mockReset().mockResolvedValue(cleanupBody())
    vi.mocked(serverCleanupService.saveSettings).mockReset().mockResolvedValue(cleanupBody())
    vi.mocked(serverCleanupService.run).mockReset().mockResolvedValue(cleanupBody())
  })

  it('shows the main disk and the Docker disk on their own lines and flags the low one', async () => {
    vi.mocked(serverHealthService.get).mockResolvedValue({ ...health(false), host: { ...health(false).host!, disks: [
      { mountPoint: '/', totalBytes: 38 * GIB, availableBytes: 8 * GIB }, { mountPoint: '/mnt/docker-data', totalBytes: 40 * GIB, availableBytes: 29 * GIB }] } } as never)
    const wrapper = mountView(['Admin', 'SysAdmin'])
    await flushPromises()
    const rows = wrapper.findAll('[data-testid="health-disk"]').map((row) => row.text())
    expect(rows).toHaveLength(2)
    expect(rows[0]!).toContain('30 GB of 38 GB')
    expect(rows[0]!).toContain('low')
    expect(rows[0]!).toContain('an update needs 10 GB free')
    expect(rows[1]!).toContain('11 GB of 40 GB')
    expect(rows[1]!).not.toContain('low')
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
    expect(wrapper.get('[data-testid="cleanup-line"]').text()).toContain('nightly at 05:00')
    expect(wrapper.get('[data-testid="cleanup-last"]').text()).toBe('last run freed 1.8 GB')
    expect(wrapper.find('[data-testid="cleanup-line"]').classes()).not.toContain('health-row__bad')
    expect(wrapper.find('[data-testid="cleanup-run"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="cleanup-save"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('links the cleanup line to Server Console when it is installed', async () => {
    vi.mocked(serverConsoleService.get).mockResolvedValue({ appId: 'sc1' } as never)
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    expect(wrapper.get('[data-testid="cleanup-manage"]').attributes('href')).toBe('/apps/sc1')
    expect(wrapper.find('[data-testid="cleanup-install"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('turns the cleanup line red with the error when the last run failed', async () => {
    const body = cleanupBody()
    body.runs = [body.runs[1]!]
    vi.mocked(serverCleanupService.get).mockResolvedValue(body)
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    expect(wrapper.get('[data-testid="cleanup-line"]').classes()).toContain('health-row__bad')
    expect(wrapper.get('[data-testid="cleanup-last"]').text()).toContain('Runner too old')
    wrapper.unmount()
  })

  it('offers Install Server Console once, not on the cleanup line', async () => {
    vi.mocked(serverConsoleService.get).mockRejectedValue(new Error('none'))
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    expect(wrapper.find('[data-testid="cleanup-manage"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="cleanup-install"]').exists()).toBe(false)
    expect(wrapper.findAll('button').filter((button) => button.text() === 'Install Server Keeper')).toHaveLength(1)
    wrapper.unmount()
  })

  it('opens the Automaton tab at /server and the Runners tab at /server/runners, with the count in the label', async () => {
    const automaton = mountView()
    await flushPromises()
    expect(automaton.find('[data-testid="version-card"]').exists()).toBe(true)
    expect(automaton.find('[data-testid="runner-row"]').exists()).toBe(false)
    expect(automaton.get('[data-testid="tab-automaton"]').attributes('href')).toBe('/server')
    expect(automaton.get('[data-testid="tab-runners"]').attributes('href')).toBe('/server/runners')
    expect(automaton.get('[data-testid="tab-runners"]').text()).toBe('Runners1')
    expect(automaton.text()).toContain('Automaton server-v1')
    automaton.unmount()
    const runners = mountView([], 'runners')
    await flushPromises()
    expect(runners.find('[data-testid="version-card"]').exists()).toBe(false)
    expect(runners.findAll('[data-testid="runner-row"]')).toHaveLength(1)
    runners.unmount()
  })

  it('always shows the version card, and says up to date', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.get('[data-testid="version-card"]').text()).toContain('server-v1')
    expect(wrapper.get('[data-testid="version-card"]').text()).toContain('Up to date')
    expect(wrapper.find('[data-testid="update-available"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('shows no error boxes while updating and a success message afterwards', async () => {
    vi.useFakeTimers()
    try {
      vi.mocked(serverHealthService.get).mockResolvedValue(health(true))
      const wrapper = mountView()
      await flushPromises()
      await wrapper.get('[data-testid="update-available"]').findAll('button').find((b) => b.text().startsWith('Update Automaton to'))!.trigger('click')
      await wrapper.findAll('button').find((b) => b.text().includes('Update now'))!.trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="update-running"]').exists()).toBe(true)

      vi.mocked(serverHealthService.get).mockRejectedValue(new Error('restarting'))
      vi.mocked(runnersService.list).mockRejectedValue(new Error('restarting'))
      await vi.advanceTimersByTimeAsync(15000)
      expect(wrapper.text()).not.toContain('could not be loaded')
      expect(wrapper.text()).toContain('server-v1')

      vi.mocked(serverHealthService.get).mockResolvedValue({ ...health(false), server: { version: 'server-v2', latestVersion: 'server-v2', updateAvailable: false } })
      vi.mocked(runnersService.list).mockResolvedValue([runner()] as never)
      vi.mocked(installService.getUpdate).mockResolvedValue({
        currentVersion: 'server-v2', triggerAvailable: true, stale: false, requestPending: false, requestAgeSeconds: null, hint: null, log: null,
        run: { state: 'succeeded', requestedVersion: 'latest', fromVersion: 'server-v1', toVersion: 'server-v2', startedAtUtc: '2026-11-08T10:00:00Z', finishedAtUtc: '2026-11-08T10:05:00Z', exitCode: 0, recoveryBackup: null, requestedBy: null, message: null },
      })
      await vi.advanceTimersByTimeAsync(3000)
      await flushPromises()
      expect(wrapper.get('[data-testid="update-succeeded"]').text()).toContain('Automaton updated to 2')
      expect(wrapper.find('[data-testid="runners-updating"]').exists()).toBe(false)
      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('shows each Runner with OS, apps and version and no health cards', async () => {
    const wrapper = mountView([], 'runners')
    await flushPromises()
    expect(wrapper.find('[data-testid="health-tiles"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="host-os"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="runner-os"]').text()).toBe('Ubuntu 24.04')
    expect(wrapper.get('[data-testid="runner-apps"]').text()).toBe('3')
    expect(wrapper.get('[data-testid="runner-version"]').text()).toBe('0.2.6')
    expect(wrapper.find('[data-testid="update-available"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="server-console-link"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="server-console-notice"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('links a system administrator to Server Console, or says how to install it', async () => {
    const missing = mountView(['SysAdmin'])
    await flushPromises()
    expect(missing.get('[data-testid="server-console-notice"]').text()).toContain('Install Server Keeper')
    expect(serverConsoleService.install).not.toHaveBeenCalled()
    vi.mocked(serverConsoleService.get).mockResolvedValue({ appId: 'sc1', running: false })
    await missing.get('[data-testid="server-console-install"]').trigger('click')
    await flushPromises()
    expect(serverConsoleService.install).toHaveBeenCalledTimes(1)
    expect(missing.get('[data-testid="server-console-open"]').attributes('href')).toBe('/apps/sc1')
    expect(missing.find('[data-testid="server-console-install"]').exists()).toBe(false)
    missing.unmount()
    vi.mocked(serverConsoleService.get).mockResolvedValue({ appId: null, running: false })

    vi.mocked(serverConsoleService.get).mockResolvedValue({ appId: 'sc1', running: false })
    const installed = mountView(['SysAdmin'])
    await flushPromises()
    expect(installed.get('[data-testid="server-console-open"]').attributes('href')).toBe('/apps/sc1')
    expect(installed.find('[data-testid="server-console-install"]').exists()).toBe(false)
    installed.unmount()
  })

  it('says why Server Console could not be installed', async () => {
    vi.mocked(serverConsoleService.install).mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { error: 'Server Console is not available in the official catalog.' } } })
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    await wrapper.get('[data-testid="server-console-install"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="server-console-install-error"]').text()).toContain('not available')
    wrapper.unmount()
  })

  it('offers an Update button only when a newer release exists and starts it after confirmation', async () => {
    vi.mocked(serverHealthService.get).mockResolvedValue(health(true))
    const wrapper = mountView()
    await flushPromises()
    const banner = wrapper.get('[data-testid="update-available"]')
    expect(banner.text()).toContain('Update Automaton to server-v2')
    expect(banner.text()).toContain('about 2 minutes')
    expect(wrapper.get('[data-testid="version-card"]').text()).toContain('server-v1')
    expect(wrapper.get('[data-testid="update-available-pill"]').text()).toBe('server-v2 available')

    await banner.findAll('button').find((button) => button.text().startsWith('Update Automaton to'))!.trigger('click')
    expect(installService.startUpdate).not.toHaveBeenCalled()
    await wrapper.findAll('button').find((button) => button.text().includes('Update now'))!.trigger('click')
    await flushPromises()
    expect(installService.startUpdate).toHaveBeenCalledWith('latest')
    expect(wrapper.find('[data-testid="update-running"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('still lists Runners for an administrator who cannot read server health', async () => {
    vi.mocked(serverHealthService.get).mockRejectedValue({ isAxiosError: true, response: { status: 403 } })
    const wrapper = mountView([], 'runners')
    await flushPromises()
    expect(wrapper.find('[data-testid="health-denied"]').exists()).toBe(false)
    expect(wrapper.findAll('[data-testid="runner-row"]')).toHaveLength(1)
    wrapper.unmount()
    const automaton = mountView()
    await flushPromises()
    expect(automaton.find('[data-testid="health-denied"]').exists()).toBe(true)
    automaton.unmount()
  })

  it('shows Add a Runner and an empty message when there are no Runners', async () => {
    vi.mocked(runnersService.list).mockResolvedValue([])
    const wrapper = mountView([], 'runners')
    await flushPromises()
    expect(wrapper.find('[data-testid="runners-empty"]').exists()).toBe(true)
    await wrapper.get('[data-testid="add-runner"]').trigger('click')
    expect(wrapper.get('[data-testid="install-dialog"]').attributes('data-visible')).toBe('true')
    wrapper.unmount()
  })

  const runStatus = (run: object, extra = {}) => ({
    currentVersion: 'server-v1', triggerAvailable: true, stale: false, requestPending: false, requestAgeSeconds: null, hint: null, log: 'line',
    run: { requestedVersion: 'latest', fromVersion: 'server-v1', toVersion: null, startedAtUtc: new Date().toISOString(), finishedAtUtc: null, exitCode: null, recoveryBackup: null, requestedBy: 'admin@example.com', message: null, ...run },
    ...extra,
  })

  it('shows the update steps advancing, who started it, and a progress bar, without error boxes', async () => {
    vi.mocked(serverHealthService.get).mockResolvedValue(health(true))
    vi.mocked(installService.getUpdate).mockResolvedValue(runStatus({ state: 'running', step: 'restart' }) as never)
    const wrapper = mountView()
    await flushPromises()
    const steps = wrapper.findAll('[data-testid="update-steps"] li')
    expect(steps.map((step) => step.text())).toEqual(['Backup', 'Download', 'Restart services', 'Health check'])
    expect(steps[2]!.classes()).toContain('is-current')
    expect(steps[0]!.classes()).toContain('is-done')
    expect(wrapper.get('[data-testid="update-running"]').text()).toContain('Started by admin@example.com')
    expect(wrapper.text()).not.toContain('could not be loaded')
    wrapper.unmount()
  })

  it('names the step where a failed update stopped and offers Try again', async () => {
    vi.mocked(serverHealthService.get).mockResolvedValue(health(true))
    vi.mocked(installService.getUpdate).mockResolvedValue(runStatus({ state: 'failed', step: 'health', message: 'Reason: no answer' }) as never)
    const wrapper = mountView()
    await flushPromises()
    const failed = wrapper.get('[data-testid="update-failed"]')
    expect(failed.text()).toContain('Stopped at Health check')
    expect(failed.text()).toContain('Reason: no answer')
    expect(failed.text()).toContain('still runs server-v1')
    await wrapper.get('[data-testid="update-retry"]').trigger('click')
    await flushPromises()
    expect(installService.startUpdate).toHaveBeenCalledWith('latest')
    wrapper.unmount()
  })

  it('does not show an old finished update as "Updated" when the page opens', async () => {
    vi.mocked(installService.getUpdate).mockResolvedValue(runStatus({ state: 'succeeded', finishedAtUtc: new Date().toISOString() }) as never)
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-testid="update-succeeded"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('keeps the last values and shows one line with Retry when the API does not answer', async () => {
    const wrapper = mountView(['SysAdmin'])
    await flushPromises()
    vi.mocked(serverHealthService.get).mockRejectedValue(new Error('down'))
    vi.mocked(runnersService.list).mockRejectedValue(new Error('down'))
    await wrapper.get('[data-testid="check-now"]').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('[data-testid="server-notice"]')).toHaveLength(1)
    expect(wrapper.get('[data-testid="server-notice"]').text()).toContain('showing')
    expect(wrapper.get('[data-testid="health-paused"]').text()).toContain('could not refresh')
    expect(wrapper.text()).not.toContain('could not be loaded')
    wrapper.unmount()
  })

  const fleet = () => [
    runner({ id: 'a', name: 'alpha', upgradeAvailable: true }),
    runner({ id: 'b', name: 'bravo', status: 'Offline' }),
    runner({ id: 'c', name: 'charlie', upgradeAvailable: true }),
  ]

  it('summarises Runners and filters and searches them when there is more than one', async () => {
    vi.mocked(runnersService.list).mockResolvedValue(fleet() as never)
    const wrapper = mountView([], 'runners')
    await flushPromises()
    expect(wrapper.get('[data-testid="runners-summary"]').text()).toBe('3 runners · 2 online · 1 offline · 2 need upgrade')
    expect(wrapper.findAll('[data-testid="runner-row"]').map((row) => row.text().split(' ')[0])).toEqual(['alpha', 'bravo', 'charlie'])
    await wrapper.get('[data-testid="runners-filter-offline"]').trigger('click')
    expect(wrapper.findAll('[data-testid="runner-row"]')).toHaveLength(1)
    await wrapper.get('[data-testid="runners-filter-all"]').trigger('click')
    await wrapper.get('[data-testid="runners-search"]').setValue('char')
    expect(wrapper.findAll('[data-testid="runner-row"]')).toHaveLength(1)
    expect(wrapper.get('[data-testid="runner-state"]').text()).toBe('Needs upgrade')
    wrapper.unmount()
  })

  it('shows no filters for a single Runner and offers Upgrade N runners only for two or more', async () => {
    const single = mountView([], 'runners')
    await flushPromises()
    expect(single.find('[data-testid="runners-search"]').exists()).toBe(false)
    expect(single.find('[data-testid="runners-upgrade-all"]').exists()).toBe(false)
    single.unmount()
    vi.mocked(runnersService.list).mockResolvedValue(fleet() as never)
    const many = mountView([], 'runners')
    await flushPromises()
    expect(many.get('[data-testid="runners-upgrade-all"]').text()).toBe('Upgrade 2 runners')
    expect(many.findAll('[data-testid="runner-upgrade-button"]')).toHaveLength(2)
    many.unmount()
  })
})
