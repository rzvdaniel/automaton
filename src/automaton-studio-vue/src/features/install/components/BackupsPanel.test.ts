// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import BackupsPanel from './BackupsPanel.vue'
import { installService } from '../services/install.service'

import { appsService } from '@/features/apps/services/apps.service'

vi.mock('../services/install.service', () => ({ installService: { getBackups: vi.fn(), startBackup: vi.fn() } }))
vi.mock('@/features/apps/services/apps.service', () => ({ appsService: { getApps: vi.fn(), getCatalog: vi.fn() } }))

const RouterLinkStub = { props: ['to'], template: '<a :data-to="JSON.stringify(to)"><slot /></a>' }
const mountPanel = () => mount(BackupsPanel, { global: { plugins: [PrimeVue], stubs: { RouterLink: RouterLinkStub } } })

const catalog = (apps: unknown[] = []) => vi.mocked(appsService.getCatalog).mockResolvedValue({ apps } as never)

describe('BackupsPanel', () => {
  beforeEach(() => { vi.mocked(installService.getBackups).mockReset(); vi.mocked(appsService.getCatalog).mockReset(); catalog() })

  it('shows the last, next and offsite tiles and links each app to its restore view', async () => {
    vi.mocked(appsService.getApps).mockResolvedValueOnce([
      { id: 'a1', name: 'Nightly copy to S3' }, { id: 'a2', name: 'Notes' },
    ] as never)
    catalog([{ id: 'offsite-backup', installedAppId: 'a1' }])
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true, lastAttemptAt: new Date().toISOString(), lastSuccessAt: new Date().toISOString(), lastError: null,
      keep: 7, directory: null, backupNowCommand: 'sudo bash backup.sh', backupRecipient: null, backupKeyCommand: '', backups: [],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-test=tile-last]').text()).toContain('Less than an hour ago')
    expect(wrapper.get('[data-test=tile-next]').text()).toContain('every night')
    expect(wrapper.get('[data-test=tile-offsite]').text()).toContain('Nightly copy to S3')
    expect(wrapper.get('[data-test=app-restore]').html()).toContain('/apps/a2')
    expect(wrapper.get('[data-test=app-restore]').html()).toContain('settings')
  })

  it('offers the catalog when the Offsite Backup app is not installed', async () => {
    vi.mocked(appsService.getApps).mockResolvedValueOnce([])
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true, lastAttemptAt: null, lastSuccessAt: null, lastError: null,
      keep: 7, directory: null, backupNowCommand: 'x', backupRecipient: null, backupKeyCommand: '', backups: [],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-test=tile-offsite]').text()).toContain('Not set up')
    expect(wrapper.find('[data-test=app-restore]').exists()).toBe(false)
  })

  it('lists backups with the command to restore each one on the server', async () => {
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true,
      lastAttemptAt: new Date().toISOString(),
      lastSuccessAt: new Date().toISOString(),
      lastError: null,
      keep: 7,
      directory: '/opt/automaton-server/backups/nightly',
      backupNowCommand: 'sudo bash /opt/automaton-server/backup.sh',
      backupRecipient: 'age1recipient',
      backupKeyCommand: 'sudo cat /etc/automaton/secrets/backup-key',
      backups: [{
        name: 'automaton-20261101T031000Z',
        createdAt: '2026-11-01T03:10:00Z',
        sizeBytes: 2 * 1024 * 1024,
        restoreCommand: 'sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z',
        archiveSizeBytes: 1024,
        archiveSha256: 'abcd',
      }],
    })
    vi.mocked(appsService.getApps).mockResolvedValueOnce([])
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z')
    expect(wrapper.text()).toContain('2.0 MB')
    expect(wrapper.text()).toContain('sudo bash /opt/automaton-server/backup.sh')
    expect(wrapper.text()).toContain('age1recipient')
    expect(wrapper.text()).toContain('sudo cat /etc/automaton/secrets/backup-key')
    expect(wrapper.text()).not.toContain('older than')
  })

  it('explains how to enable backups when the server has none set up', async () => {
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: false, lastAttemptAt: null, lastSuccessAt: null, lastError: null, keep: 0, directory: null,
      backupNowCommand: 'sudo bash /opt/automaton-server/backup.sh', backups: [],
      backupRecipient: null, backupKeyCommand: 'sudo cat /etc/automaton/secrets/backup-key',
    })
    vi.mocked(appsService.getApps).mockResolvedValueOnce([])
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('Nightly backups are not set up on this server yet')
  })

  it('does not match the Offsite Backup app by its name', async () => {
    vi.mocked(appsService.getApps).mockResolvedValueOnce([{ id: 'a1', name: 'Offsite Backup' }] as never)
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true, lastAttemptAt: null, lastSuccessAt: null, lastError: null,
      keep: 7, directory: null, backupNowCommand: 'x', backupRecipient: null, backupKeyCommand: '', backups: [],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-test=tile-offsite]').text()).toContain('Not set up')
  })

  it('shows the real next backup time and starts a backup from the button', async () => {
    vi.useFakeTimers()
    vi.mocked(appsService.getApps).mockResolvedValue([])
    const base = {
      available: true, lastSuccessAt: '2026-11-01T03:10:00Z', lastError: null, keep: 7, directory: null,
      backupNowCommand: 'x', backupRecipient: null, backupKeyCommand: '', backups: [],
      nextRunAt: '2099-01-02T03:25:00Z', triggerAvailable: true,
    }
    vi.mocked(installService.getBackups)
      .mockResolvedValueOnce({ ...base, lastAttemptAt: '2026-11-01T03:10:00Z' })
      .mockResolvedValue({ ...base, lastAttemptAt: '2026-11-02T10:00:00Z' })
    vi.mocked(installService.startBackup).mockResolvedValue()
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-test=tile-next]').text()).toContain(new Date('2099-01-02T03:25:00Z').toLocaleString())
    expect(wrapper.get('[data-test=tile-next]').text()).not.toContain('About')
    await wrapper.get('[data-test=back-up-now]').trigger('click')
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()

    expect(installService.startBackup).toHaveBeenCalledTimes(1)
    expect(installService.getBackups).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test=backup-requested]').exists()).toBe(false)
    vi.useRealTimers()
  })

  it('falls back to the command when the server cannot start backups', async () => {
    vi.mocked(appsService.getApps).mockResolvedValueOnce([])
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true, lastAttemptAt: null, lastSuccessAt: null, lastError: null, keep: 7, directory: null,
      backupNowCommand: 'sudo bash backup.sh', backupRecipient: null, backupKeyCommand: '', backups: [], triggerAvailable: false,
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-test=back-up-now]').exists()).toBe(false)
    expect(wrapper.text()).toContain('sudo bash backup.sh')
  })
})
