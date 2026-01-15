/**
 * AI Conversation Service
 * REST API calls for AI conversation endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'

class AiConversationService {
  /**
   * Test endpoint to verify authentication and routing
   * GET /api/ai/conversation/test
   */
  async test(): Promise<{
    message: string
    userId: string
    authenticated: boolean
    timestamp: string
  }> {
    const { data } = await apiClient.get('/api/ai/conversation/test')
    return data
  }
}

export const aiConversationService = new AiConversationService()
