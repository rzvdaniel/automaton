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
  PermissionsAdmin: 8,
} as const

export type ApiKeyScope = (typeof ApiKeyScope)[keyof typeof ApiKeyScope]

/** The API returns the scope as its name ("Developer"); forms use the number. Accept both. */
export function normalizeScope(scope: ApiKeyScope | string): ApiKeyScope | undefined {
  if (typeof scope === 'number') return scope
  const byName = (ApiKeyScope as Record<string, ApiKeyScope>)[scope]
  return byName ?? (Object.values(ApiKeyScope) as number[]).find((v) => String(v) === scope) as ApiKeyScope | undefined
}

export const ApiKeySource = {
  Manual: 1,
  CliAuthorization: 2,
  RunnerProvisioning: 3,
} as const

export type ApiKeySource = (typeof ApiKeySource)[keyof typeof ApiKeySource]

/** The API sends the source name (e.g. "CliAuthorization"); accept a number too. */
export function normalizeSource(source: ApiKeySource | string): ApiKeySource | undefined {
  if (typeof source === 'number') return source
  const byName = (ApiKeySource as Record<string, ApiKeySource>)[source]
  return byName ?? (Object.values(ApiKeySource) as number[]).find((v) => String(v) === source) as ApiKeySource | undefined
}

/**
 * API Key response from server
 * Matches ApiKeyResponse from ApiKeysController.cs
 */
export interface ApiKey {
  id: string
  name: string
  keyPrefix: string
  scope: ApiKeyScope | string // the API sends the name, e.g. "Developer"
  source: ApiKeySource | string // the API sends the name, e.g. "CliAuthorization"
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
export function getScopeDisplayName(scope: ApiKeyScope | string): string {
  switch (normalizeScope(scope)) {
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
    case ApiKeyScope.PermissionsAdmin:
      return 'Admin: approve app permissions'
    default:
      return 'Unknown'
  }
}

export function getSourceDisplayName(source: ApiKeySource | string): string {
  switch (normalizeSource(source)) {
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
  scope: ApiKeyScope | string,
): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (normalizeScope(scope)) {
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
    case ApiKeyScope.PermissionsAdmin:
      return 'danger'
    default:
      return 'secondary'
  }
}

/**
 * Get usage instructions based on scope
 */
export function getUsageInstructions(apiKey: string, scope: ApiKeyScope | string): string {
  switch (normalizeScope(scope)) {
    case ApiKeyScope.Developer:
      return `Use this Developer key with the Automaton CLI:

automaton login --key ${apiKey}
automaton deploy
automaton publish`

    case ApiKeyScope.PermissionsAdmin:
      return `Use this admin key only on a machine you control. It can approve and reject app permissions and does nothing else. It expires and stops working if you are no longer a system administrator.

export AUTOMATON_API_KEY="${apiKey}"
automaton permissions list --pending
automaton permissions approve <app> <permission>`

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
