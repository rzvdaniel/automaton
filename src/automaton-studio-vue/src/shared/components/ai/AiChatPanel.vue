<script setup lang="ts">
/**
 * AiChatPanel.vue
 * Enhanced AI Chat component with streaming, tool execution, and code generation
 * Includes provider/model selection based on user's configured API keys
 */
import { ref, computed, nextTick, watch, onMounted, onUnmounted } from 'vue'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import AiMessage from './messages/AiMessage.vue'
import type { AiChatMessage, AiChatConfig, StreamChunk, CodeBlock } from '@/shared/types/ai-chat.types'
import { aiStreamingService } from '@/shared/services/ai/ai-streaming.service'
import {
  aiProvidersService,
  type AiProviderInfo,
  getModelDisplayName,
} from '@/shared/services/ai/ai-providers.service'

interface Props {
  config: AiChatConfig
  onApplyCode?: (code: string, fileName?: string) => void
}

const props = defineProps<Props>()

// Provider/Model State
const providers = ref<AiProviderInfo[]>([])
const selectedProvider = ref<AiProviderInfo | null>(null)
const selectedModel = ref<string | null>(null)
const isLoadingProviders = ref(false)
const providerError = ref<string | null>(null)

// Chat State
const messages = ref<AiChatMessage[]>([])
const userInput = ref('')
const isLoading = ref(false)
const messagesContainer = ref<HTMLElement | null>(null)
const currentAbortController = ref<AbortController | null>(null)
const conversationId = ref(crypto.randomUUID())

// Current streaming message (for real-time updates)
const streamingMessage = ref<AiChatMessage | null>(null)

// Computed
const suggestions = computed(() => props.config.suggestions || [])

const availableProviders = computed(() =>
  providers.value.filter((p) => p.hasStoredKeys || p.hasEnvironmentKey),
)

const availableModels = computed(() => {
  if (!selectedProvider.value) return []
  return selectedProvider.value.supportedModels.map((modelId) => ({
    id: modelId,
    name: getModelDisplayName(modelId),
  }))
})

const hasConfiguredProvider = computed(() => availableProviders.value.length > 0)

const isReadyToChat = computed(
  () => hasConfiguredProvider.value && selectedProvider.value && selectedModel.value,
)

// Watch for new messages and scroll to bottom
watch(
  () => messages.value.length,
  async () => {
    await nextTick()
    scrollToBottom()
  },
)

watch(
  () => streamingMessage.value?.content,
  async () => {
    await nextTick()
    scrollToBottom()
  },
)

// Watch provider selection to auto-select first model
watch(selectedProvider, (provider) => {
  if (provider && provider.supportedModels.length > 0) {
    selectedModel.value = provider.supportedModels[0]
  } else {
    selectedModel.value = null
  }
})

// Load providers on mount
onMounted(async () => {
  await loadProviders()
})

async function loadProviders() {
  isLoadingProviders.value = true
  providerError.value = null

  try {
    providers.value = await aiProvidersService.getAvailableProviders(true)

    // Auto-select first available provider
    if (availableProviders.value.length > 0) {
      selectedProvider.value = availableProviders.value[0]
    }
  } catch (error) {
    console.error('Failed to load AI providers:', error)
    providerError.value = 'Failed to load AI providers. Using simulation mode.'
  } finally {
    isLoadingProviders.value = false
  }
}

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
      .slice(-10)
      .map((m) => ({
        role: m.role,
        content: m.content,
      }))

    const requestParams = {
      conversationId: conversationId.value,
      resourceType: props.config.resourceType || 'app',
      resourceId: props.config.resourceId,
      language: props.config.language,
      message: messageText,
      history: history.slice(0, -1),
      context: props.config.context,
      providerName: selectedProvider.value?.providerName,
      model: selectedModel.value || undefined,
      apiKeyId: selectedProvider.value
        ? aiProvidersService.getFirstApiKeyId(selectedProvider.value)
        : undefined,
    }

    const callbacks = {
      onChunk: handleStreamChunk,
      onComplete: handleStreamComplete,
      onError: handleStreamError,
    }

    // Use real streaming if we have a configured provider, otherwise simulate
    if (isReadyToChat.value) {
      currentAbortController.value = await aiStreamingService.streamConversation(
        requestParams,
        callbacks,
      )
    } else {
      // Fallback to simulation
      currentAbortController.value = await aiStreamingService.simulateStreaming(
        requestParams,
        callbacks,
      )
    }
  } catch (error) {
    handleStreamError(error as Error)
  }
}

function handleStreamChunk(chunk: StreamChunk) {
  if (!streamingMessage.value) return

  switch (chunk.type) {
    case 'text':
      streamingMessage.value.type = 'text'
      streamingMessage.value.content = chunk.content || ''
      break

    case 'reasoning':
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
      break
  }
}

function handleStreamComplete() {
  if (streamingMessage.value) {
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

onUnmounted(() => {
  cancelRequest()
})

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
    <!-- Header with Provider/Model Selection -->
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

    <!-- Provider/Model Selector -->
    <div class="model-selector">
      <div class="selector-row">
        <Select
          v-model="selectedProvider"
          :options="availableProviders"
          optionLabel="displayName"
          placeholder="Select Provider"
          :loading="isLoadingProviders"
          :disabled="isLoading"
          class="provider-select"
          size="small"
        >
          <template #value="slotProps">
            <div v-if="slotProps.value" class="provider-option">
              <span>{{ slotProps.value.displayName }}</span>
            </div>
            <span v-else>{{ slotProps.placeholder }}</span>
          </template>
          <template #option="slotProps">
            <div class="provider-option">
              <span>{{ slotProps.option.displayName }}</span>
              <span v-if="slotProps.option.hasEnvironmentKey" class="env-badge">ENV</span>
            </div>
          </template>
        </Select>

        <Select
          v-model="selectedModel"
          :options="availableModels"
          optionLabel="name"
          optionValue="id"
          placeholder="Select Model"
          :disabled="!selectedProvider || isLoading"
          class="model-select"
          size="small"
        />
      </div>

      <!-- No provider warning -->
      <div v-if="!isLoadingProviders && !hasConfiguredProvider" class="no-provider-warning">
        <i class="pi pi-info-circle"></i>
        <span>No AI providers configured. <a href="/settings/ai">Add API keys</a> or using demo mode.</span>
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
        <span v-if="isReadyToChat">
          Using {{ selectedProvider?.displayName }} / {{ getModelDisplayName(selectedModel || '') }}
        </span>
        <span v-else>Demo mode - responses are simulated</span>
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

.model-selector {
  padding: 8px 16px;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.selector-row {
  display: flex;
  gap: 8px;
}

.provider-select {
  flex: 1;
}

.model-select {
  flex: 1;
}

.provider-option {
  display: flex;
  align-items: center;
  gap: 8px;
}

.env-badge {
  font-size: 0.65rem;
  background: var(--p-green-100);
  color: var(--p-green-700);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 500;
}

.no-provider-warning {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
  padding: 8px;
  background: var(--p-amber-50);
  border-radius: 6px;
  font-size: 0.75rem;
  color: var(--p-amber-700);
}

.no-provider-warning a {
  color: var(--p-primary-500);
  text-decoration: underline;
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

:global(.app-dark) .model-selector {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .input-area {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .empty-icon {
  color: var(--p-surface-600);
}

:global(.app-dark) .env-badge {
  background: var(--p-green-900);
  color: var(--p-green-300);
}

:global(.app-dark) .no-provider-warning {
  background: rgba(245, 158, 11, 0.15);
  color: var(--p-amber-400);
}
</style>
