import { describe, expect, it } from 'vitest'
import { RunnerApplicationProfiles } from '../types/runners.types'
import {
  canCreateSelfHostedInstallation,
  createSelfHostedInstallationRequest,
  isValidSelfHostedRunnerName,
} from './self-hosted-installation'

describe('self-hosted Runner installation form', () => {
  it('accepts a Standard Runner without a host-access acknowledgement', () => {
    expect(
      canCreateSelfHostedInstallation('my-runner', RunnerApplicationProfiles.Standard, false)
    ).toBe(true)
  })

  it('requires acknowledgement before creating a Privileged Host installation', () => {
    expect(
      canCreateSelfHostedInstallation(
        'host-runner',
        RunnerApplicationProfiles.PrivilegedHost,
        false
      )
    ).toBe(false)
    expect(
      canCreateSelfHostedInstallation('host-runner', RunnerApplicationProfiles.PrivilegedHost, true)
    ).toBe(true)
  })

  it('enforces the API-aligned Runner name limits', () => {
    expect(isValidSelfHostedRunnerName('   ')).toBe(false)
    expect(isValidSelfHostedRunnerName('a'.repeat(120))).toBe(true)
    expect(isValidSelfHostedRunnerName('a'.repeat(121))).toBe(false)
  })

  it('sends only the name and selected application profile to the installation endpoint', () => {
    expect(
      createSelfHostedInstallationRequest('  my-runner  ', RunnerApplicationProfiles.Standard)
    ).toEqual({
      runnerName: 'my-runner',
      applicationProfile: 'Standard',
    })
  })
})
