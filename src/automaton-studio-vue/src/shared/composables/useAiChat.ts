/**
 * useAiChat Composable
 * Manages AI chat state and streaming conversations
 */
import { ref, computed } from 'vue'
import { aiConversationClient } from '../services/ai/ai-conversation-client'
import { getAvailableProviders } from '../services/ai/ai-providers-service'
import type {
  ChatMessage,
  AiProvider,
  StreamConversationRequest,
  SseConversationUpdate,
  ConversationMessage,
  ToolCall,
  ToolResult,
} from '../types/ai.types'

export function useAiChat(resourceType: string, resourceId: string) {
  // State
  const messages = ref<ChatMessage[]>([])
  const isGenerating = ref(false)
  const selectedProvider = ref<string>('')
  const selectedModel = ref<string>('')
  const availableProviders = ref<AiProvider[]>([])
  const error = ref<string | null>(null)
  const conversationId = ref<string | null>(null)

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
          selectedModel.value = providerWithKey.supportedModels[0]
        }
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load AI providers'
      console.error('Error loading providers:', err)
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

    // Build request
    const request: StreamConversationRequest = {
      conversationId: conversationId.value || undefined,
      provider: selectedProvider.value,
      model: selectedModel.value || undefined,
      message: userMessage,
      systemPrompt,
      history: history.slice(0, -1), // Exclude the message we just added
      context: {
        resourceType,
        resourceId,
        ...context,
      },
    }

    try {
      await aiConversationClient.startConversation(request, {
        onUpdate: handleConversationUpdate,
        onDone: (data) => {
          conversationId.value = data.conversationId
          isGenerating.value = false
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
    const toolName = toolCall.function?.name || 'unknown'
    
    messages.value.push({
      id: toolCall.id,
      role: 'assistant',
      type: 'toolExecution',
      content: `Executing ${toolName}...`,
      toolName,
      toolStatus: 'executing',
      timestamp: new Date(),
    })
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
    }
  }

  /**
   * Clear all messages
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

    // Methods
    loadProviders,
    sendMessage,
    clearMessages,
    cancelGeneration,
  }
}
