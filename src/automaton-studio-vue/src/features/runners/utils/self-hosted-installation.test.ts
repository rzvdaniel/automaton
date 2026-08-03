import { describe, expect, it } from 'vitest'
import {
  RunnerApplicationProfiles,
  SelfHostedRunnerInstallationStatuses,
} from '../types/runners.types'
import {
  canCreateSelfHostedInstallation,
  createSelfHostedInstallationRequest,
  getSelfHostedInstallationStatusLabel,
  isSelfHostedInstallationPollingComplete,
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

  it.each([
    [SelfHostedRunnerInstallationStatuses.CommandCreated, 'Command created'],
    [SelfHostedRunnerInstallationStatuses.WaitingForEnrollment, 'Waiting for enrollment'],
    [SelfHostedRunnerInstallationStatuses.RunnerEnrolled, 'Runner enrolled'],
    [SelfHostedRunnerInstallationStatuses.RunnerConnected, 'Runner connected'],
    [SelfHostedRunnerInstallationStatuses.Expired, 'Expired'],
    [SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment, 'Offline after enrollment'],
  ])('shows %s as %s', (status, label) => {
    expect(getSelfHostedInstallationStatusLabel(status)).toBe(label)
  })

  it('stops automatic polling only for connected or expired installations', () => {
    expect(
      isSelfHostedInstallationPollingComplete(
        SelfHostedRunnerInstallationStatuses.RunnerConnected
      )
    ).toBe(true)
    expect(
      isSelfHostedInstallationPollingComplete(SelfHostedRunnerInstallationStatuses.Expired)
    ).toBe(true)
    expect(
      isSelfHostedInstallationPollingComplete(
        SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment
      )
    ).toBe(false)
  })
})
