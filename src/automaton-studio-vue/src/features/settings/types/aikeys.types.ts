/**
 * AI API Keys feature types
 * TypeScript interfaces for AI API keys data structures
 */

/**
 * AI API Key model
 * Matches AiApiKeyModel from AiApiKeyModels.cs
 */
export interface AiApiKey {
  id: string
  name: string
  providerName: string
  description?: string
  createdAt: string // ISO date string
  lastUsedAt?: string // ISO date string
  isActive: boolean
}

/**
 * AI Provider information
 * Matches AiProviderInfo from AiApiKeyModels.cs
 */
export interface AiProviderInfo {
  providerName: string
  displayName: string
  description?: string
  iconName?: string
  apiKeyHelpUrl?: string
  isImplemented: boolean
  hasStoredKeys: boolean
  storedKeyCount: number
  hasEnvironmentKey: boolean
  storedKeys: AiApiKey[]
  supportedModels: string[]
}

/**
 * Request to create a new AI API key
 * Matches CreateAiApiKeyRequest from AiApiKeyModels.cs
 */
export interface CreateAiApiKeyRequest {
  name: string
  providerName: string
  apiKey: string
  description?: string
}

/**
 * Get provider icon based on provider name
 */
export function getProviderIcon(providerName: string): string {
  const provider = providerName.toLowerCase()
  switch (provider) {
    case 'openai':
      return 'pi pi-sparkles'
    case 'anthropic':
      return 'pi pi-sparkles'
    case 'google':
      return 'pi pi-google'
    default:
      return 'pi pi-sparkles'
  }
}

/**
 * Get provider color for visual distinction
 */
export function getProviderColor(providerName: string): string {
  const provider = providerName.toLowerCase()
  switch (provider) {
    case 'openai':
      return '#10a37f'
    case 'anthropic':
      return '#d97757'
    case 'google':
      return '#4285f4'
    default:
      return '#6366f1'
  }
}
