/**
 * Runners feature types
 * TypeScript interfaces matching backend models
 */

/**
 * Runner status enum - matches Automaton.Core.Enums.RunnerStatus
 */
export const RunnerStatus = {
  None: 'None',
  Online: 'Online',
  Offline: 'Offline',
} as const

export type RunnerStatus = (typeof RunnerStatus)[keyof typeof RunnerStatus]

/**
 * Access scope enum for resources (matches Automaton.Api.Enums.AccessScope)
 */
export const AccessScope = {
  Personal: 0,
  Project: 1,
} as const

export type AccessScope = (typeof AccessScope)[keyof typeof AccessScope]

/**
 * Runner details response from server
 * Matches RunnerDetails from RunnersController.cs
 */
export interface Runner {
  id: string
  name: string
  connectionId?: string
  status: RunnerStatus
  lastHeartbeatAtUtc?: string | null
  lastConnectedAtUtc?: string | null
  lastDisconnectedAtUtc?: string | null
  currentSessionConnectedAtUtc?: string | null
  connectionSessionCount: number
  currentSessionId?: string | null
  lastDisconnectReason?: string | null
  version?: string | null
  machineName?: string | null
  ipAddress?: string | null
  apiInstanceId?: string | null
  apiColor?: string | null
  purpose?: string
  applicationProfile?: string | null
  capabilities?: string[]
  runnerHostId?: string | null
  scope: AccessScope
  projectId?: string | null
  projectName?: string | null
  hostedRunnerId?: string | null
  isHostedRunner: boolean
  storageAllowanceBytes?: number | null
  storageTotalBytes?: number | null
  storageUsagePercent?: number | null
  storageMeasuredAtUtc?: string | null
  storageMeasurementSource?: string | null
  storageMeasurementStatus?: string | null
  storageState?: string | null
  storageMeasurementError?: string | null
  machineStorageTotalBytes?: number | null
  machineStorageAvailableBytes?: number | null
  machineStorageMeasuredAtUtc?: string | null
  machineStorageMeasurementStatus?: string | null
  machineStorageMeasurementError?: string | null
}

export const RunnerApplicationProfiles = {
  MachineManagement: 'MachineManagement',
} as const

export type RunnerApplicationProfile =
  (typeof RunnerApplicationProfiles)[keyof typeof RunnerApplicationProfiles]

export const SelfHostedRunnerInstallationStatuses = {
  CommandCreated: 'CommandCreated',
  WaitingForEnrollment: 'WaitingForEnrollment',
  RunnerEnrolled: 'RunnerEnrolled',
  RunnerConnected: 'RunnerConnected',
  Expired: 'Expired',
  OfflineAfterEnrollment: 'OfflineAfterEnrollment',
} as const

export type SelfHostedRunnerInstallationStatus =
  (typeof SelfHostedRunnerInstallationStatuses)[keyof typeof SelfHostedRunnerInstallationStatuses]

export interface CreateSelfHostedRunnerInstallationRequest {
  runnerName: string
  applicationProfile: RunnerApplicationProfile
  acknowledgeHostAccess: boolean
}

export interface SelfHostedRunnerInstallation {
  installationId: string
  enrollmentId: string
  runnerName: string
  applicationProfile: RunnerApplicationProfile
  expiresAtUtc: string
  installationCommand: string
  requiresHostAccessAcknowledgement: boolean
  hostAccessWarning?: string | null
  status: SelfHostedRunnerInstallationStatus
}

export interface SelfHostedRunnerInstallationStatusResponse {
  installationId: string
  status: SelfHostedRunnerInstallationStatus
  expiresAtUtc: string
  runnerId?: string | null
  lastConnectedAtUtc?: string | null
  lastHeartbeatAtUtc?: string | null
}

/**
 * Runner status response
 * Matches RunnerStatusResponse from RunnersController.cs
 */
export interface RunnerStatusResponse {
  hasOnlineRunners: boolean
  totalRunners: number
  onlineRunners: number
}

/**
 * Get status display name
 */
export function getStatusDisplayName(status: RunnerStatus): string {
  switch (status) {
    case RunnerStatus.Online:
      return 'Online'
    case RunnerStatus.Offline:
      return 'Offline'
    case RunnerStatus.None:
    default:
      return 'Unknown'
  }
}

/**
 * Get status color for PrimeVue Tag severity
 */
export function getStatusSeverity(status: RunnerStatus): 'success' | 'danger' | 'secondary' {
  switch (status) {
    case RunnerStatus.Online:
      return 'success'
    case RunnerStatus.Offline:
      return 'danger'
    case RunnerStatus.None:
    default:
      return 'secondary'
  }
}

/**
 * Get status icon
 */
export function getStatusIcon(status: RunnerStatus): string {
  switch (status) {
    case RunnerStatus.Online:
      return 'pi pi-check-circle'
    case RunnerStatus.Offline:
      return 'pi pi-times-circle'
    case RunnerStatus.None:
    default:
      return 'pi pi-question-circle'
  }
}

/**
 * Helper: whether runner is linked to a project
 */
export function isRunnerLinkedToProject(runner: Runner): boolean {
  return runner.scope === AccessScope.Project && !!runner.projectId
}
