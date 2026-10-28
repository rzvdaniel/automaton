/**
 * API Keys feature types
 * TypeScript interfaces for API keys data structures
 */

/**
 * API Key scope enum - matches Automaton.Core.Enums.ApiKeyScope
 */
export const ApiKeyScope = {
  ReadOnly: 1,
  WorkflowExecution: 2,
  Runner: 3,
  FullAccess: 4,
  WebApp: 5,
  Developer: 7,
} as const

export type ApiKeyScope = (typeof ApiKeyScope)[keyof typeof ApiKeyScope]

export const ApiKeySource = {
  Manual: 1,
  CliAuthorization: 2,
  RunnerProvisioning: 3,
} as const

export type ApiKeySource = (typeof ApiKeySource)[keyof typeof ApiKeySource]

/**
 * API Key response from server
 * Matches ApiKeyResponse from ApiKeysController.cs
 */
export interface ApiKey {
  id: string
  name: string
  keyPrefix: string
  scope: ApiKeyScope
  source: ApiKeySource
  createdAt: string // ISO date string
  lastUsedAt?: string // ISO date string
  expiresAt?: string // ISO date string
  isActive: boolean
  description?: string
}

/**
 * Request to create a new API key
 * Matches CreateApiKeyRequest from ApiKeysController.cs
 */
export interface CreateApiKeyRequest {
  name: string
  description?: string
  expiresAt?: string // ISO date string
  scope: ApiKeyScope
}

/**
 * Response when creating a new API key (includes full key - only shown once)
 * Matches CreateApiKeyResponse from ApiKeysController.cs
 */
export interface CreateApiKeyResponse {
  id: string
  name: string
  apiKey: string // Full key - only shown during creation
  keyPrefix: string
  scope: ApiKeyScope
  source: ApiKeySource
  createdAt: string
  expiresAt?: string
}

/**
 * Get scope display name
 */
export function getScopeDisplayName(scope: ApiKeyScope): string {
  switch (scope) {
    case ApiKeyScope.ReadOnly:
      return 'Read Only'
    case ApiKeyScope.WorkflowExecution:
      return 'Workflow Execution'
    case ApiKeyScope.Runner:
      return 'Runner'
    case ApiKeyScope.FullAccess:
      return 'Full Access'
    case ApiKeyScope.WebApp:
      return 'Web App'
    case ApiKeyScope.Developer:
      return 'Developer / CLI'
    default:
      return 'Unknown'
  }
}

export function getSourceDisplayName(source: ApiKeySource): string {
  switch (source) {
    case ApiKeySource.Manual:
      return 'Manual'
    case ApiKeySource.CliAuthorization:
      return 'Automaton CLI authorization'
    case ApiKeySource.RunnerProvisioning:
      return 'Runner provisioning'
    default:
      return 'Unknown'
  }
}

/**
 * Get scope color for PrimeVue Tag severity
 */
export function getScopeSeverity(
  scope: ApiKeyScope,
): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (scope) {
    case ApiKeyScope.ReadOnly:
      return 'info'
    case ApiKeyScope.WorkflowExecution:
      return 'info'
    case ApiKeyScope.Runner:
      return 'info'
    case ApiKeyScope.FullAccess:
      return 'danger'
    case ApiKeyScope.WebApp:
      return 'warn'
    case ApiKeyScope.Developer:
      return 'success'
    default:
      return 'secondary'
  }
}

/**
 * Get usage instructions based on scope
 */
export function getUsageInstructions(apiKey: string, scope: ApiKeyScope): string {
  switch (scope) {
    case ApiKeyScope.Developer:
      return `Use this Developer key with the Automaton CLI:

automaton login --key ${apiKey}
automaton deploy
automaton publish`

    case ApiKeyScope.Runner:
    case ApiKeyScope.FullAccess:
      return `Use this API key to authenticate console runners and execute workflows:

automaton-runner auth --api-key ${apiKey}
export AUTOMATON_API_KEY="${apiKey}"`

    case ApiKeyScope.WorkflowExecution:
      return `Use this Workflow Execution key to run workflows and read their execution status.

It does not provide general app-management access.`

    case ApiKeyScope.WebApp:
      return "This API key enables your published web apps to execute flows securely. The key is used automatically by the server when users interact with your web apps - you don't need to configure it in your web app code."

    case ApiKeyScope.ReadOnly:
      return 'This API key provides read-only access to flows and execution history. Use it for monitoring, reporting, or integration with external dashboards.'

    default:
      return 'API key created successfully.'
  }
}
