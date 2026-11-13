import { beforeEach, describe, expect, it, vi } from 'vitest'
import apiClient from '@/shared/services/api/axios-instance'
import { serverCleanupService } from './server-cleanup.service'

vi.mock('@/shared/services/api/axios-instance', () => ({ default: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }))

const body = { settings: { timeUtc: '05:00', keepDays: 7, journalLimitMb: 200 }, nextRunAtUtc: '2026-11-14T05:00:00Z', runs: [] }

describe('serverCleanupService', () => {
  beforeEach(() => vi.clearAllMocks())

  it('reads the cleanup', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: body })
    expect(await serverCleanupService.get()).toEqual(body)
    expect(apiClient.get).toHaveBeenCalledWith('/api/server/cleanup')
  })

  it('saves the settings', async () => {
    vi.mocked(apiClient.put).mockResolvedValue({ data: body })
    await serverCleanupService.saveSettings(body.settings)
    expect(apiClient.put).toHaveBeenCalledWith('/api/server/cleanup/settings', body.settings)
  })

  it('starts a run', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: body })
    await serverCleanupService.run()
    expect(apiClient.post).toHaveBeenCalledWith('/api/server/cleanup/run')
  })
})
