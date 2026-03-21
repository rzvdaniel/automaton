/**
 * useAiChat Composable
 * Manages AI chat state and streaming conversations with session persistence
 */
import { ref, computed, watch } from 'vue'
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
  const isLoadingMoreSessions = ref(false)
  const isLoadingMessages = ref(false)
  const sessionsTotalCount = ref(0)
  const sessionsPage = ref(1)
  const sessionsPageSize = 20
  const hasMoreSessions = computed(() => sessions.value.length < sessionsTotalCount.value)
  const sessionsSearch = ref('')

  // LocalStorage key for persisting user's model preference
  const AI_MODEL_PREFERENCE_KEY = 'ai_preferred_model'

  // Flag to prevent saving to localStorage during initialization
  const isInitializingModel = ref(false)

  // Chat mode: defaults to 'agent'. Switches to 'plan' when the AI calls request_plan
  // (auto-detected complexity) or when the user manually toggles. Switches back to
  // 'agent' automatically after propose_plan completes.
  const chatMode = ref<'plan' | 'agent'>('agent')

  // Computed
  const hasMessages = computed(() => messages.value.length > 0)
  const currentProvider = computed(() =>
    availableProviders.value.find((p) => p.providerName === selectedProvider.value),
  )
  const availableModels = computed(() => {
    const models: string[] = []
    for (const provider of availableProviders.value) {
      for (const model of provider.supportedModels) {
        if (!models.includes(model)) {
          models.push(model)
        }
      }
    }
    return models.sort((a, b) => a.localeCompare(b))
  })

  const providerByModel = computed(() => {
    const map = new Map<string, string>()
    for (const provider of availableProviders.value) {
      for (const model of provider.supportedModels) {
        if (!map.has(model)) {
          map.set(model, provider.providerName)
        }
      }
    }
    return map
  })

  function ensureValidModelSelection() {
    const models = availableModels.value
    
    if (models.length === 0) {
      selectedModel.value = ''
      selectedProvider.value = ''
      return
    }

    // If already valid, keep current selection
    if (selectedModel.value && models.includes(selectedModel.value)) {
      return
    }

    // Set flag to prevent saving during initialization
    isInitializingModel.value = true

    // Priority: localStorage > default model > first alphabetically
    
    // 1. Check localStorage for user's saved preference
    const savedModel = localStorage.getItem(AI_MODEL_PREFERENCE_KEY)
    if (savedModel && models.includes(savedModel)) {
      selectedModel.value = savedModel
      selectedProvider.value = providerByModel.value.get(savedModel) || ''
      isInitializingModel.value = false
      return
    }

    // 2. Use default model from backend (first provider with a default)
    for (const provider of availableProviders.value) {
      if (provider.defaultModel && models.includes(provider.defaultModel)) {
        selectedModel.value = provider.defaultModel
        selectedProvider.value = providerByModel.value.get(provider.defaultModel) || ''
        isInitializingModel.value = false
        return
      }
    }

    // 3. Fallback to first model alphabetically
    selectedModel.value = models[0]!
    selectedProvider.value = providerByModel.value.get(models[0]!) || ''
    isInitializingModel.value = false
  }

  /**
   * Load available AI providers with keys and models.
   */
  async function loadProviders() {
    try {
      const userProviders = await getAvailableProviders(true)

      const providers = userProviders.filter(
        (p) => (p.hasStoredKeys || p.hasEnvironmentKey) && p.supportedModels.length > 0,
      )
      availableProviders.value = providers

      if (providers.length > 0) {
        ensureValidModelSelection()
      } else {
        selectedProvider.value = ''
        selectedModel.value = ''
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load AI providers'
      console.error('Error loading providers:', err)
    }
  }

  watch(selectedModel, (newModel) => {
    if (newModel) {
      // Update provider based on model
      selectedProvider.value = providerByModel.value.get(newModel) || ''
      
      // Only save user's preference to localStorage when not initializing
      // This prevents overwriting during automatic model selection on page load
      if (!isInitializingModel.value) {
        localStorage.setItem(AI_MODEL_PREFERENCE_KEY, newModel)
      }
    }
  })

  watch(availableProviders, () => {
    ensureValidModelSelection()
  })

  /**
   * Load sessions (page 1) — resets the list and applies optional search filter
   */
  async function loadSessions(search?: string) {
    isLoadingSessions.value = true
    sessionsPage.value = 1
    sessionsSearch.value = search ?? ''
    try {
      const result = await aiChatHistoryService.getSessions(
        resourceType, resourceId, 1, sessionsPageSize, sessionsSearch.value || undefined
      )
      sessions.value = result.items
      sessionsTotalCount.value = result.totalCount
      
      const active = sessions.value.find((s) => s.isActive)
      if (active) activeSession.value = active
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load sessions'
      console.error('Error loading sessions:', err)
    } finally {
      isLoadingSessions.value = false
    }
  }

  /**
   * Append the next page of sessions (infinite scroll)
   */
  async function loadMoreSessions() {
    if (!hasMoreSessions.value || isLoadingMoreSessions.value) return
    isLoadingMoreSessions.value = true
    const nextPage = sessionsPage.value + 1
    try {
      const result = await aiChatHistoryService.getSessions(
        resourceType, resourceId, nextPage, sessionsPageSize, sessionsSearch.value || undefined
      )
      sessions.value = [...sessions.value, ...result.items]
      sessionsTotalCount.value = result.totalCount
      sessionsPage.value = nextPage
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load more sessions'
      console.error('Error loading more sessions:', err)
    } finally {
      isLoadingMoreSessions.value = false
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
      
      // Skip role='tool' messages — raw tool output only needed by AI as context, not meaningful for users.
      messages.value = serverMessages.filter((m: any) => m.role !== 'tool')

      // Always resume in agent mode — the user has prior context and can ask for planning explicitly
      // or let the AI trigger it via request_plan if needed.
      chatMode.value = 'agent'
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

      // Activate on the server
      await aiChatHistoryService.activateSession(session.id)

      // Update local state without loading messages — new sessions are always empty,
      // and callers may have already added an optimistic user message we must not clear.
      sessions.value.forEach((s) => {
        s.isActive = s.id === session.id
      })
      activeSession.value = session
      if (activeSession.value) activeSession.value.isActive = true
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

      // If deleted session was active, clear it but don't create a new one
      // New session will be created lazily when user sends the first message
      if (activeSession.value?.id === sessionId) {
        activeSession.value = null
        messages.value = []
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
   * @param userMessageAlreadyAdded - set true when the caller has already pushed the user
   *   message into `messages` optimistically, so we don't add it a second time.
   */
  async function sendMessage(userMessage: string, systemPrompt?: string, context?: Record<string, any>, userMessageAlreadyAdded = false) {
    if (!userMessage.trim() || isGenerating.value) return
    if (!selectedModel.value) {
      error.value = 'Please select an AI model'
      return
    }

    // Ensure we have an active session
    if (!activeSession.value) {
      await getOrCreateActiveSession()
    }

    // Capture mode before the request starts.
    const modeForThisRequest = chatMode.value
    // Track whether propose_plan completed during this response.
    let proposePlanCompleted = false
    // Track whether request_plan was called (AI-driven escalation to plan mode).
    let requestPlanTriggered = false
    // Add user message to chat (skip when caller already added it optimistically)
    const userChatMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: 'user',
      content: userMessage,
      timestamp: new Date(),
    }
    if (!userMessageAlreadyAdded) {
      messages.value.push(userChatMessage)
    }

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
      mode: modeForThisRequest,
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

          // Mark all streaming messages as complete and stamp the actual model used.
          // (data.model comes from the backend — may differ from selectedModel due to plan mode override, token budget, etc.)
          // IMPORTANT: only stamp messages that were actively streaming — NOT old history messages
          // that happen to have no model saved (they'd incorrectly show the current model).
          const usedModel = data.model || selectedModel.value || undefined
          messages.value.forEach((m) => {
            if (m.isStreaming) {
              if (!m.model && usedModel) {
                m.model = usedModel
              }
              m.isStreaming = false
            }
          })

          // After propose_plan completes in plan mode, auto-switch to agent so the
          // user can confirm implementation with a single reply.
          if (modeForThisRequest === 'plan' && proposePlanCompleted) {
            chatMode.value = 'agent'
          }

          // If the AI called request_plan (complexity escalation), switch to plan mode
          // and transparently re-send the original message so planning starts immediately.
          if (requestPlanTriggered) {
            chatMode.value = 'plan'
            setTimeout(() => {
              void sendMessage(userMessage, systemPrompt, context, true)
            }, 50)
          }

          // Safety net: if this was a plan-mode re-send triggered by request_plan but
          // the AI never called propose_plan (e.g. it was a question, not a code request),
          // fall back to agent mode so the user isn't silently stuck.
          if (modeForThisRequest === 'plan' && !proposePlanCompleted && !requestPlanTriggered) {
            chatMode.value = 'agent'
          }
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
        // Accumulate token usage from completed messages
        if (message.inputTokens || message.outputTokens) {
          if (activeSession.value) {
            activeSession.value.totalInputTokens += message.inputTokens || 0
            activeSession.value.totalOutputTokens += message.outputTokens || 0
            activeSession.value.totalTokens = activeSession.value.totalInputTokens + activeSession.value.totalOutputTokens
            activeSession.value.totalPromptsSubmitted += 1
          }
        }

        // Only add assistant messages (user message already added)
        if (message.role === 'assistant') {
          const existingMessage = messages.value.find(
            (m) => m.role === 'assistant' && m.isStreaming,
          )

          if (existingMessage) {
            // Check if this is a completely different message (not just streaming continuation)
            // Detect if the new content doesn't START with the existing content
            const isNewMessage = existingMessage.content.length > 50 &&
                                !message.content.startsWith(existingMessage.content.substring(0, 30))

            if (isNewMessage) {
              console.log('[useAiChat] Detected new message (not streaming continuation), preserving existing message')

              // Mark existing message as complete and stamp the model
              existingMessage.isStreaming = false
              existingMessage.model = selectedModel.value || undefined

              // Add new message for the summary (will appear after tool messages)
              messages.value.push({
                id: crypto.randomUUID(),
                role: 'assistant',
                content: message.content,
                timestamp: new Date(),
                isStreaming: true,
              })
            } else {
              // Streaming continuation - update existing message
              existingMessage.content = message.content
            }
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
        // Detect a successful propose_plan so we can auto-switch to agent
        const raw = toolResult as any
        const isSuccess = raw.isSuccess !== undefined ? raw.isSuccess : !toolResult.isError
        const matchingCall = messages.value.find((m) => m.id === toolResult.toolCallId)
        if (isSuccess && matchingCall?.toolName === 'propose_plan') {
          proposePlanCompleted = true
        }
        if (isSuccess && matchingCall?.toolName === 'request_plan') {
          requestPlanTriggered = true
        }
      }
    }
  }

  /**
   * Handle tool call execution
   */
  function handleToolCall(toolCall: ToolCall) {
    console.log('[useAiChat] handleToolCall received:', toolCall)

    // Check if we've already added this tool call (backend may send duplicates)
    const existingMessage = messages.value.find(
      (m) => m.id === toolCall.id && m.type === 'toolExecution'
    )
    if (existingMessage) {
      console.log('[useAiChat] Tool call already exists, skipping:', toolCall.id)
      return
    }

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

    // Note: We don't call onToolExecuted here - only when the result is received
    // This prevents duplicate calls and ensures we have the result data
  }

  /**
   * Handle tool result
   */
  function handleToolResult(toolResult: ToolResult) {
    const toolMessage = messages.value.find(
      (m) => m.id === toolResult.toolCallId && m.type === 'toolExecution',
    )

    if (toolMessage) {
      // Backend sends { toolCallId, result, isSuccess, errorMessage }
      // Frontend ToolResult type has { toolCallId, content, isError } — bridge both shapes
      const raw      = toolResult as any
      const content  = raw.result    ?? toolResult.content  ?? ''
      const isError  = raw.isSuccess !== undefined ? !raw.isSuccess : (toolResult.isError ?? false)

      toolMessage.toolStatus = isError ? 'error' : 'completed'
      toolMessage.toolResult = content
      if (isError) toolMessage.error = raw.errorMessage ?? content

      // Parse result JSON once
      let resultData: any = content
      try { resultData = JSON.parse(content) } catch { /* not JSON */ }

      // Upgrade message type for special tools
      if (!isError) {
        if (toolMessage.toolName === 'write_instruction' && resultData?.notification) {
          toolMessage.type = 'instructionUpdate'
          toolMessage.instructionNotification = resultData.notification as string
        }
        if (toolMessage.toolName === 'request_plan') {
          toolMessage.content = `Switching to plan mode: ${resultData?.reason ?? 'complex task detected'}`
        }
      }

      // Notify tool execution callback with result
      if (options?.onToolExecuted && toolMessage.toolName) {
        try {
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
    chatMode,

    // Session state
    sessions,
    activeSession,
    isLoadingSessions,
    isLoadingMoreSessions,
    isLoadingMessages,
    hasMoreSessions,
    sessionsTotalCount,

    // Methods
    loadProviders,
    sendMessage,
    clearMessages,
    cancelGeneration,

    // Session methods
    loadSessions,
    loadMoreSessions,
    getOrCreateActiveSession,
    createSession,
    activateSession,
    deleteSession,
    updateSessionTitle,
    clearSessionMessages,
    loadSessionMessages,
  }
}
