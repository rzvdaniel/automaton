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
  infrastructureRunnerId?: string
  infrastructureRunnerName?: string
  infrastructureRunnerPurpose?: string
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

export interface AiGuidanceAdmin {
  id: string
  key: string
  title: string
  surface: string
  documentKind: string
  description?: string
  tagsJson?: string
  outputPath?: string
  contentMarkdown: string
  isEnabled: boolean
  requiredByDefault: boolean
  isModelLoadable: boolean
  sortOrder: number
  version: number
  createdAt: string
  updatedAt?: string
  hasEmbeddedDefault: boolean
}

export interface UpsertAiGuidanceRequest {
  key: string
  title: string
  surface: string
  documentKind: string
  description?: string
  tagsJson?: string
  outputPath?: string
  contentMarkdown: string
  isEnabled: boolean
  requiredByDefault: boolean
  isModelLoadable: boolean
  sortOrder: number
}

export interface AppDeployRulesSettings {
  sourceExclude: string[]
  frontendExclude: string[]
  backendExclude: string[]
  remixSourceExclude: string[]
  remixFrontendExclude: string[]
}

export interface AdminUser {
  id: string
  name: string
  email: string
  emailConfirmed: boolean
  publicUsername?: string
  registeredAtUtc?: string
  lastLoginAtUtc?: string
  registrationProvider?: string
  isLocked: boolean
  appCount: number
  publishedAppCount: number
  privateAppCount: number
  lastAppName?: string
  runnerCount: number
  onlineRunnerCount: number
  lastRunnerHeartbeatAtUtc?: string
  apps: AdminUserApp[]
  runners: AdminUserRunner[]
  roles: string[]
  accessFailedCount?: number
}

export interface AdminUserApp {
  id: string
  name: string
  isPublished: boolean
}

export interface AdminUserRunner {
  id: string
  name: string
  status: string
  lastHeartbeatAtUtc?: string
  lastConnectedAtUtc?: string
  version?: string
}

export interface AdminUserSummary {
  totalCount: number
  verifiedCount: number
  activeCount: number
  joinedTodayCount: number
  joinedThisWeekCount: number
  joinedThisMonthCount: number
}

export interface AdminUserPage {
  items: AdminUser[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  summary: AdminUserSummary
}

export interface AdminDashboardSummary {
  totalUsers: number
  newRegistrations: number
  publishedApps: number
  activeRunners: number
  executionsToday: number
  failedExecutions: number
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
  canAccessHostedRunnerTestPlans: boolean
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
  status: 'pending' | 'provisioning' | 'running' | 'failed' | 'disabled' | 'deleting' | 'deleteFailed' | 'deleted'
  cpuCores: number
  memoryMb: number
  diskMb: number
  maxApps: number
  containerName: string
  containerId?: string
  runnerConnectivityStatus: string
  runnerLastHeartbeatAtUtc?: string
  runnerLastConnectedAtUtc?: string
  runnerVersion?: string
  runnerMachineName?: string
  lastError?: string
  createdAt: string
  updatedAt?: string
  provisionedAt?: string
  deletedAt?: string
  latestDeployment?: HostedRunnerDeployment
}

export interface ProvisionHostedRunnerRequest {
  planId?: string
}

export interface AdminAppDetails {
  app: import('@/features/apps/types/apps.types').App
  ownerUserId: string
  ownerName: string
  ownerEmail: string
  ownerPublicUsername?: string
  executionRunnerName?: string
  projectName?: string
  sourceFileCount: number
  publishedFileCount: number
  backendFileCount: number
  totalSizeBytes: number
}

export interface AdminAppModerationRequest {
  reviewStatus?: string
  accessStatus?: string
  marketplaceModerationReason?: string
}
