// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import BackupsPanel from './BackupsPanel.vue'
import { installService } from '../services/install.service'

import { appsService } from '@/features/apps/services/apps.service'

vi.mock('../services/install.service', () => ({ installService: { getBackups: vi.fn() } }))
vi.mock('@/features/apps/services/apps.service', () => ({ appsService: { getApps: vi.fn() } }))

const RouterLinkStub = { props: ['to'], template: '<a :data-to="JSON.stringify(to)"><slot /></a>' }
const mountPanel = () => mount(BackupsPanel, { global: { plugins: [PrimeVue], stubs: { RouterLink: RouterLinkStub } } })

describe('BackupsPanel', () => {
  it('shows the last, next and offsite tiles and links each app to its restore view', async () => {
    vi.mocked(appsService.getApps).mockResolvedValueOnce([
      { id: 'a1', name: 'Offsite Backup' }, { id: 'a2', name: 'Notes' },
    ] as never)
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true, lastAttemptAt: new Date().toISOString(), lastSuccessAt: new Date().toISOString(), lastError: null,
      keep: 7, directory: null, backupNowCommand: 'sudo bash backup.sh', backupRecipient: null, backupKeyCommand: '', backups: [],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.get('[data-test=tile-last]').text()).toContain('Less than an hour ago')
    expect(wrapper.get('[data-test=tile-next]').text()).toContain('every night')
    expect(wrapper.get('[data-test=tile-offsite]').text()).toContain('Offsite Backup')
    expect(wrapper.get('[data-test=app-restore]').html()).toContain('/apps/a2')
    expect(wrapper.get('[data-test=app-restore]').html()).toContain('more')
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
})
