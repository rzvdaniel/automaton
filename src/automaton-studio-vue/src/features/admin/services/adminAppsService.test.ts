import { beforeEach, describe, expect, it, vi } from 'vitest'
import { adminAppsService } from './adminAppsService'

const get = vi.hoisted(() => vi.fn())
const put = vi.hoisted(() => vi.fn())
vi.mock('@/shared/services/api/axios-instance', () => ({ default: { get, put } }))

describe('adminAppsService runner permissions', () => {
  beforeEach(() => {
    get.mockReset().mockResolvedValue({ data: [{ runnerId: 'r1', name: 'runner-01', approved: false }] })
    put.mockReset().mockResolvedValue({ data: [{ runnerId: 'r1', name: 'runner-01', approved: true }] })
  })

  it('reads the machines an app may maintain', async () => {
    expect(await adminAppsService.getRunnerPermissions('a1')).toEqual([{ runnerId: 'r1', name: 'runner-01', approved: false }])
    expect(get).toHaveBeenCalledWith('/api/admin/apps/a1/runner-permissions')
  })

  it('approves one machine at a time', async () => {
    const result = await adminAppsService.setRunnerPermission('a1', 'r1', true)
    expect(put).toHaveBeenCalledWith('/api/admin/apps/a1/runner-permissions/r1', { approved: true })
    expect(result[0]?.approved).toBe(true)
  })
})
