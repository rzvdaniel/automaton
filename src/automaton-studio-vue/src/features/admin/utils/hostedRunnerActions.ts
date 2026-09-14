import type { HostedRunner } from '../types/admin.types'

export type HostedRunnerAction = 'verifyStorage' | 'suspend' | 'resume'

export interface HostedRunnerActionService {
  verifyHostedRunnerStorage(id: string): Promise<void>
  suspendHostedRunner(id: string): Promise<void>
  resumeHostedRunner(id: string): Promise<void>
}

const suspensionInProgressStates = new Set(['suspended', 'suspensionpending', 'resumepending'])

function normalizedEnforcementState(runner: Pick<HostedRunner, 'enforcementState'>) {
  return runner.enforcementState.trim().toLowerCase()
}

export function canSuspendHostedRunner(runner: Pick<HostedRunner, 'status' | 'enforcementState'>) {
  return (
    runner.status !== 'deleted' &&
    runner.status !== 'deleting' &&
    !suspensionInProgressStates.has(normalizedEnforcementState(runner))
  )
}

export function canResumeHostedRunner(runner: Pick<HostedRunner, 'status' | 'enforcementState'>) {
  return (
    runner.status !== 'deleted' &&
    runner.status !== 'deleting' &&
    normalizedEnforcementState(runner) === 'suspended'
  )
}

export function isHostedRunnerActionPending(runnerId: string, pendingRunnerId: string | null) {
  return runnerId === pendingRunnerId
}

export function executeHostedRunnerAction(
  action: HostedRunnerAction,
  id: string,
  service: HostedRunnerActionService
) {
  switch (action) {
    case 'verifyStorage':
      return service.verifyHostedRunnerStorage(id)
    case 'suspend':
      return service.suspendHostedRunner(id)
    case 'resume':
      return service.resumeHostedRunner(id)
  }
}
