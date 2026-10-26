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
