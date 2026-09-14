import { describe, expect, it, vi } from 'vitest'
import type { HostedRunnerActionService } from './hostedRunnerActions'
import {
  canResumeHostedRunner,
  canSuspendHostedRunner,
  executeHostedRunnerAction,
  isHostedRunnerActionPending,
} from './hostedRunnerActions'

function runner(
  overrides: Partial<{
    status: 'running' | 'suspended' | 'deleting' | 'deleted'
    enforcementState: string
  }> = {}
) {
  return { status: 'running' as const, enforcementState: 'None', ...overrides }
}

describe('hosted Runner administration actions', () => {
  it('invokes each existing service method', async () => {
    const service: HostedRunnerActionService = {
      verifyHostedRunnerStorage: vi.fn().mockResolvedValue(undefined),
      suspendHostedRunner: vi.fn().mockResolvedValue(undefined),
      resumeHostedRunner: vi.fn().mockResolvedValue(undefined),
    }

    await executeHostedRunnerAction('verifyStorage', 'runner-id', service)
    await executeHostedRunnerAction('suspend', 'runner-id', service)
    await executeHostedRunnerAction('resume', 'runner-id', service)

    expect(service.verifyHostedRunnerStorage).toHaveBeenCalledWith('runner-id')
    expect(service.suspendHostedRunner).toHaveBeenCalledWith('runner-id')
    expect(service.resumeHostedRunner).toHaveBeenCalledWith('runner-id')
  })

  it.each(['Suspended', 'SuspensionPending', 'ResumePending'])(
    'hides Suspend for %s enforcement state',
    (enforcementState) => {
      expect(canSuspendHostedRunner(runner({ enforcementState }))).toBe(false)
    }
  )

  it('shows Suspend for an ordinary running Runner', () => {
    expect(canSuspendHostedRunner(runner())).toBe(true)
  })

  it('shows Resume only for a suspended Runner', () => {
    expect(
      canResumeHostedRunner(runner({ status: 'suspended', enforcementState: 'Suspended' }))
    ).toBe(true)
    expect(canResumeHostedRunner(runner())).toBe(false)
    expect(
      canResumeHostedRunner(runner({ status: 'suspended', enforcementState: 'ResumePending' }))
    ).toBe(false)
  })

  it('treats a pending action as unavailable for duplicate submission', () => {
    expect(isHostedRunnerActionPending('runner-id', 'runner-id')).toBe(true)
    expect(isHostedRunnerActionPending('runner-id', null)).toBe(false)
    expect(isHostedRunnerActionPending('other-runner', 'runner-id')).toBe(false)
  })
})
