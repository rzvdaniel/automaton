import { beforeEach, describe, expect, it, vi } from 'vitest'
import { appsService } from './apps.service'

const apiClient = vi.hoisted(() => ({
  post: vi.fn(),
  delete: vi.fn(),
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
