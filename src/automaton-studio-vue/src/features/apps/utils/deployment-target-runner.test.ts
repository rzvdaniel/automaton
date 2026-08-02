import { describe, expect, it } from 'vitest'
import { AccessScope, RunnerApplicationProfiles, RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'

function runner(overrides: Partial<Runner> = {}): Runner {
  return { id: 'runner', name: 'Runner', status: RunnerStatus.Online, connectionSessionCount: 0, scope: AccessScope.Personal, isHostedRunner: false, ...overrides }
}

describe('deployment target Runner options', () => {
  it('filters Infrastructure Runners and classifies privileged self-hosted Runners', () => {
    expect(isVisibleApplicationRunner(runner({ purpose: 'Infrastructure' }))).toBe(false)
    expect(classifyRunner(runner({ applicationProfile: RunnerApplicationProfiles.PrivilegedHost }))).toBe('self-hosted-privileged')
  })
})
