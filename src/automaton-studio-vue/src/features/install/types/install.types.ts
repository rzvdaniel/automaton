/**
 * Install feature TypeScript types
 * Based on ASP.NET API contracts:
 * - GET /api/install/status
 * - POST /api/install
 */

/**
 * Request to install Automaton with admin account details
 */
export interface InstallCommand {
  name: string
  email: string
  password: string
}

/**
 * Response from installation status check
 */
export interface InstallationStatusResponse {
  installationComplete: boolean
  setupTokenRequired: boolean
  gitHubEnabled: boolean
  userSignUp: boolean
}

/**
 * Form validation errors
 */
export interface InstallFormErrors {
  name?: string
  email?: string
  password?: string
  confirmPassword?: string
}

export interface InstallationCheck {
  name: string
  status: 'verified' | 'configured' | 'unverified' | 'error'
  detail: string
}

export interface SmtpSettingsSummary {
  source: 'none' | 'file' | 'studio'
  emailHost: string
  emailPort: number
  emailUseSsl: boolean
  emailAccount: string
  fromEmail: string
  supportEmail: string
  passwordSet: boolean
  verifiedAt: string | null
}

export interface SmtpSettingsUpdate {
  emailHost: string
  emailPort: number
  emailUseSsl: boolean
  emailAccount: string
  emailPassword: string
  fromEmail: string
  supportEmail: string
}

export interface InstallationBackup {
  name: string
  createdAt: string
  sizeBytes: number
  restoreCommand: string
  archiveSizeBytes: number | null
  archiveSha256: string | null
}

export interface InstallationUpdateRun {
  state: 'starting' | 'running' | 'succeeded' | 'failed' | 'unknown'
  requestedVersion: string | null
  fromVersion: string | null
  toVersion: string | null
  startedAtUtc: string | null
  finishedAtUtc: string | null
  exitCode: number | null
  recoveryBackup: string | null
  requestedBy: string | null
  message: string | null
}

export interface InstallationUpdateStatus {
  currentVersion: string | null
  triggerAvailable: boolean
  run: InstallationUpdateRun | null
  log: string | null
  stale: boolean
  requestPending: boolean
  requestAgeSeconds: number | null
  hint: string | null
}

export interface InstallationBackupStatus {
  available: boolean
  lastAttemptAt: string | null
  lastSuccessAt: string | null
  lastError: string | null
  keep: number
  directory: string | null
  backupNowCommand: string
  backupRecipient: string | null
  backupKeyCommand: string
  backups: InstallationBackup[]
  /** When the host timer next starts a backup, if the host reports it. */
  nextRunAt?: string | null
  /** The host can start a backup requested from Studio. */
  triggerAvailable?: boolean
  requestPending?: boolean
  /** Set when the host has not picked up a backup request. */
  hint?: string | null
}
