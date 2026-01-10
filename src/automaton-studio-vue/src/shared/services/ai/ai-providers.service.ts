/**
 * AI Providers Service
 * Fetches available AI providers and models from the backend
 */
import axiosInstance from '../api/axios-instance'

export interface AiApiKeyModel {
  id: string
  name: string
  providerName: string
  description?: string
  createdAt: string
  lastUsedAt?: string
  isActive: boolean
}

export interface AiProviderInfo {
  providerName: string
  displayName: string
  description?: string
  iconName?: string
  apiKeyHelpUrl?: string
  hasStoredKeys: boolean
  storedKeyCount: number
  hasEnvironmentKey: boolean
  storedKeys: AiApiKeyModel[]
  supportedModels: string[]
}

/**
 * Get available AI providers with their configuration status
 */
export async function getAvailableProviders(
  includeEnvironment: boolean = true,
): Promise<AiProviderInfo[]> {
  const response = await axiosInstance.get<AiProviderInfo[]>('/api/AiApiKeys/providers', {
    params: { includeEnvironment },
  })
  return response.data
}

/**
 * Get providers that have at least one API key configured
 */
export async function getConfiguredProviders(): Promise<AiProviderInfo[]> {
  const providers = await getAvailableProviders(true)
  return providers.filter((p) => p.hasStoredKeys || p.hasEnvironmentKey)
}

/**
 * Get the first available API key ID for a provider
 */
export function getFirstApiKeyId(provider: AiProviderInfo): string | undefined {
  if (provider.storedKeys.length > 0) {
    // Prefer most recently used
    const sorted = [...provider.storedKeys].sort((a, b) => {
      const aDate = a.lastUsedAt || a.createdAt
      const bDate = b.lastUsedAt || b.createdAt
      return new Date(bDate).getTime() - new Date(aDate).getTime()
    })
    return sorted[0].id
  }
  return undefined
}

/**
 * Get a user-friendly model name
 */
export function getModelDisplayName(modelId: string): string {
  const modelNames: Record<string, string> = {
    'gpt-4o': 'GPT-4o',
    'gpt-4o-mini': 'GPT-4o Mini',
    'gpt-4-turbo': 'GPT-4 Turbo',
    'gpt-4': 'GPT-4',
    'gpt-3.5-turbo': 'GPT-3.5 Turbo',
    'claude-3-5-sonnet-20241022': 'Claude 3.5 Sonnet',
    'claude-3-opus-20240229': 'Claude 3 Opus',
    'claude-3-sonnet-20240229': 'Claude 3 Sonnet',
    'claude-3-haiku-20240307': 'Claude 3 Haiku',
    'gemini-1.5-pro': 'Gemini 1.5 Pro',
    'gemini-1.5-flash': 'Gemini 1.5 Flash',
    'gemini-pro': 'Gemini Pro',
  }
  return modelNames[modelId] || modelId
}

/**
 * Get provider icon class
 */
export function getProviderIcon(providerName: string): string {
  const icons: Record<string, string> = {
    OpenAI: 'pi pi-box',
    Anthropic: 'pi pi-sparkles',
    Google: 'pi pi-google',
  }
  return icons[providerName] || 'pi pi-cog'
}

export const aiProvidersService = {
  getAvailableProviders,
  getConfiguredProviders,
  getFirstApiKeyId,
  getModelDisplayName,
  getProviderIcon,
}

export default aiProvidersService
