/**
 * AI Chat History Service
 * Manages chat sessions and message persistence
 */
import axiosInstance from './axios-instance'
import type {
  ChatSession,
  ChatMessage,
  CreateChatSessionRequest,
  SaveChatMessageRequest,
} from '@/shared/types/ai.types'

export const aiChatHistoryService = {
  /**
   * Get all sessions for a resource
   */
  async getSessions(
    resourceType: string,
    resourceId: string,
  ): Promise<ChatSession[]> {
    const response = await axiosInstance.get('/api/AiChatHistory/sessions', {
      params: { resourceType, resourceId },
    })
    return response.data
  },

  /**
   * Get a specific session by ID
   */
  async getSession(sessionId: string): Promise<ChatSession> {
    const response = await axiosInstance.get(
      `/api/AiChatHistory/sessions/${sessionId}`,
    )
    return response.data
  },

  /**
   * Get messages for a session
   */
  async getSessionMessages(sessionId: string): Promise<ChatMessage[]> {
    const response = await axiosInstance.get(
      `/api/AiChatHistory/sessions/${sessionId}/messages`,
    )
    return response.data
  },

  /**
   * Get session with all its messages
   */
  async getSessionWithMessages(sessionId: string): Promise<{
    session: ChatSession
    messages: ChatMessage[]
  }> {
    const response = await axiosInstance.get(
      `/api/AiChatHistory/sessions/${sessionId}/full`,
    )
    return response.data
  },

  /**
   * Get or create active session for resource
   */
  async getOrCreateActiveSession(
    resourceType: string,
    resourceId: string,
    provider?: string,
    model?: string,
  ): Promise<ChatSession> {
    const response = await axiosInstance.get(
      '/api/AiChatHistory/sessions/active',
      {
        params: { resourceType, resourceId, provider, model },
      },
    )
    return response.data
  },

  /**
   * Create new session
   */
  async createSession(
    request: CreateChatSessionRequest,
  ): Promise<ChatSession> {
    const response = await axiosInstance.post(
      '/api/AiChatHistory/sessions',
      request,
    )
    return response.data
  },

  /**
   * Activate a session
   */
  async activateSession(sessionId: string): Promise<void> {
    await axiosInstance.put(
      `/api/AiChatHistory/sessions/${sessionId}/activate`,
    )
  },

  /**
   * Update session title
   */
  async updateSessionTitle(sessionId: string, title: string): Promise<void> {
    await axiosInstance.put(
      `/api/AiChatHistory/sessions/${sessionId}/title`,
      { title },
    )
  },

  /**
   * Delete session
   */
  async deleteSession(sessionId: string): Promise<void> {
    await axiosInstance.delete(`/api/AiChatHistory/sessions/${sessionId}`)
  },

  /**
   * Save a single message (for manual persistence if needed)
   */
  async saveMessage(request: SaveChatMessageRequest): Promise<ChatMessage> {
    const response = await axiosInstance.post(
      '/api/AiChatHistory/messages',
      request,
    )
    return response.data
  },

  /**
   * Save multiple messages in batch
   */
  async saveMessages(
    sessionId: string,
    messages: Array<{
      role: string
      content: string
      messageType?: string
      metadata?: string
    }>,
  ): Promise<ChatMessage[]> {
    const response = await axiosInstance.post(
      '/api/AiChatHistory/messages/batch',
      {
        sessionId,
        messages,
      },
    )
    return response.data
  },

  /**
   * Clear session messages
   */
  async clearSessionMessages(sessionId: string): Promise<void> {
    await axiosInstance.delete(
      `/api/AiChatHistory/sessions/${sessionId}/messages`,
    )
  },
}
