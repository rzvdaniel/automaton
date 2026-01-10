<script setup lang="ts">
/**
 * AiAssistant.vue
 * AI Assistant sidebar component for the App Editor
 * Includes chat interface and collapsible Backend Steps section
 *
 * NOTE: AI chat is a placeholder implementation.
 * Full AI integration requires:
 * - WebSocket/SignalR connection to MCP server
 * - Message handling for AI responses
 * - Tool execution feedback
 * - Code update synchronization with editor
 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Panel from 'primevue/panel'
import Tag from 'primevue/tag'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { LinkedStepInfo } from '../types/apps.types'

const emit = defineEmits<{
  createStep: []
}>()

const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

interface Message {
  id: string
  role: 'user' | 'assistant'
  content: string
  timestamp: Date
}

// Messages
const messages = ref<Message[]>([])
const userInput = ref('')
const isLoading = ref(false)

// Quick suggestions
const suggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

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
  } catch {
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

function handleSuggestion(suggestion: string) {
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

// Backend Steps functions
function getLanguageIcon(language: string): string {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'pi pi-code'
    case 'powershell':
      return 'pi pi-terminal'
    case 'batch':
      return 'pi pi-desktop'
    default:
      return 'pi pi-file'
  }
}

function getLanguageColor(language: string): 'info' | 'success' | 'warn' | 'danger' | undefined {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'info'
    case 'powershell':
      return 'warn'
    case 'batch':
      return 'success'
    default:
      return undefined
  }
}

function handleEditStep(stepId: string) {
  router.push(`/steps/${stepId}/edit`)
}

function handleUnlinkStep(step: LinkedStepInfo) {
  confirm.require({
    message: `Unlink step "${step.displayName || step.name}" from this app?`,
    header: 'Confirm Unlink',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unlink',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unlinkStep(step.id)
        toast.add({
          severity: 'success',
          summary: 'Unlinked',
          detail: 'Step unlinked from app',
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to unlink step',
          life: 5000,
        })
      }
    },
  })
}

function generateUsageCode(step: LinkedStepInfo): string {
  return `await automatonApi.executeStep('${step.name}', { inputs: {} });`
}

async function copyCode(step: LinkedStepInfo) {
  try {
    await navigator.clipboard.writeText(generateUsageCode(step))
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Code copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to copy code',
      life: 3000,
    })
  }
}

const stepsCount = computed(() => editorStore.linkedSteps.length)
</script>

<template>
  <div class="ai-assistant">
    <!-- Header -->
    <div class="ai-header">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
        <span class="font-semibold">AI Assistant</span>
      </div>
    </div>

    <!-- Messages area -->
    <div class="messages-area">
      <!-- Empty state -->
      <div v-if="messages.length === 0" class="empty-state">
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
          placeholder="Describe what you want..."
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
          v-tooltip.top="'Clear'"
        />
      </div>
    </div>

    <!-- Backend Steps Panel -->
    <Panel
      :collapsed="!editorStore.isStepsPanelExpanded"
      toggleable
      class="steps-panel"
      @update:collapsed="editorStore.toggleStepsPanel()"
    >
      <template #header>
        <div class="flex align-items-center gap-2">
          <i class="pi pi-puzzle-piece"></i>
          <span>Backend Steps</span>
          <Tag v-if="stepsCount > 0" :value="String(stepsCount)" severity="secondary" rounded />
        </div>
      </template>

      <template #icons>
        <Button
          icon="pi pi-plus"
          text
          rounded
          size="small"
          severity="secondary"
          @click.stop="emit('createStep')"
          v-tooltip.left="'New Step'"
        />
      </template>

      <div class="steps-content">
        <!-- Empty state -->
        <div v-if="editorStore.linkedSteps.length === 0" class="steps-empty">
          <p class="text-color-secondary text-sm mb-2">No backend steps</p>
          <Button
            label="Create Step"
            icon="pi pi-plus"
            size="small"
            outlined
            @click="emit('createStep')"
          />
        </div>

        <!-- Steps list -->
        <div v-else class="steps-list">
          <div
            v-for="step in editorStore.linkedSteps"
            :key="step.id"
            class="step-item"
          >
            <div class="step-info">
              <div class="flex align-items-center gap-2 mb-1">
                <i :class="getLanguageIcon(step.language)" class="text-color-secondary"></i>
                <span class="step-name">{{ step.displayName || step.name }}</span>
              </div>
              <Tag :value="step.language" :severity="getLanguageColor(step.language)" size="small" />
            </div>
            <div class="step-actions">
              <Button
                icon="pi pi-pencil"
                text
                rounded
                size="small"
                severity="secondary"
                @click="handleEditStep(step.id)"
                v-tooltip.top="'Edit'"
              />
              <Button
                icon="pi pi-copy"
                text
                rounded
                size="small"
                severity="secondary"
                @click="copyCode(step)"
                v-tooltip.top="'Copy code'"
              />
              <Button
                icon="pi pi-times"
                text
                rounded
                size="small"
                severity="danger"
                @click="handleUnlinkStep(step)"
                v-tooltip.top="'Unlink'"
              />
            </div>
          </div>
        </div>
      </div>
    </Panel>
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

.messages-area {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
  min-height: 200px;
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

/* Steps Panel */
.steps-panel {
  flex-shrink: 0;
  border-top: 1px solid var(--p-surface-200);
}

.steps-panel :deep(.p-panel-header) {
  padding: 0.5rem 0.75rem;
  background-color: var(--p-surface-50);
}

.steps-panel :deep(.p-panel-content) {
  padding: 0;
}

.steps-content {
  max-height: 200px;
  overflow-y: auto;
}

.steps-empty {
  padding: 1rem;
  text-align: center;
}

.steps-list {
  display: flex;
  flex-direction: column;
}

.step-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-100);
}

.step-item:last-child {
  border-bottom: none;
}

.step-item:hover {
  background-color: var(--p-surface-50);
}

.step-info {
  flex: 1;
  min-width: 0;
}

.step-name {
  font-size: 0.8rem;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.step-actions {
  display: flex;
  gap: 0.25rem;
  flex-shrink: 0;
}

/* Dark mode */
:global(.app-dark) .ai-assistant {
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

:global(.app-dark) .steps-panel {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .steps-panel :deep(.p-panel-header) {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .step-item {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .step-item:hover {
  background-color: var(--p-surface-700);
}
</style>
