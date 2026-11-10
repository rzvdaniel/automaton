import { beforeEach, describe, expect, it, vi } from 'vitest'
import { appsService } from './apps.service'

const apiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  delete: vi.fn(),
  put: vi.fn(),
}))

vi.mock('@/shared/services/api/axios-instance', () => ({ default: apiClient }))

describe('AppsService metadata settings', () => {
  beforeEach(() => vi.clearAllMocks())

  it('persists a cleared marketplace category instead of restoring the old value', async () => {
    apiClient.get.mockResolvedValue({ data: {
      id: 'app-id',
      name: 'Clock',
      category: 'Productivity',
      definition: { version: 1 },
      screenshots: [],
    } })

    await appsService.updateSettings('app-id', { category: null })

    expect(apiClient.put).toHaveBeenCalledWith('/api/apps/app-id', expect.objectContaining({ category: null }))
  })

  it('loads marketplace categories through the app service', async () => {
    apiClient.get.mockResolvedValue({ data: ['Productivity', 'Utilities'] })

    await expect(appsService.getMarketplaceCategories()).resolves.toEqual(['Productivity', 'Utilities'])
    expect(apiClient.get).toHaveBeenCalledWith('/api/public/marketplace/categories')
  })
})

describe('AppsService long-running actions', () => {
  beforeEach(() => vi.clearAllMocks())

  it('gives restart and resume a longer timeout than the default', async () => {
    apiClient.post.mockResolvedValue({ data: {} })
    await appsService.restart('a')
    await appsService.setPaused('a', false)
    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/apps/a/restart', undefined, { timeout: 180000 })
    expect(apiClient.post).toHaveBeenNthCalledWith(2, '/api/apps/a/resume', undefined, { timeout: 180000 })
  })
})
