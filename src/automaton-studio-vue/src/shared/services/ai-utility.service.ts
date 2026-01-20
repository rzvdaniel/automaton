import apiClient from './api/axios-instance'

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
   * Check if the system AI service is available.
   * @returns Whether the service is available
   */
  async checkStatus(): Promise<boolean> {
    try {
      const response = await apiClient.get<{ isAvailable: boolean }>('/api/aiutility/status')
      return response.data.isAvailable
    } catch {
      return false
    }
  }
}

export const aiUtilityService = new AiUtilityService()
