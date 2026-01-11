/**
 * AI Providers Service
 * Handles fetching AI providers, models, and API keys
 */
import axios from '../api/axios-instance'
import type { AiProvider, AiApiKey } from '@/shared/types/ai.types'

/**
 * Get available AI providers with their configuration
 */
export async function getAvailableProviders(
  includeEnvironment: boolean = false,
): Promise<AiProvider[]> {
  const { data } = await axios.get<AiProvider[]>('/api/aiApiKeys/providers', {
    params: { includeEnvironment },
  })
  return data
}

/**
 * Get user's AI API keys
 */
export async function getUserAiApiKeys(projectId?: string): Promise<AiApiKey[]> {
  const { data } = await axios.get<AiApiKey[]>('/api/aiApiKeys', {
    params: projectId ? { projectId } : undefined,
  })
  return data
}

/**
 * Get decrypted API key by ID (for MCP integration)
 */
export async function getDecryptedApiKey(keyId: string): Promise<string> {
  const { data } = await axios.post<{ apiKey: string }>(`/api/aiApiKeys/${keyId}/decrypt`)
  return data.apiKey
}

/**
 * Create a new AI API key
 */
export async function createAiApiKey(request: {
  name: string
  providerName: string
  apiKey: string
  description?: string
}): Promise<AiApiKey> {
  const { data } = await axios.post<AiApiKey>('/api/aiApiKeys', request)
  return data
}

/**
 * Delete an AI API key
 */
export async function deleteAiApiKey(keyId: string): Promise<void> {
  await axios.delete(`/api/aiApiKeys/${keyId}`)
}
