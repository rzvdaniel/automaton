/**
 * Admin feature TypeScript types
 * Matches backend DTOs from AdminAiProvidersController
 */

/**
 * AI Provider admin model
 * Matches AiProviderAdminResponse from backend
 */
export interface AiProviderAdmin {
  id: string
  name: string
  displayName: string
  description?: string
  baseUrl?: string
  isActive: boolean
  requiresApiKey: boolean
  environmentKeyName?: string
  apiKeyHelpUrl?: string
  iconName?: string
  sortOrder: number
  createdAt: string
  updatedAt?: string
  modelCount: number
  activeModelCount: number
  /** True when at least one active system (all-users) API key exists for this provider. */
  hasSystemKey: boolean
  /** True when at least one active model has isDefault = true for this provider. */
  hasDefaultModel: boolean
  /** JSON merged into every request body for all models of this provider. */
  extraBodyJson?: string
}

/**
 * Create AI Provider request
 * Matches AdminCreateAiProviderRequest from backend
 */
export interface CreateAiProviderRequest {
  name: string
  displayName: string
  description?: string
  baseUrl?: string
  isActive?: boolean
  requiresApiKey: boolean
  environmentKeyName?: string
  apiKeyHelpUrl?: string
  iconName?: string
  sortOrder: number
  extraBodyJson?: string
}

/**
 * Update AI Provider request
 * Matches AdminUpdateAiProviderRequest from backend
 */
export interface UpdateAiProviderRequest {
  name?: string
  displayName?: string
  description?: string
  baseUrl?: string
  isActive?: boolean
  requiresApiKey?: boolean
  environmentKeyName?: string
  apiKeyHelpUrl?: string
  iconName?: string
  sortOrder?: number
  extraBodyJson?: string
}

/**
 * AI Model admin model
 * Matches AiModelAdminResponse from backend
 */
export interface AiModelAdmin {
  id: string
  aiProviderId: string
  providerName: string
  modelId: string
  displayName: string
  description?: string
  isActive: boolean
  isDefault: boolean
  isPlanningModel: boolean
  maxTokens: number | null
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  costPer1MTokensInput?: number
  costPer1MTokensOutput?: number
  extraBodyJson?: string
  sortOrder: number
  createdAt: string
  updatedAt?: string
}
/**
 * Matches AdminCreateAiModelRequest from backend
 */
export interface CreateAiModelRequest {
  modelId: string
  displayName: string
  description?: string
  isActive: boolean
  isDefault: boolean
  isPlanningModel: boolean
  maxTokens?: number | null
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  costPer1MTokensInput?: number
  costPer1MTokensOutput?: number
  extraBodyJson?: string
  sortOrder: number
}

/**
 * Update AI Model request
 * Matches AdminUpdateAiModelRequest from backend
 */
export interface UpdateAiModelRequest {
  modelId?: string
  displayName?: string
  description?: string
  isActive?: boolean
  isDefault?: boolean
  isPlanningModel?: boolean
  maxTokens?: number
  supportsStreaming?: boolean
  supportsTools?: boolean
  supportsVision?: boolean
  costPer1MTokensInput?: number
  costPer1MTokensOutput?: number
  extraBodyJson?: string
  sortOrder?: number
}

/**
 * Global AI defaults response
 * Matches GlobalAiDefaultsResponse from backend
 */
export interface GlobalAiDefaults {
  defaultModel?: AiModelAdmin
  planningModel?: AiModelAdmin
  defaultMaxOutputTokens?: number
}

/**
 * Iteration limits config (per-complexity-tier)
 * Matches IterationLimitsConfigResponse from backend
 */
export interface IterationLimitsConfig {
  simple: number
  medium: number
  large: number
  enterprise: number
  autonomous: number
  planning: number
}

export interface HistoryCompactionConfig {
  recentMessages: number
  maxSummaryEntries: number
  maxSummaryChars: number
  maxEntryChars: number
}

/**
 * AI providers operational config
 * Matches AiProvidersOperationalConfigResponse from backend
 */
export interface AiProvidersOperationalConfig {
  maxConversationHistoryMessages: number
  maxIterations: number
  maxConsecutiveFailures: number
  defaultMaxOutputTokens: number
  appVerificationDefaultStrategy: string
  appVerificationAllowUserOverride: boolean
  appExecutionDefaultPolicy: string
  appExecutionAllowUserOverride: boolean
  iterationLimits: IterationLimitsConfig
  historyCompaction: HistoryCompactionConfig
}

/**
 * AI API Key admin model
 * Matches AiApiKeyAdminResponse from backend
 */
