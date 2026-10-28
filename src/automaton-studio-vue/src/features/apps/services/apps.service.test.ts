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
