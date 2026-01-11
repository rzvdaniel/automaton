<script setup lang="ts">
/**
 * AiAssistant.vue
 * AI Assistant sidebar component for the App Editor
 * Provides chat interface for AI-assisted app development with real backend integration
 */
import { ref, onMounted, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import { useAiChat } from '@/shared/composables/useAiChat'

// Props
const props = defineProps<{
  appId: string
  runnerId?: string | null
}>()

// Emits
const emit = defineEmits<{
  (e: 'codeGenerated', code: string): void
}>()

// AI Chat composable
const {
  messages,
  isGenerating,
  selectedProvider,
  selectedModel,
  availableProviders,
  availableModels,
  error,
  hasMessages,
  loadProviders,
  sendMessage: sendAiMessage,
  clearMessages,
} = useAiChat('WebApp', props.appId)

// UI state
const userInput = ref('')

// Quick suggestions
const suggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

// Load providers on mount
onMounted(async () => {
  await loadProviders()
})

// Watch for code generation in messages
watch(
  messages,
  (newMessages) => {
    const lastMessage = newMessages[newMessages.length - 1]
    if (lastMessage?.generatedCode) {
      emit('codeGenerated', lastMessage.generatedCode)
    }
  },
  { deep: true }
)

// Send message
async function sendMessage() {
  if (!userInput.value.trim() || isGenerating.value) return

  const messageContent = userInput.value.trim()
  userInput.value = ''

  // Build context with app and runner info
  const context: Record<string, any> = {
    appId: props.appId,
  }
  
  if (props.runnerId) {
    context.runnerId = props.runnerId
  }

  // Build system prompt (this could be more sophisticated)
  const systemPrompt = `You are an AI assistant helping to build a web application. 
You can create and modify HTML, CSS, and JavaScript code.
${props.runnerId ? 'The app has a runner attached for backend step execution.' : 'No runner is attached. Backend steps cannot be executed.'}`

  await sendAiMessage(messageContent, systemPrompt, context)
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
</script>

<template>
  <div class="ai-assistant">
    <!-- Header with Model Selector -->
    <div class="ai-header">
      <div class="flex align-items-center justify-content-between w-full gap-2">
        <div class="flex align-items-center gap-2">
          <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
          <span class="font-semibold">AI Assistant</span>
        </div>
        
        <!-- Model Selector -->
        <div class="flex align-items-center gap-2" v-if="availableProviders.length > 0">
          <Select
            v-model="selectedProvider"
            :options="availableProviders"
            optionLabel="displayName"
            optionValue="providerName"
            placeholder="Provider"
            size="small"
            class="model-select"
            :disabled="isGenerating"
          >
            <template #value="slotProps">
              <span v-if="slotProps.value">{{ availableProviders.find(p => p.providerName === slotProps.value)?.displayName }}</span>
              <span v-else>{{ slotProps.placeholder }}</span>
            </template>
          </Select>
          
          <Select
            v-model="selectedModel"
            :options="availableModels"
            placeholder="Model"
            size="small"
            class="model-select"
            :disabled="isGenerating || availableModels.length === 0"
          />
        </div>
      </div>
    </div>

    <!-- Error message -->
    <div v-if="error" class="error-banner">
      <i class="pi pi-exclamation-triangle mr-2"></i>
      {{ error }}
    </div>

    <!-- Messages area -->
    <div class="messages-area">
      <!-- Empty state -->
      <div v-if="!hasMessages" class="empty-state">
        <i class="pi pi-sparkles text-3xl text-color-secondary mb-2"></i>
        <p class="text-color-secondary text-sm mb-3">
          Ask me to create or modify your app
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
          :class="[message.role, message.type]"
        >
          <div class="message-header">
            <span class="message-role">
              {{ message.role === 'user' ? 'You' : 'AI' }}
              <span v-if="message.toolName" class="tool-badge">
                <i class="pi pi-cog"></i> {{ message.toolName }}
              </span>
            </span>
            <span class="message-time">
              {{ message.timestamp.toLocaleTimeString() }}
            </span>
          </div>
          
          <div class="message-content">
            {{ message.content }}
            
            <!-- Tool execution status -->
            <div v-if="message.toolStatus" class="tool-status" :class="message.toolStatus">
              <i v-if="message.toolStatus === 'executing'" class="pi pi-spin pi-spinner"></i>
              <i v-else-if="message.toolStatus === 'completed'" class="pi pi-check"></i>
              <i v-else-if="message.toolStatus === 'error'" class="pi pi-times"></i>
              {{ message.toolStatus }}
            </div>
          </div>
        </div>

        <!-- Generating indicator -->
        <div v-if="isGenerating" class="message assistant generating">
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
          placeholder="Describe what you want..."
          class="flex-1"
          :disabled="isGenerating"
          @keypress="handleKeyPress"
        />
        <Button
          icon="pi pi-send"
          :disabled="!userInput.trim() || isGenerating || !selectedProvider"
          :loading="isGenerating"
          @click="sendMessage"
          v-tooltip.top="!selectedProvider ? 'Please select an AI provider' : 'Send message'"
        />
        <Button
          v-if="hasMessages"
          icon="pi pi-trash"
          severity="secondary"
          text
          @click="clearMessages"
          v-tooltip.top="'Clear conversation'"
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

.ai-header {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.model-select {
  min-width: 120px;
  font-size: 0.875rem;
}

.error-banner {
  background-color: var(--p-red-100);
  color: var(--p-red-900);
  padding: 0.5rem 1rem;
  font-size: 0.875rem;
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--p-red-200);
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

.message.generating {
  opacity: 0.7;
}

.message.toolExecution {
  background-color: var(--p-blue-50);
  border-left: 3px solid var(--p-blue-500);
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
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.tool-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.125rem 0.375rem;
  background-color: var(--p-blue-100);
  color: var(--p-blue-900);
  border-radius: 0.25rem;
  font-size: 0.65rem;
}

.message-time {
  color: var(--p-text-secondary);
}

.message-content {
  font-size: 0.8rem;
  line-height: 1.4;
  white-space: pre-wrap;
}

.tool-status {
  margin-top: 0.5rem;
  padding: 0.25rem 0.5rem;
  border-radius: 0.25rem;
  font-size: 0.75rem;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
}

.tool-status.executing {
  background-color: var(--p-blue-100);
  color: var(--p-blue-900);
}

.tool-status.completed {
  background-color: var(--p-green-100);
  color: var(--p-green-900);
}

.tool-status.error {
  background-color: var(--p-red-100);
  color: var(--p-red-900);
}

.input-area {
  padding: 0.75rem;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

/* Dark mode */
:global(.app-dark) .ai-assistant {
  background-color: var(--p-surface-800);
}

:global(.app-dark) .ai-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .error-banner {
  background-color: var(--p-red-900);
  color: var(--p-red-100);
  border-color: var(--p-red-800);
}

:global(.app-dark) .message.user {
  background-color: var(--p-primary-900);
}

:global(.app-dark) .message.assistant {
  background-color: var(--p-surface-700);
}

:global(.app-dark) .message.toolExecution {
  background-color: var(--p-blue-900);
  border-left-color: var(--p-blue-400);
}

:global(.app-dark) .input-area {
  border-color: var(--p-surface-700);
}
</style>
