import {
  RunnerApplicationProfiles,
  type CreateSelfHostedRunnerInstallationRequest,
  type RunnerApplicationProfile,
} from '../types/runners.types'

export function isValidSelfHostedRunnerName(runnerName: string): boolean {
  const length = runnerName.trim().length
  return length > 0 && length <= 120
}

export function requiresHostAccessAcknowledgement(profile: RunnerApplicationProfile): boolean {
  return profile === RunnerApplicationProfiles.PrivilegedHost
}

export function canCreateSelfHostedInstallation(
  runnerName: string,
  profile: RunnerApplicationProfile,
  acknowledgedHostAccess: boolean
): boolean {
  return (
    isValidSelfHostedRunnerName(runnerName) &&
    (!requiresHostAccessAcknowledgement(profile) || acknowledgedHostAccess)
  )
}

export function createSelfHostedInstallationRequest(
  runnerName: string,
  applicationProfile: RunnerApplicationProfile
): CreateSelfHostedRunnerInstallationRequest {
  return {
    runnerName: runnerName.trim(),
    applicationProfile,
  }
}
