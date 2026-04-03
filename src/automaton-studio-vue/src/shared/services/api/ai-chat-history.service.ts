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
  AppChatStats,
  PagedResult,
} from '@/shared/types/ai.types'

function parseMetadata(metadata: string | null | undefined): any {
  if (!metadata) return null
  try { return JSON.parse(metadata) } catch { return null }
}

function mapMessageType(serverType: string | undefined): ChatMessage['type'] {
  const map: Record<string, ChatMessage['type']> = {
    Text: 'text',
    ToolExecution: 'toolExecution',
    OperationComplete: 'codeSuccess',
    CodeError: 'codeError',
  }
  return serverType ? (map[serverType] ?? 'text') : 'text'
}

function mapServerMessage(m: any): ChatMessage {
  const meta = parseMetadata(m.metadata)
  const mapped: ChatMessage & { toolCallId?: string } = {
    id: m.id || crypto.randomUUID(),
    role: m.role,
    content: m.content,
    type: mapMessageType(m.messageType),
    timestamp: new Date(m.createdAt || Date.now()),
    toolName: meta?.toolName,
    toolArgs: meta?.arguments,
    toolStatus: m.messageType === 'ToolExecution' ? 'completed' : undefined,
    inputTokens: m.inputTokens,
    outputTokens: m.outputTokens,
    model: m.model,
  }

  if (meta?.toolCallId) {
    mapped.toolCallId = String(meta.toolCallId)
  }

  return mapped
}

export const aiChatHistoryService = {
  /**
   * Get all sessions for a resource
   */
  async getSessions(
    resourceType: string,
    resourceId: string,
    page = 1,
    pageSize = 20,
    search?: string,
  ): Promise<PagedResult<ChatSession>> {
    const response = await axiosInstance.get('/api/AiChatHistory/sessions', {
      params: { resourceType, resourceId, page, pageSize, search: search || undefined },
    })
    return response.data
  },

  /**
   * Export all sessions/messages for a resource as a downloadable JSON file
   */
  async exportSessions(
    resourceType: string,
    resourceId: string,
  ): Promise<{ blob: Blob; fileName: string }> {
    const response = await axiosInstance.get(
      '/api/AiChatHistory/sessions/export',
      {
        params: { resourceType, resourceId },
        responseType: 'blob',
      },
    )

    const disposition = response.headers['content-disposition'] as string | undefined
    const fileNameMatch = disposition?.match(/filename="?([^\"]+)"?/i)
    const fileName = fileNameMatch?.[1] || `ai-chat-sessions-${resourceType.toLowerCase()}-${resourceId}.json`

    return {
      blob: response.data,
      fileName,
    }
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
    const mappedMessages = (response.data as any[]).map(mapServerMessage)

    // Stitch role='tool' messages back into their originating ToolExecution message
    // so history views can show what the tool actually read/returned.
    const toolExecutionsByCallId = new Map<string, ChatMessage>()
    for (const message of mappedMessages) {
      if (message.type === 'toolExecution') {
        const toolCallId = (message as ChatMessage & { toolCallId?: string }).toolCallId
        if (toolCallId) {
          toolExecutionsByCallId.set(toolCallId, message)
        }
      }
    }

    for (const message of mappedMessages) {
      if (message.role !== 'tool') continue

      const toolCallId = (message as ChatMessage & { toolCallId?: string }).toolCallId
      if (!toolCallId) continue

      const executionMessage = toolExecutionsByCallId.get(toolCallId)
      if (!executionMessage) continue

      executionMessage.toolResult = message.content
      if (message.type === 'codeError') {
        executionMessage.toolStatus = 'error'
        executionMessage.error = message.content || 'Tool execution failed'
      } else if (executionMessage.toolStatus !== 'error') {
        executionMessage.toolStatus = 'completed'
      }
    }

    return mappedMessages
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

  /**
   * Get aggregate chat statistics for an app
   */
  async getAppChatStats(appId: string): Promise<AppChatStats> {
    const response = await axiosInstance.get(
      `/api/AiChatHistory/apps/${appId}/stats`,
    )
    return response.data
  },
}
