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
}

/**
 * Update AI Provider request
 * Matches AdminUpdateAiProviderRequest from backend
 */
export interface UpdateAiProviderRequest {
  displayName?: string
  description?: string
  baseUrl?: string
  isActive?: boolean
  requiresApiKey?: boolean
  environmentKeyName?: string
  apiKeyHelpUrl?: string
  iconName?: string
  sortOrder?: number
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
  maxTokens: number
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  costPer1kTokensInput?: number
  costPer1kTokensOutput?: number
  sortOrder: number
  createdAt: string
  updatedAt?: string
}

/**
 * Create AI Model request
 * Matches AdminCreateAiModelRequest from backend
 */
export interface CreateAiModelRequest {
  modelId: string
  displayName: string
  description?: string
  isActive: boolean
  isDefault: boolean
  maxTokens: number
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  costPer1kTokensInput?: number
  costPer1kTokensOutput?: number
  sortOrder: number
}

/**
 * Update AI Model request
 * Matches AdminUpdateAiModelRequest from backend
 */
export interface UpdateAiModelRequest {
  displayName?: string
  description?: string
  isActive?: boolean
  isDefault?: boolean
  maxTokens?: number
  supportsStreaming?: boolean
  supportsTools?: boolean
  supportsVision?: boolean
  costPer1kTokensInput?: number
  costPer1kTokensOutput?: number
  sortOrder?: number
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
 * System AI Settings
 * Matches SystemAiSettingsResponse from backend
 */
export interface SystemAiSettings {
  defaultProviderName: string
  defaultModelId: string
  maxRequestsPerUserPerMinute: number
  isEnabled: boolean
  hasApiKey: boolean
  createdAt: string
  updatedAt?: string
}

/**
 * Update System AI Settings Request
 * Matches UpdateSystemAiSettingsRequest from backend
 */
export interface UpdateSystemAiSettingsRequest {
  defaultProviderName: string
  defaultModelId: string
  apiKey?: string
  maxRequestsPerUserPerMinute: number
  isEnabled: boolean
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

