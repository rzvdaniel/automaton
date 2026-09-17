import { beforeEach, describe, expect, it, vi } from 'vitest'
import { appsService } from './apps.service'

const apiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  delete: vi.fn(),
  put: vi.fn(),
}))

vi.mock('@/shared/services/api/axios-instance', () => ({ default: apiClient }))

describe('AppsService privileged Host approval', () => {
  beforeEach(() => vi.clearAllMocks())

  it('approves privileged Host access on a deployment target', async () => {
    const target = { id: 'target-id', privilegedHostAccessApproved: true }
    apiClient.post.mockResolvedValue({ data: target })

    await expect(appsService.approvePrivilegedHostAccess('app-id', 'target-id')).resolves.toBe(target)

    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/apps/app-id/targets/target-id/privileged-host-access',
      {},
    )
  })

  it('revokes privileged Host access on a deployment target', async () => {
    const target = { id: 'target-id', privilegedHostAccessApproved: false }
    apiClient.delete.mockResolvedValue({ data: target })

    await expect(appsService.revokePrivilegedHostAccess('app-id', 'target-id')).resolves.toBe(target)

    expect(apiClient.delete).toHaveBeenCalledWith(
      '/api/apps/app-id/targets/target-id/privileged-host-access',
    )
  })
})

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
