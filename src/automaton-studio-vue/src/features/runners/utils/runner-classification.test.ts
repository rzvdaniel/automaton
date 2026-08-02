import { describe, expect, it } from 'vitest'
import {
  AccessScope,
  RunnerApplicationProfiles,
  RunnerStatus,
  type Runner,
} from '../types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from './runner-classification'

function runner(overrides: Partial<Runner> = {}): Runner {
  return {
    id: 'runner-1',
    name: 'Runner',
    status: RunnerStatus.Offline,
    connectionSessionCount: 0,
    scope: AccessScope.Personal,
    isHostedRunner: false,
    ...overrides,
  }
}

describe('runner classification', () => {
  it('uses the hosted-runner relationship as the authoritative managed classification', () => {
    expect(
      classifyRunner(
        runner({
          isHostedRunner: true,
          applicationProfile: RunnerApplicationProfiles.PrivilegedHost,
        })
      )
    ).toBe('automaton-hosted')
  })

  it('classifies legacy null-profile application runners as self-hosted standard', () => {
    expect(classifyRunner(runner({ applicationProfile: null }))).toBe('self-hosted-standard')
  })

  it('marks a non-hosted Privileged Host runner with its warning classification', () => {
    expect(
      classifyRunner(runner({ applicationProfile: RunnerApplicationProfiles.PrivilegedHost }))
    ).toBe('self-hosted-privileged')
  })

  it('hides infrastructure runners from the normal Runners page', () => {
    const infrastructureRunner = runner({ purpose: 'Infrastructure' })

    expect(classifyRunner(infrastructureRunner)).toBe('hidden-infrastructure')
    expect(isVisibleApplicationRunner(infrastructureRunner)).toBe(false)
  })
})
