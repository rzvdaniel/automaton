/**
 * Runners feature types
 * TypeScript interfaces matching backend models
 */

/**
 * Runner status enum - matches Automaton.Core.Enums.RunnerStatus
 */
export const RunnerStatus = {
  None: 0,
  Online: 1,
  Offline: 2,
} as const

export type RunnerStatus = (typeof RunnerStatus)[keyof typeof RunnerStatus]

/**
 * Runner details response from server
 * Matches RunnerDetails from RunnersController.cs
 */
export interface Runner {
  id: string
  name: string
  connectionId?: string
  status: RunnerStatus
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
export function getStatusSeverity(
  status: RunnerStatus,
): 'success' | 'danger' | 'secondary' {
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
