// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import BackupsPanel from './BackupsPanel.vue'
import { installService } from '../services/install.service'

vi.mock('../services/install.service', () => ({ installService: { getBackups: vi.fn() } }))

const mountPanel = () => mount(BackupsPanel, { global: { plugins: [PrimeVue] } })

describe('BackupsPanel', () => {
  it('lists backups with the command to restore each one on the server', async () => {
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: true,
      lastAttemptAt: new Date().toISOString(),
      lastSuccessAt: new Date().toISOString(),
      lastError: null,
      keep: 7,
      directory: '/opt/automaton-server/backups/nightly',
      backupNowCommand: 'sudo bash /opt/automaton-server/backup.sh',
      backups: [{
        name: 'automaton-20261101T031000Z',
        createdAt: '2026-11-01T03:10:00Z',
        sizeBytes: 2 * 1024 * 1024,
        restoreCommand: 'sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z',
      }],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z')
    expect(wrapper.text()).toContain('2.0 MB')
    expect(wrapper.text()).toContain('sudo bash /opt/automaton-server/backup.sh')
    expect(wrapper.text()).not.toContain('older than')
  })

  it('explains how to enable backups when the server has none set up', async () => {
    vi.mocked(installService.getBackups).mockResolvedValueOnce({
      available: false, lastAttemptAt: null, lastSuccessAt: null, lastError: null, keep: 0, directory: null,
      backupNowCommand: 'sudo bash /opt/automaton-server/backup.sh', backups: [],
    })
    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.text()).toContain('Nightly backups are not set up on this server yet')
  })
})
