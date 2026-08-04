import { describe, expect, it } from 'vitest'
import { AccessScope, RunnerApplicationProfiles, RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import {
  getDeploymentTargetRunnerOptions,
  getDeploymentTargetRunnerProjectId,
  isPrivilegedSelfHostedRunner,
  requiresPrivilegedHostAcknowledgement,
  resolveAutomatonHostedRunnerId,
  shouldResetPrivilegedHostAcknowledgement,
} from './deployment-target-runner'

function runner(overrides: Partial<Runner> = {}): Runner {
  return { id: 'runner', name: 'Runner', status: RunnerStatus.Online, connectionSessionCount: 0, scope: AccessScope.Personal, isHostedRunner: false, ...overrides }
}

describe('deployment target Runner options', () => {
  it('filters Infrastructure Runners and keeps offline application Runners unavailable', () => {
    const options = getDeploymentTargetRunnerOptions([
      runner({ id: 'infrastructure', purpose: 'Infrastructure' }),
      runner({ id: 'offline', status: RunnerStatus.Offline }),
      runner({ id: 'hosted', isHostedRunner: true }),
    ])

    expect(options).toEqual([
      expect.objectContaining({ value: 'offline', disabled: true, label: expect.stringContaining('Self-hosted · Standard') }),
      expect.objectContaining({ value: 'hosted', disabled: false, label: expect.stringContaining('Automaton-hosted') }),
    ])
  })

  it('requires acknowledgement only for a new privileged-host assignment', () => {
    const privileged = runner({ id: 'privileged', applicationProfile: RunnerApplicationProfiles.PrivilegedHost })

    expect(isPrivilegedSelfHostedRunner(privileged)).toBe(true)
    expect(requiresPrivilegedHostAcknowledgement(privileged, 'privileged', null)).toBe(true)
    expect(requiresPrivilegedHostAcknowledgement(privileged, 'privileged', 'privileged')).toBe(false)
    expect(requiresPrivilegedHostAcknowledgement(runner({ id: 'standard' }), 'standard', null)).toBe(false)
    expect(shouldResetPrivilegedHostAcknowledgement('another-runner', 'privileged')).toBe(true)
    expect(shouldResetPrivilegedHostAcknowledgement('privileged', 'privileged')).toBe(false)
  })

  it('loads Runners in the App project context', () => {
    expect(getDeploymentTargetRunnerProjectId('project-id')).toBe('project-id')
    expect(getDeploymentTargetRunnerProjectId(null)).toBeUndefined()
  })

  it('does not choose an arbitrary managed Hosted Runner', () => {
    const hostedA = runner({ id: 'hosted-a', isHostedRunner: true })
    const hostedB = runner({ id: 'hosted-b', isHostedRunner: true })

    expect(resolveAutomatonHostedRunnerId([hostedA])).toBe('hosted-a')
    expect(resolveAutomatonHostedRunnerId([hostedA, hostedB])).toBeNull()
    expect(resolveAutomatonHostedRunnerId([hostedA, hostedB], 'hosted-b')).toBe('hosted-b')
  })
})
