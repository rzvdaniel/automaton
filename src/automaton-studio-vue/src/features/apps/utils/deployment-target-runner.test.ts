import { describe, expect, it } from 'vitest'
import { AccessScope, RunnerApplicationProfiles, RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import {
  getDeploymentTargetRunnerOptions,
  getDeploymentTargetRunnerProjectId,
} from './deployment-target-runner'

function runner(overrides: Partial<Runner> = {}): Runner {
  return { id: 'runner', name: 'Runner', status: RunnerStatus.Online, connectionSessionCount: 0, scope: AccessScope.Personal, isHostedRunner: false, ...overrides }
}

describe('deployment target Runner options', () => {
  it('shows only Machine Management runners and disables offline runners', () => {
    const options = getDeploymentTargetRunnerOptions([
      runner({ id: 'unsupported', applicationProfile: 'Standard' as never }),
      runner({ id: 'hosted', isHostedRunner: true, applicationProfile: RunnerApplicationProfiles.MachineManagement }),
      runner({ id: 'offline', status: RunnerStatus.Offline }),
    ])

    expect(options).toEqual([
      expect.objectContaining({ value: 'offline', disabled: true, label: expect.stringContaining('Machine Management') }),
    ])
  })

  it('loads Runners in the App project context', () => {
    expect(getDeploymentTargetRunnerProjectId('project-id')).toBe('project-id')
    expect(getDeploymentTargetRunnerProjectId(null)).toBeUndefined()
  })

})
