import { describe, expect, it, vi } from 'vitest'
import apiClient from '@/shared/services/api/axios-instance'
import { installService } from './install.service'
vi.mock('@/shared/services/api/axios-instance', () => ({ default: { get: vi.fn(), post: vi.fn() } }))

describe('installation service', () => {
  it('propagates an unavailable API instead of treating it as an empty installation', async () => {
    vi.mocked(apiClient.get).mockRejectedValueOnce(new Error('offline'))
    await expect(installService.getStatus()).rejects.toThrow('offline')
  })
  it('sends the bootstrap token in a header for both checks and administrator creation', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: [] })
    await installService.check('private-setup-token')
    expect(apiClient.post).toHaveBeenLastCalledWith('/api/install/checks', null, {
      headers: { 'X-Automaton-Setup-Token': 'private-setup-token' },
    })
    const command = { name: 'Owner', email: 'owner@example.com', password: 'Setup-Test-Password-123!' }
    await installService.install(command, 'private-setup-token')
    expect(apiClient.post).toHaveBeenLastCalledWith('/api/install', command, {
      headers: { 'X-Automaton-Setup-Token': 'private-setup-token' },
    })
  })
})
