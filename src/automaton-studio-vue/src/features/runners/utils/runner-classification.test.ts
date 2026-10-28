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
  it('marks a non-hosted Machine Management runner distinctly', () => {
    expect(
      classifyRunner(runner({ applicationProfile: RunnerApplicationProfiles.MachineManagement }))
    ).toBe('self-hosted-machine-management')
  })

  it('hides all unsupported and retired runners from the normal Runners page', () => {
    expect(classifyRunner(runner({ applicationProfile: null }))).toBe('unsupported')
    expect(classifyRunner(runner({ isHostedRunner: true, applicationProfile: RunnerApplicationProfiles.MachineManagement }))).toBe('unsupported')
    expect(isVisibleApplicationRunner(runner({ applicationProfile: null }))).toBe(false)
  })
})
