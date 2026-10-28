import {
  RunnerApplicationProfiles,
  SelfHostedRunnerInstallationStatuses,
  type CreateSelfHostedRunnerInstallationRequest,
  type RunnerApplicationProfile,
  type SelfHostedRunnerInstallationStatus,
} from '../types/runners.types'

export function isValidSelfHostedRunnerName(runnerName: string): boolean {
  const length = runnerName.trim().length
  return length > 0 && length <= 120
}

export function requiresHostAccessAcknowledgement(profile: RunnerApplicationProfile): boolean {
  return profile === RunnerApplicationProfiles.MachineManagement
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
  applicationProfile: RunnerApplicationProfile,
  acknowledgeHostAccess = false
): CreateSelfHostedRunnerInstallationRequest {
  return {
    runnerName: runnerName.trim(),
    applicationProfile,
    acknowledgeHostAccess,
  }
}

export function getSelfHostedInstallationStatusLabel(
  status: SelfHostedRunnerInstallationStatus
): string {
  switch (status) {
    case SelfHostedRunnerInstallationStatuses.CommandCreated:
      return 'Command created'
    case SelfHostedRunnerInstallationStatuses.WaitingForEnrollment:
      return 'Waiting for enrollment'
    case SelfHostedRunnerInstallationStatuses.RunnerEnrolled:
      return 'Runner enrolled'
    case SelfHostedRunnerInstallationStatuses.RunnerConnected:
      return 'Runner connected'
    case SelfHostedRunnerInstallationStatuses.Expired:
      return 'Expired'
    case SelfHostedRunnerInstallationStatuses.OfflineAfterEnrollment:
      return 'Offline after enrollment'
  }
}

export function isSelfHostedInstallationPollingComplete(
  status: SelfHostedRunnerInstallationStatus
): boolean {
  return (
    status === SelfHostedRunnerInstallationStatuses.RunnerConnected ||
    status === SelfHostedRunnerInstallationStatuses.Expired
  )
}
