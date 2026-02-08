import apiClient from './api/axios-instance'

/**
 * System AI status response
 */
export interface SystemAiStatus {
  isAvailable: boolean
  provider?: string
  model?: string
}

/**
 * Service for AI utility endpoints that use the system AI service.
 * These endpoints use Automaton's API key, not the user's.
 */
class AiUtilityService {
  /**
   * Generate a short, catchy app name from a description.
   * @param description The user's description of the app
   * @returns The AI-generated app name
   */
  async generateAppName(description: string): Promise<string> {
    const response = await apiClient.post<{ name: string }>('/api/aiutility/generate-app-name', {
      description,
    })
    return response.data.name
  }

  /**
   * Generate a concise session name from a chat prompt.
   * @param prompt The first message/prompt in the chat session
   * @returns The AI-generated session name
   */
  async generateSessionName(prompt: string): Promise<string> {
    const response = await apiClient.post<{ name: string }>(
      '/api/aiutility/generate-session-name',
      {
        prompt,
      },
    )
    return response.data.name
  }

  /**
   * Check if the system AI service is available and get its config.
   * @returns System AI status including provider and model info
   */
  async getStatus(): Promise<SystemAiStatus> {
    try {
      const response = await apiClient.get<SystemAiStatus>('/api/aiutility/status')
      return response.data
    } catch {
      return { isAvailable: false }
    }
  }
}

export const aiUtilityService = new AiUtilityService()