export interface AiApiKeyAdmin {
  id: string
  name: string
  providerName: string
  description?: string
  createdAt: string
  lastUsedAt?: string
  isActive: boolean
  /** True when the key was added by an admin and is available to all users. */
  isSystemKey: boolean
}

/**
 * Create AI API Key request
 * Matches AdminCreateAiApiKeyRequest from backend
 */
export interface CreateAiApiKeyRequest {
  name: string
  apiKey: string
  description?: string
}

/**
 * Update AI API Key request
 * Matches AdminUpdateAiApiKeyRequest from backend
 */
export interface UpdateAiApiKeyRequest {
  name?: string
  apiKey?: string
  description?: string
  isActive?: boolean
}

/**
 * AI Provider Option for admin dropdowns
 * Matches AiProviderOptionResponse from backend
 */
export interface AiProviderOption {
  providerName: string
  displayName: string
  description?: string
  iconName?: string
}

/**
 * AI Model Option for admin dropdowns
 * Matches AiModelOptionResponse from backend
 */
export interface AiModelOption {
  modelId: string
  displayName: string
}

// ── Subscription Plans ───────────────────────────────────────────────────────

export interface SubscriptionPlan {
  id: string
  name: string
  description?: string
  priceUsd: number
  stripeProductId?: string
  stripePriceId?: string
  isDefault: boolean
  isActive: boolean
  capabilities: PlanCapabilities
  sortOrder: number
  createdAt: string
  updatedAt?: string
}

export interface PlanCapabilities {
  version: number
  hostedRunner: HostedRunnerCapability
}

export interface HostedRunnerCapability {
  enabled: boolean
  cpuCores: number
  memoryMb: number
  diskMb: number
  maxApps: number
  visibility: 'disabled' | 'testUsersOnly' | 'public'
}

export interface CreateSubscriptionPlanRequest {
  name: string
  description?: string
  priceUsd: number
  stripeProductId?: string
  stripePriceId?: string
  isDefault: boolean
  isActive: boolean
  capabilities: PlanCapabilities
  sortOrder: number
}

export interface UpdateSubscriptionPlanRequest {
  name?: string
  description?: string
  priceUsd?: number
  stripeProductId?: string
  stripePriceId?: string
  isDefault?: boolean
  isActive?: boolean
  capabilities?: PlanCapabilities
  sortOrder?: number
}

export interface UserPlan {
  userId: string
  email: string
  name: string
  subscriptionPlanId?: string
  planName?: string
}

export interface AssignPlanRequest {
  planId?: string
}

// ── Hosted Runners ──────────────────────────────────────────────────────────

export interface RunnerHost {
  id: string
  name: string
  provider: string
  region?: string
  publicIp?: string
  isLocal: boolean
  status: 'active' | 'draining' | 'disabled'
  totalCpuCores: number
  usedCpuCores: number
  totalMemoryMb: number
  usedMemoryMb: number
  totalDiskMb: number
  usedDiskMb: number
  hostedRunnerCount: number
  createdAt: string
  updatedAt?: string
}

export interface CreateRunnerHostRequest {
  name: string
  provider: string
  region?: string
  publicIp?: string
  isLocal: boolean
  status: 'active' | 'draining' | 'disabled'
  totalCpuCores: number
  totalMemoryMb: number
  totalDiskMb: number
}

export interface UpdateRunnerHostRequest {
  name?: string
  provider?: string
  region?: string
  publicIp?: string
  isLocal?: boolean
  status?: 'active' | 'draining' | 'disabled'
  totalCpuCores?: number
  totalMemoryMb?: number
  totalDiskMb?: number
}

export interface HostedRunnerDeployment {
  id: string
  action: string
  status: string
  commandPreview?: string
  logs?: string
  error?: string
  startedAt: string
  finishedAt?: string
}

export interface HostedRunner {
  id: string
  userId: string
  userEmail: string
  subscriptionPlanId: string
  subscriptionPlanName: string
  runnerHostId?: string
  runnerHostName?: string
  runnerId?: string
  runnerName?: string
  apiKeyId?: string
  name: string
  status: 'pending' | 'provisioning' | 'running' | 'failed' | 'disabled' | 'deleting'
  cpuCores: number
  memoryMb: number
  diskMb: number
  maxApps: number
  containerName: string
  containerId?: string
  lastError?: string
  createdAt: string
  updatedAt?: string
  provisionedAt?: string
  latestDeployment?: HostedRunnerDeployment
}

export interface ProvisionHostedRunnerRequest {
  planId?: string
}
