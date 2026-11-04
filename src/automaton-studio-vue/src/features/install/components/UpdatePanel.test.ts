// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import UpdatePanel from './UpdatePanel.vue'
import { installService } from '../services/install.service'
import type { InstallationUpdateStatus } from '../types/install.types'

vi.mock('../services/install.service', () => ({
  installService: { getUpdate: vi.fn(), startUpdate: vi.fn() },
}))

const idle: InstallationUpdateStatus = { currentVersion: 'server-v0.1.1', triggerAvailable: true, run: null, log: null }
const run = (state: 'running' | 'succeeded' | 'failed', extra = {}): InstallationUpdateStatus => ({
  ...idle,
  run: {
    state, requestedVersion: 'latest', fromVersion: 'server-v0.1.1', toVersion: null, startedAtUtc: '2026-11-04T10:00:00Z',
    finishedAtUtc: null, exitCode: null, recoveryBackup: null, requestedBy: 'admin@example.com', message: null, ...extra,
  },
  log: 'pulling images',
})

const mountPanel = () => mount(UpdatePanel, { attachTo: document.body, global: { plugins: [PrimeVue] } })
const button = (wrapper: ReturnType<typeof mountPanel>, label: string) =>
  wrapper.findAll('button').find((candidate) => candidate.text().includes(label))

describe('UpdatePanel', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.mocked(installService.getUpdate).mockReset()
    vi.mocked(installService.startUpdate).mockReset()
  })
  afterEach(() => vi.useRealTimers())

  it('shows the version and asks for confirmation before starting an update', async () => {
    vi.mocked(installService.getUpdate).mockResolvedValue(idle)
    vi.mocked(installService.startUpdate).mockResolvedValue()
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('server-v0.1.1')

    await button(wrapper, 'Update to the latest release')!.trigger('click')
    expect(installService.startUpdate).not.toHaveBeenCalled()
    await button(wrapper, 'Update now')!.trigger('click')
    await flushPromises()

    expect(installService.startUpdate).toHaveBeenCalledWith('latest')
    wrapper.unmount()
  })

  it('polls while an update runs, survives the API restarting, and shows the result', async () => {
    vi.mocked(installService.getUpdate)
      .mockResolvedValueOnce(run('running'))
      .mockRejectedValueOnce(new Error('bad gateway'))
      .mockResolvedValueOnce(run('succeeded', { toVersion: 'server-v0.2.0', recoveryBackup: '/opt/automaton-server/backups/b1' }))
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('pulling images')

    await vi.advanceTimersByTimeAsync(3000)
    expect(wrapper.text()).toContain('Waiting for the server to restart')

    await vi.advanceTimersByTimeAsync(3000)
    expect(wrapper.text()).toContain('The server runs server-v0.2.0')
    expect(wrapper.text()).toContain('/opt/automaton-server/backups/b1')
    expect(installService.getUpdate).toHaveBeenCalledTimes(3)
    await vi.advanceTimersByTimeAsync(9000)
    expect(installService.getUpdate).toHaveBeenCalledTimes(3)
    wrapper.unmount()
  })

  it('shows a failed run with the recovery backup and the server message when starting is refused', async () => {
    vi.mocked(installService.getUpdate).mockResolvedValue(run('failed', { message: 'The update did not finish.', recoveryBackup: '/opt/automaton-server/backups/b2' }))
    vi.mocked(installService.startUpdate).mockRejectedValue({ response: { data: { message: 'An update is already running.' } } })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('/opt/automaton-server/backups/b2')

    await button(wrapper, 'Update to the latest release')!.trigger('click')
    await button(wrapper, 'Update now')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('An update is already running.')
    wrapper.unmount()
  })

  it('explains how to enable updates on a server without the trigger', async () => {
    vi.mocked(installService.getUpdate).mockResolvedValue({ ...idle, triggerAvailable: false })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('cannot update itself from Studio yet')
    expect(button(wrapper, 'Update to the latest release')!.attributes('disabled')).toBeDefined()
    wrapper.unmount()
  })
})
