import { beforeEach, describe, expect, it, vi } from 'vitest'
import { adminHostedRunnersService } from './adminHostedRunnersService'

const apiClient = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/shared/services/api/axios-instance', () => ({ default: apiClient }))

describe('Durable host validation', () => {
  beforeEach(() => vi.clearAllMocks())

  it('starts validation with the stable host and Runner identity', async () => {
    const operation = { id: 'operation-id', status: 'queued' }
    apiClient.post.mockResolvedValue({ data: operation })
    await expect(adminHostedRunnersService.startHostValidation('host-id', 'runner-id')).resolves.toBe(operation)
    expect(apiClient.post).toHaveBeenCalledWith('/api/admin/infrastructure-workflows/host-validation', {
      runnerHostId: 'host-id', runnerId: 'runner-id', idempotencyKey: expect.any(String),
    })
  })

  it('refreshes the persisted operation without starting another command', async () => {
    const operation = { id: 'operation-id', status: 'succeeded' }
    apiClient.get.mockResolvedValue({ data: operation })
    await expect(adminHostedRunnersService.getInfrastructureWorkflowExecution('operation-id')).resolves.toBe(operation)
    expect(apiClient.get).toHaveBeenCalledWith('/api/admin/infrastructure-workflows/executions/operation-id')
    expect(apiClient.post).not.toHaveBeenCalled()
  })
})
