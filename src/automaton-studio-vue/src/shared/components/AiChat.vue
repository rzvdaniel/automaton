<script setup lang="ts">
/**
 * AiChat.vue
 * Generic, reusable AI Chat component
 * Can be used in apps, flows, steps, and other features
 * Handlers are injected to customize behavior per feature
 */
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import type { AiChatMessage, AiChatConfig, AiChatHandlers } from '@/shared/types/ai-chat.types'

const props = defineProps<{
  /** Configuration for the chat component */
  config: AiChatConfig
  
  /** Handlers for message sending and events */
  handlers: AiChatHandlers
}>()

// Messages
const messages = ref<AiChatMessage[]>([])
const userInput = ref('')
const isLoading = ref(false)

// Computed suggestions or defaults
const suggestions = computed(() => props.config.suggestions || [])

// Send message
async function sendMessage() {
  if (!userInput.value.trim() || isLoading.value) return

  const messageContent = userInput.value.trim()
  userInput.value = ''

  messages.value.push({
    id: crypto.randomUUID(),
    role: 'user',
    content: messageContent,
    timestamp: new Date(),
  })

  isLoading.value = true

  try {
    // Call injected handler with context
    const response = await props.handlers.onSendMessage(messageContent, props.config.context)
    
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: response,
      timestamp: new Date(),
    })
  } catch (error) {
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: `Error: ${error instanceof Error ? error.message : 'Failed to process request. Please try again.'}`,
      timestamp: new Date(),
    })
  } finally {
    isLoading.value = false
  }
}

function handleSuggestion(suggestion: string) {
  // Call optional handler if provided
  if (props.handlers.onSuggestion) {
    props.handlers.onSuggestion(suggestion)
  }
  userInput.value = suggestion
}

function handleKeyPress(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    sendMessage()
  }
}

function clearMessages() {
  messages.value = []
}
</script>

<template>
  <div class="ai-chat">
    <!-- Header -->
    <div class="ai-header">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
        <span class="font-semibold">{{ config.title }}</span>
      </div>
    </div>

    <!-- Messages area -->
    <div class="messages-area">
      <!-- Empty state -->
      <div v-if="messages.length === 0" class="empty-state">
        <i class="pi pi-sparkles text-3xl text-color-secondary mb-2"></i>
        <p class="text-color-secondary text-sm mb-3">
          {{ config.emptyStateMessage }}
        </p>

        <!-- Quick suggestions -->
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
      <div v-else class="messages">
        <div
          v-for="message in messages"
          :key="message.id"
          class="message"
          :class="message.role"
        >
          <div class="message-header">
            <span class="message-role">{{ message.role === 'user' ? 'You' : 'AI' }}</span>
            <span class="message-time">
              {{ message.timestamp.toLocaleTimeString() }}
            </span>
          </div>
          <div class="message-content">{{ message.content }}</div>
        </div>

        <!-- Loading indicator -->
        <div v-if="isLoading" class="message assistant loading">
          <div class="message-header">
            <span class="message-role">AI</span>
          </div>
          <div class="message-content">
            <i class="pi pi-spin pi-spinner mr-2"></i>
            Thinking...
          </div>
        </div>
      </div>
    </div>

    <!-- Input area -->
    <div class="input-area">
      <div class="flex gap-2">
        <InputText
          v-model="userInput"
          :placeholder="config.inputPlaceholder"
          class="flex-1"
          :disabled="isLoading"
          @keypress="handleKeyPress"
        />
        <Button
          icon="pi pi-send"
          :disabled="!userInput.trim() || isLoading"
          :loading="isLoading"
          @click="sendMessage"
        />
        <Button
          v-if="messages.length > 0"
          v-tooltip.top="'Clear'"
          icon="pi pi-trash"
          severity="secondary"
          text
          @click="clearMessages"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.ai-chat {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--p-surface-0);
}

.ai-header {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.messages-area {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
  padding: 1rem;
}

.suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  justify-content: center;
}

.suggestion-btn {
  font-size: 0.75rem;
}

.messages {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.message {
  padding: 0.5rem 0.75rem;
  border-radius: var(--p-border-radius);
  max-width: 90%;
}

.message.user {
  background-color: var(--p-primary-100);
  align-self: flex-end;
  margin-left: 10%;
}

.message.assistant {
  background-color: var(--p-surface-100);
  align-self: flex-start;
  margin-right: 10%;
}

.message.loading {
  opacity: 0.7;
}

.message-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.25rem;
  font-size: 0.7rem;
}

.message-role {
  font-weight: 600;
}

.message-time {
  color: var(--p-text-secondary);
}

.message-content {
  font-size: 0.8rem;
  line-height: 1.4;
  white-space: pre-wrap;
}

.input-area {
  padding: 0.75rem;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

/* Dark mode */
:global(.app-dark) .ai-chat {
  background-color: var(--p-surface-800);
}

:global(.app-dark) .ai-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .message.user {
  background-color: var(--p-primary-900);
}

:global(.app-dark) .message.assistant {
  background-color: var(--p-surface-700);
}

:global(.app-dark) .input-area {
  border-color: var(--p-surface-700);
}
</style>
