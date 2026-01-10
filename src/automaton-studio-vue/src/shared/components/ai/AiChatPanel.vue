<script setup lang="ts">
/**
 * AiChatPanel.vue
 * Enhanced AI Chat component with streaming, tool execution, and code generation
 * Replaces the simpler AiChat component with full functionality
 */
import { ref, computed, nextTick, watch, onMounted, onUnmounted } from 'vue'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import AiMessage from './messages/AiMessage.vue'
import type {
  AiChatMessage,
  AiChatConfig,
  AiMessageType,
  StreamChunk,
  CodeBlock,
} from '@/shared/types/ai-chat.types'
import { aiStreamingService } from '@/shared/services/ai/ai-streaming.service'

interface Props {
  config: AiChatConfig
  onApplyCode?: (code: string, fileName?: string) => void
}

const props = defineProps<Props>()

// State
const messages = ref<AiChatMessage[]>([])
const userInput = ref('')
const isLoading = ref(false)
const messagesContainer = ref<HTMLElement | null>(null)
const currentAbortController = ref<AbortController | null>(null)
const conversationId = ref(crypto.randomUUID())

// Current streaming message (for real-time updates)
const streamingMessage = ref<AiChatMessage | null>(null)

// Suggestions from config
const suggestions = computed(() => props.config.suggestions || [])

// Watch for new messages and scroll to bottom
watch(
  () => messages.value.length,
  async () => {
    await nextTick()
    scrollToBottom()
  },
)

// Also scroll when streaming message updates
watch(
  () => streamingMessage.value?.content,
  async () => {
    await nextTick()
    scrollToBottom()
  },
)

function scrollToBottom() {
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

// Send message with streaming
async function sendMessage() {
  const messageText = userInput.value.trim()
  if (!messageText || isLoading.value) return

  userInput.value = ''
  isLoading.value = true

  // Add user message
  const userMessage: AiChatMessage = {
    id: crypto.randomUUID(),
    role: 'user',
    type: 'text',
    content: messageText,
    timestamp: new Date(),
  }
  messages.value.push(userMessage)

  // Create initial streaming message
  streamingMessage.value = {
    id: crypto.randomUUID(),
    role: 'assistant',
    type: 'generating',
    content: 'Thinking...',
    timestamp: new Date(),
    isStreaming: true,
  }

  try {
    // Build conversation history
    const history = messages.value
      .filter((m) => m.role !== 'system')
      .slice(-10) // Keep last 10 messages for context
      .map((m) => ({
        role: m.role,
        content: m.content,
      }))

    // Use simulation for now (backend may not be available)
    // In production, this would call streamConversation
    currentAbortController.value = await aiStreamingService.simulateStreaming(
      {
        conversationId: conversationId.value,
        resourceType: props.config.resourceType || 'app',
        resourceId: props.config.resourceId,
        language: props.config.language,
        message: messageText,
        history: history.slice(0, -1), // Exclude the message we just added
        context: props.config.context,
      },
      {
        onChunk: handleStreamChunk,
        onComplete: handleStreamComplete,
        onError: handleStreamError,
      },
    )
  } catch (error) {
    handleStreamError(error as Error)
  }
}

function handleStreamChunk(chunk: StreamChunk) {
  if (!streamingMessage.value) return

  switch (chunk.type) {
    case 'text':
      // Update or create text message
      streamingMessage.value.type = 'text'
      streamingMessage.value.content = chunk.content || ''
      break

    case 'reasoning':
      // Add as separate reasoning message
      const reasoningMsg: AiChatMessage = {
        id: crypto.randomUUID(),
        role: 'assistant',
        type: 'reasoning',
        content: chunk.content || '',
        timestamp: new Date(),
        isCollapsed: true,
      }
      messages.value.push(reasoningMsg)
      break

    case 'tool_call':
      if (chunk.toolCall) {
        // Add tool execution message
        const toolCallMsg: AiChatMessage = {
          id: crypto.randomUUID(),
          role: 'assistant',
          type: 'tool_execution',
          content: chunk.toolCall.description || `Executing ${chunk.toolCall.name}...`,
          toolName: chunk.toolCall.name,
          toolStatus: 'executing',
          timestamp: new Date(),
        }
        messages.value.push(toolCallMsg)
      }
      break

    case 'tool_result':
      if (chunk.toolResult) {
        // Find and update the tool call message
        const toolMsg = messages.value.find(
          (m) =>
            m.type === 'tool_execution' &&
            m.toolName === chunk.toolResult?.toolName &&
            m.toolStatus === 'executing',
        )
        if (toolMsg) {
          toolMsg.toolStatus = chunk.toolResult.isSuccess ? 'completed' : 'error'
          toolMsg.toolResult = {
            toolName: chunk.toolResult.toolName,
            toolId: chunk.toolResult.toolId,
            isSuccess: chunk.toolResult.isSuccess,
            data: chunk.toolResult.data,
            error: chunk.toolResult.error,
          }
        }
      }
      break

    case 'code_generated':
      // Add code message
      const codeBlock: CodeBlock = {
        id: chunk.codeId || crypto.randomUUID(),
        language: chunk.language || 'text',
        code: chunk.code || '',
        fileName: chunk.fileName,
        canApply: true,
      }

      const codeMsg: AiChatMessage = {
        id: crypto.randomUUID(),
        role: 'assistant',
        type: 'code',
        content: chunk.content || '',
        codeBlocks: [codeBlock],
        timestamp: new Date(),
      }
      messages.value.push(codeMsg)
      break

    case 'error':
      streamingMessage.value.type = 'error'
      streamingMessage.value.content = chunk.content || 'An error occurred'
      break

    case 'complete':
      // Handled in onComplete
      break
  }
}

function handleStreamComplete() {
  if (streamingMessage.value) {
    // Only add the streaming message if it has meaningful content
    if (
      streamingMessage.value.type !== 'generating' &&
      streamingMessage.value.content &&
      streamingMessage.value.content !== 'Thinking...'
    ) {
      streamingMessage.value.isStreaming = false
      messages.value.push({ ...streamingMessage.value })
    }
    streamingMessage.value = null
  }
  isLoading.value = false
  currentAbortController.value = null
}

function handleStreamError(error: Error) {
  console.error('Stream error:', error)

  if (streamingMessage.value) {
    streamingMessage.value.type = 'error'
    streamingMessage.value.content = error.message || 'An unexpected error occurred'
    streamingMessage.value.isStreaming = false
    messages.value.push({ ...streamingMessage.value })
    streamingMessage.value = null
  } else {
    // Add error message
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      type: 'error',
      content: error.message || 'An unexpected error occurred',
      timestamp: new Date(),
    })
  }

  isLoading.value = false
  currentAbortController.value = null
}

