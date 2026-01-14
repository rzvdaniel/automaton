/**
 * useAiChat Composable
 * Manages AI chat state and streaming conversations with session persistence
 */
import { ref, computed } from 'vue'
import { aiConversationClient } from '../services/ai/ai-conversation-client'
import { aiChatHistoryService } from '../services/api/ai-chat-history.service'
import { getAvailableProviders } from '../services/ai/ai-providers-service'
import type {
  ChatMessage,
  ChatSession,
  AiProvider,
  StreamConversationRequest,
  SseConversationUpdate,
  ConversationMessage,
  ToolCall,
  ToolResult,
} from '../types/ai.types'

export interface UseAiChatOptions {
  /**
   * Callback when a tool is executed
   * @param toolName - Name of the MCP tool that was called
   * @param args - Arguments passed to the tool
   * @param result - Result returned from the tool (if available)
   */
  onToolExecuted?: (toolName: string, args: any, result?: any) => void | Promise<void>
}

export function useAiChat(resourceType: string, resourceId: string, options?: UseAiChatOptions) {
  // State
  const messages = ref<ChatMessage[]>([])
  const isGenerating = ref(false)
  const selectedProvider = ref<string>('')
  const selectedModel = ref<string>('')
  const availableProviders = ref<AiProvider[]>([])
  const error = ref<string | null>(null)
  const conversationId = ref<string | null>(null)

  // Session state
  const sessions = ref<ChatSession[]>([])
  const activeSession = ref<ChatSession | null>(null)
  const isLoadingSessions = ref(false)
  const isLoadingMessages = ref(false)

  // Computed
  const hasMessages = computed(() => messages.value.length > 0)
  const currentProvider = computed(() =>
    availableProviders.value.find((p) => p.providerName === selectedProvider.value),
  )
  const availableModels = computed(() => currentProvider.value?.supportedModels || [])

  /**
   * Load available AI providers
   */
  async function loadProviders() {
    try {
      availableProviders.value = await getAvailableProviders(true)

      // Auto-select first provider with API key
      const providerWithKey = availableProviders.value.find(
        (p) => p.hasStoredKeys || p.hasEnvironmentKey,
      )
      if (providerWithKey) {
        selectedProvider.value = providerWithKey.providerName
        // Select first model
        if (providerWithKey.supportedModels.length > 0) {
          selectedModel.value = providerWithKey.supportedModels[0] || ''
        }
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load AI providers'
      console.error('Error loading providers:', err)
    }
  }

  /**
   * Load all sessions for this resource
   */
  async function loadSessions() {
    isLoadingSessions.value = true
    try {
      sessions.value = await aiChatHistoryService.getSessions(resourceType, resourceId)
      
      // Set active session if one exists
      const active = sessions.value.find((s) => s.isActive)
      if (active) {
        activeSession.value = active
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load sessions'
      console.error('Error loading sessions:', err)
    } finally {
      isLoadingSessions.value = false
    }
  }

  /**
   * Get or create active session
   */
  async function getOrCreateActiveSession() {
    try {
      activeSession.value = await aiChatHistoryService.getOrCreateActiveSession(
        resourceType,
        resourceId,
        selectedProvider.value || undefined,
        selectedModel.value || undefined,
      )
      
      // Load messages from server
      if (activeSession.value) {
        await loadSessionMessages(activeSession.value.id)
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to get active session'
      console.error('Error getting active session:', err)
    }
  }

  /**
   * Load messages for a session
   */
  async function loadSessionMessages(sessionId: string) {
    isLoadingMessages.value = true
    try {
      const serverMessages = await aiChatHistoryService.getSessionMessages(sessionId)
      
      // Convert server messages to chat messages
      messages.value = serverMessages.map((m: any) => ({
        id: m.id || crypto.randomUUID(),
        role: m.role,
        content: m.content,
        type: mapMessageType(m.messageType),
        timestamp: new Date(m.createdAt || Date.now()),
        toolName: m.metadata ? parseToolMetadata(m.metadata)?.toolName : undefined,
        toolStatus: m.messageType === 'ToolExecution' ? 'completed' : undefined,
      }))
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load messages'
      console.error('Error loading messages:', err)
    } finally {
      isLoadingMessages.value = false
    }
  }

  /**
   * Map backend message type to frontend message type
   */
  function mapMessageType(messageType: string): ChatMessage['type'] {
    switch (messageType) {
      case 'ToolExecution':
        return 'toolExecution'
      case 'CodeSuccess':
        return 'codeSuccess'
      case 'CodeError':
        return 'codeError'
      case 'Generating':
        return 'generating'
      case 'System':
        return 'system'
      case 'Reasoning':
        return 'reasoning'
      default:
        return 'text'
    }
  }

  /**
   * Parse tool metadata
   */
  function parseToolMetadata(metadata: string): any {
    try {
      return JSON.parse(metadata)
    } catch {
      return null
    }
  }

  /**
   * Create a new session
   */
  async function createSession(title?: string) {
    try {
      const session = await aiChatHistoryService.createSession({
        resourceType: resourceType as any,
        resourceId,
        title: title || `Chat ${new Date().toLocaleString()}`,
        provider: selectedProvider.value || undefined,
        model: selectedModel.value || undefined,
      })
      
      sessions.value.push(session)
      await activateSession(session.id)
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to create session'
      console.error('Error creating session:', err)
    }
  }

  /**
   * Activate a different session
   */
  async function activateSession(sessionId: string) {
    try {
      await aiChatHistoryService.activateSession(sessionId)
      
      // Update local state
      activeSession.value = sessions.value.find((s) => s.id === sessionId) || null
      
      // Update isActive flag in sessions list
      sessions.value.forEach((s) => {
        s.isActive = s.id === sessionId
      })
      
      if (activeSession.value) {
        await loadSessionMessages(sessionId)
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to activate session'
      console.error('Error activating session:', err)
    }
  }

  /**
   * Delete a session
   */
  async function deleteSession(sessionId: string) {
    try {
      await aiChatHistoryService.deleteSession(sessionId)
      sessions.value = sessions.value.filter((s) => s.id !== sessionId)

      // If deleted session was active, create or get another one
      if (activeSession.value?.id === sessionId) {
        activeSession.value = null
        messages.value = []
        await getOrCreateActiveSession()
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to delete session'
      console.error('Error deleting session:', err)
    }
  }

  /**
   * Update session title
   */
  async function updateSessionTitle(sessionId: string, title: string) {
    try {
      await aiChatHistoryService.updateSessionTitle(sessionId, title)
      
      // Update local state
      const session = sessions.value.find((s) => s.id === sessionId)
      if (session) {
        session.title = title
      }
      if (activeSession.value?.id === sessionId) {
        activeSession.value.title = title
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to update session title'
      console.error('Error updating session title:', err)
    }
  }

  /**
   * Clear session messages
   */
  async function clearSessionMessages() {
    if (!activeSession.value) return
    
    try {
      await aiChatHistoryService.clearSessionMessages(activeSession.value.id)
      messages.value = []
      conversationId.value = null
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to clear messages'
      console.error('Error clearing messages:', err)
    }
  }

  /**
   * Send a message to the AI
   */
  async function sendMessage(userMessage: string, systemPrompt?: string, context?: Record<string, any>) {
    if (!userMessage.trim() || isGenerating.value) return
    if (!selectedProvider.value) {
      error.value = 'Please select an AI provider'
      return
    }

    // Ensure we have an active session
    if (!activeSession.value) {
      await getOrCreateActiveSession()
    }

    // Add user message to chat
    const userChatMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: 'user',
      content: userMessage,
      timestamp: new Date(),
    }
    messages.value.push(userChatMessage)

    // Reset error
    error.value = null
    isGenerating.value = true

    // Build conversation history
    const history: ConversationMessage[] = messages.value
      .filter((m) => m.role !== 'system')
      .map((m) => ({
        role: m.role,
        content: m.content,
      }))

    // Build request with session ID and resource context
    const request: StreamConversationRequest = {
      conversationId: conversationId.value || undefined,
      provider: selectedProvider.value,
      model: selectedModel.value || undefined,
      message: userMessage,
      systemPrompt,
      history: history.slice(0, -1), // Exclude the message we just added
      sessionId: activeSession.value?.id,
      resourceType: resourceType, // Top-level property for backend
      resourceId: resourceId, // Top-level property for backend
      language: context?.language,
      context: {
        ...context, // Additional context (e.g., runnerId, appId)
      },
    }

    try {
      await aiConversationClient.startConversation(request, {
        onUpdate: handleConversationUpdate,
        onDone: (data) => {
          conversationId.value = data.conversationId
          isGenerating.value = false

          // Update active session ID if backend created a new session
          if (data.sessionId && activeSession.value) {
            activeSession.value.id = data.sessionId
          }

          // Mark all streaming messages as complete
          messages.value.forEach((m) => {
            if (m.isStreaming) {
              m.isStreaming = false
            }
          })
        },
        onError: (data) => {
          error.value = data.error
          isGenerating.value = false

          // Add error message to chat
          messages.value.push({
            id: crypto.randomUUID(),
            role: 'assistant',
            type: 'codeError',
            content: data.error,
            timestamp: new Date(),
          })
        },
        onCancelled: () => {
          isGenerating.value = false
        },
      })
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to send message'
      isGenerating.value = false
      console.error('Error sending message:', err)
    }
  }

  /**
   * Handle conversation update from SSE stream
   */
  function handleConversationUpdate(update: SseConversationUpdate) {
    // Process new messages
    if (update.messages && update.messages.length > 0) {
      for (const message of update.messages) {
        // Only add assistant messages (user message already added)
        if (message.role === 'assistant') {
          const existingMessage = messages.value.find(
            (m) => m.role === 'assistant' && m.isStreaming,
          )

          if (existingMessage) {
            // Update existing streaming message
            existingMessage.content = message.content
          } else {
            // Add new assistant message
            messages.value.push({
              id: crypto.randomUUID(),
              role: 'assistant',
              content: message.content,
              timestamp: new Date(),
              isStreaming: true,
            })
          }
        }
      }
    }

    // Process tool calls
    if (update.toolCalls && update.toolCalls.length > 0) {
      for (const toolCall of update.toolCalls) {
        handleToolCall(toolCall)
      }
    }

    // Process tool results
    if (update.toolResults && update.toolResults.length > 0) {
      for (const toolResult of update.toolResults) {
        handleToolResult(toolResult)
      }
    }
  }

  /**
   * Handle tool call execution
   */
  function handleToolCall(toolCall: ToolCall) {
    console.log('[useAiChat] handleToolCall received:', toolCall)

    // Backend sends normalized format for all AI providers (OpenAI, Anthropic, Google, etc.)
    // Properties are capitalized (Name, Arguments) due to C# naming conventions
    const toolName = (toolCall as any).Name || toolCall.name || 'unknown'
    const toolArgs = (toolCall as any).Arguments || toolCall.arguments || {}

    console.log('[useAiChat] Parsed tool:', { toolName, toolArgs })

    messages.value.push({
      id: toolCall.id,
      role: 'assistant',
      type: 'toolExecution',
      content: `Executing ${toolName}...`,
      toolName,
      toolArgs, // Store args for later use
      toolStatus: 'executing',
      timestamp: new Date(),
    })

    // Notify tool execution callback
    if (options?.onToolExecuted) {
      try {
        options.onToolExecuted(toolName, toolArgs)
      } catch (err) {
        console.error('Error in onToolExecuted callback:', err)
      }
    }
  }

  /**
   * Handle tool result
   */
  function handleToolResult(toolResult: ToolResult) {
    const toolMessage = messages.value.find(
      (m) => m.id === toolResult.toolCallId && m.type === 'toolExecution',
    )

    if (toolMessage) {
      toolMessage.toolStatus = toolResult.isError ? 'error' : 'completed'
      toolMessage.toolResult = toolResult.content
      if (toolResult.isError) {
        toolMessage.error = toolResult.content
      }

      // Notify tool execution callback with result
      if (options?.onToolExecuted && toolMessage.toolName) {
        try {
          // Parse result content if it's JSON
          let resultData: any = toolResult.content
          try {
            resultData = JSON.parse(toolResult.content)
          } catch {
            // Not JSON, use as-is
          }

          // Use stored args from tool call
          options.onToolExecuted(toolMessage.toolName, toolMessage.toolArgs || {}, resultData)
        } catch (err) {
          console.error('Error in onToolExecuted callback:', err)
        }
      }
    }
  }

  /**
   * Clear all messages (legacy method - use clearSessionMessages instead)
   */
  function clearMessages() {
    messages.value = []
    conversationId.value = null
    error.value = null
  }

  /**
   * Cancel ongoing generation
   */
  function cancelGeneration() {
    aiConversationClient.close()
    isGenerating.value = false
  }

  return {
    // State
    messages,
    isGenerating,
    selectedProvider,
    selectedModel,
    availableProviders,
    availableModels,
    error,
    hasMessages,
    currentProvider,

    // Session state
    sessions,
    activeSession,
    isLoadingSessions,
    isLoadingMessages,

    // Methods
    loadProviders,
    sendMessage,
    clearMessages,
    cancelGeneration,

    // Session methods
    loadSessions,
    getOrCreateActiveSession,
    createSession,
    activateSession,
    deleteSession,
    updateSessionTitle,
    clearSessionMessages,
    loadSessionMessages,
  }
}
