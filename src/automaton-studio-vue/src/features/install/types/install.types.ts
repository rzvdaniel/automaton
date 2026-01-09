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