function handleSuggestion(suggestion: string) {
  userInput.value = suggestion
}

function handleKeyPress(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    sendMessage()
  }
}

function handleApplyCode(code: string, fileName?: string) {
  if (props.onApplyCode) {
    props.onApplyCode(code, fileName)
  }
}

function clearMessages() {
  messages.value = []
  conversationId.value = crypto.randomUUID()
}

function cancelRequest() {
  if (currentAbortController.value) {
    currentAbortController.value.abort()
    currentAbortController.value = null
    streamingMessage.value = null
    isLoading.value = false
  }
}

// Cleanup on unmount
onUnmounted(() => {
  cancelRequest()
})

// Combined messages for display (including streaming message)
const displayMessages = computed(() => {
  const msgs = [...messages.value]
  if (streamingMessage.value) {
    msgs.push(streamingMessage.value)
  }
  return msgs
})
</script>

<template>
  <div class="ai-chat-panel">
    <!-- Header -->
    <div class="chat-header">
      <div class="header-left">
        <i class="pi pi-sparkles header-icon"></i>
        <span class="header-title">{{ config.title }}</span>
      </div>
      <div class="header-actions">
        <Button
          v-if="messages.length > 0"
          icon="pi pi-trash"
          text
          rounded
          severity="secondary"
          size="small"
          @click="clearMessages"
          v-tooltip.left="'Clear conversation'"
        />
      </div>
    </div>

    <!-- Messages Area -->
    <div ref="messagesContainer" class="messages-area">
      <!-- Empty State -->
      <div v-if="displayMessages.length === 0" class="empty-state">
        <i class="pi pi-sparkles empty-icon"></i>
        <p class="empty-text">{{ config.emptyStateMessage }}</p>

        <!-- Suggestions -->
        <div v-if="suggestions.length > 0" class="suggestions">
          <Button
            v-for="suggestion in suggestions"
            :key="suggestion"
            :label="suggestion"
            size="small"
            text
            class="suggestion-btn"
            @click="handleSuggestion(suggestion)"
          />
        </div>
      </div>

      <!-- Messages -->
      <template v-else>
        <AiMessage
          v-for="message in displayMessages"
          :key="message.id"
          :message="message"
          :on-apply-code="handleApplyCode"
        />
      </template>
    </div>

    <!-- Input Area -->
    <div class="input-area">
      <div class="input-wrapper">
        <Textarea
          v-model="userInput"
          :placeholder="config.inputPlaceholder"
          :disabled="isLoading"
          auto-resize
          rows="1"
          class="message-input"
          @keypress="handleKeyPress"
        />
        <div class="input-actions">
          <Button
            v-if="isLoading"
            icon="pi pi-stop"
            severity="danger"
            text
            rounded
            @click="cancelRequest"
            v-tooltip.top="'Cancel'"
          />
          <Button
            v-else
            icon="pi pi-send"
            :disabled="!userInput.trim()"
            rounded
            @click="sendMessage"
            v-tooltip.top="'Send'"
          />
        </div>
      </div>
      <div class="input-hint">
        <span>Press Enter to send, Shift+Enter for new line</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ai-chat-panel {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--p-surface-0);
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.header-icon {
  color: var(--p-primary-500);
  font-size: 1.1rem;
}

.header-title {
  font-weight: 600;
  font-size: 0.95rem;
}

.messages-area {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
  padding: 20px;
}

.empty-icon {
  font-size: 2.5rem;
  color: var(--p-surface-400);
  margin-bottom: 12px;
}

.empty-text {
  color: var(--p-text-secondary);
  font-size: 0.9rem;
  margin-bottom: 16px;
}

.suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
  max-width: 100%;
}

.suggestion-btn {
  font-size: 0.8rem;
  white-space: nowrap;
}

.input-area {
  padding: 12px 16px;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.input-wrapper {
  display: flex;
  align-items: flex-end;
  gap: 8px;
}

.message-input {
  flex: 1;
  resize: none;
  max-height: 120px;
  font-size: 0.9rem;
}

.input-actions {
  display: flex;
  align-items: center;
}

.input-hint {
  margin-top: 6px;
  font-size: 0.7rem;
  color: var(--p-text-secondary);
}

/* Dark mode */
:global(.app-dark) .ai-chat-panel {
  background: var(--p-surface-800);
}

:global(.app-dark) .chat-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .input-area {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .empty-icon {
  color: var(--p-surface-600);
}
</style>
