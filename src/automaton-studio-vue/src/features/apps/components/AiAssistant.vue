<script setup lang="ts">
/**
 * AiAssistant.vue
 * AI Assistant placeholder component for the App Editor
 * Will connect to MCP server for AI-powered code generation
 *
 * NOTE: This is a placeholder implementation.
 * Full AI integration requires:
 * - WebSocket/SignalR connection to MCP server
 * - Message handling for AI responses
 * - Tool execution feedback
 * - Code update synchronization with editor
 */
import { ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import { useAppEditorStore } from '../stores/app-editor.store'

interface Message {
  id: string
  role: 'user' | 'assistant'
  content: string
  timestamp: Date
}

const editorStore = useAppEditorStore()

// Messages
const messages = ref<Message[]>([])
const userInput = ref('')
const isLoading = ref(false)

// Quick suggestions
const suggestions = [
  'Add a login form with email and password',
  'Create a navigation bar with links',
  'Add a contact form with validation',
  'Create a data table from an API',
]

// Send message
async function sendMessage() {
  if (!userInput.value.trim() || isLoading.value) return

  const messageContent = userInput.value.trim()
  userInput.value = ''

  // Add user message
  messages.value.push({
    id: crypto.randomUUID(),
    role: 'user',
    content: messageContent,
    timestamp: new Date(),
  })

  isLoading.value = true

  try {
    // Placeholder response - will be replaced with actual MCP client
    await new Promise((resolve) => setTimeout(resolve, 1000))

    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content:
        'AI assistant is not yet implemented. This will connect to the MCP server for app generation. Your request: "' +
        messageContent +
        '"',
      timestamp: new Date(),
    })
  } catch (err) {
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: 'Error: Failed to process request. Please try again.',
      timestamp: new Date(),
    })
  } finally {
    isLoading.value = false
  }
}

// Handle suggestion click
function handleSuggestion(suggestion: string) {
  userInput.value = suggestion
}

// Handle key press
function handleKeyPress(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    sendMessage()
  }
}

// Clear messages
function clearMessages() {
  messages.value = []
}
</script>

<template>
  <div class="ai-assistant">
    <!-- Messages area -->
    <div class="messages-area">
      <!-- Empty state -->
      <div v-if="messages.length === 0" class="empty-state">
        <i class="pi pi-sparkles text-4xl text-color-secondary mb-3"></i>
        <h3 class="mb-2">AI Assistant</h3>
        <p class="text-color-secondary text-sm mb-3">
          Ask me to create or modify your app's code
        </p>

        <!-- Quick suggestions -->
        <div class="suggestions">
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
          placeholder="Ask AI to create or modify your app..."
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
          icon="pi pi-trash"
          severity="secondary"
          text
          @click="clearMessages"
          v-tooltip.top="'Clear messages'"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.ai-assistant {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--p-surface-0);
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
}

.suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
  max-width: 400px;
}

.suggestion-btn {
  font-size: 0.75rem;
}

.messages {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.message {
  padding: 0.75rem;
  border-radius: var(--p-border-radius);
  max-width: 85%;
}

.message.user {
  background-color: var(--p-primary-100);
  align-self: flex-end;
  margin-left: 15%;
}

.message.assistant {
  background-color: var(--p-surface-100);
  align-self: flex-start;
  margin-right: 15%;
}

.message.loading {
  opacity: 0.7;
}

.message-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.25rem;
  font-size: 0.75rem;
}

.message-role {
  font-weight: 600;
}

.message-time {
  color: var(--p-text-secondary);
}

.message-content {
  font-size: 0.875rem;
  line-height: 1.5;
  white-space: pre-wrap;
}

.input-area {
  padding: 1rem;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

/* Dark mode */
:global(.app-dark) .ai-assistant {
  background-color: var(--p-surface-800);
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
