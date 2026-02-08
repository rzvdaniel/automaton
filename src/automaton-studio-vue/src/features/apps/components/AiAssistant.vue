<script setup lang="ts">
/**
 * AiAssistant.vue
 * AI Assistant sidebar component for the App Editor
 * Provides chat interface for AI-assisted app development with real backend integration
 */
import { ref, onMounted, watch, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Dialog from 'primevue/dialog'
import { useAiChat } from '@/shared/composables/useAiChat'
import { aiUtilityService } from '@/shared/services/ai-utility.service'
import type { ChatSession } from '@/shared/types/ai.types'

// Props
const props = defineProps<{
  appId: string
  runnerId?: string | null
  initialPrompt?: string
}>()

// Emits
const emit = defineEmits<{
  (e: 'codeGenerated', code: string): void
  (e: 'toolExecuted', toolName: string, args: any, result?: any): void
}>()

// AI Chat composable with tool execution callback
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
  getOrCreateActiveSession,
  clearMessages,
  sessions,
  activeSession,
  isLoadingSessions,
  loadSessions,
  createSession,
  activateSession,
  updateSessionTitle,
  deleteSession,
} = useAiChat('WebApp', props.appId, {
  // Callback when AI tools are executed
  onToolExecuted: (toolName, args, result) => {
    emit('toolExecuted', toolName, args, result)
  },
})

// View state: 'sessions' (list view) or 'chat' (detail view)
const currentView = ref<'sessions' | 'chat'>('sessions')

// Session UI state
const showSessionDialog = ref(false)
const newSessionTitle = ref('')
const showRenameDialog = ref(false)
const renameSessionId = ref<string | null>(null)
const renameSessionTitle = ref('')
const showDeleteConfirmation = ref(false)
const deleteConfirmationSessionId = ref<string | null>(null)
const sessionsSearchQuery = ref('')
const sortedSessions = computed(() => {
  return [...sessions.value].sort((a, b) => {
    // Active session first, then by creation date descending
    if (a.isActive) return -1
    if (b.isActive) return 1
    return new Date(b.createdAt || 0).getTime() - new Date(a.createdAt || 0).getTime()
  })
})

// Filter sessions by search query
const filteredSessions = computed(() => {
  if (!sessionsSearchQuery.value.trim()) return sortedSessions.value
  
  const query = sessionsSearchQuery.value.toLowerCase()
  return sortedSessions.value.filter(s => 
    s.title?.toLowerCase().includes(query)
  )
})

// UI state
const userInput = ref('')

// Quick suggestions
const suggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

// Load providers and session on mount, auto-send initial prompt if provided
onMounted(async () => {
  await loadProviders()
  
  // Load sessions but don't create one automatically
  await loadSessions()
  
  // If sessions exist, switch to chat view with the active session
  if (sessions.value.length > 0 && activeSession.value) {
    currentView.value = 'chat'
    // Load messages for the active session
    await loadSessionMessages(activeSession.value.id)
  }
  // Otherwise stay in sessions view (empty state)

  // Auto-send initial prompt if provided (e.g., from "Generate App" button)
  if (props.initialPrompt) {
    userInput.value = props.initialPrompt
    currentView.value = 'chat'
    // Small delay to ensure UI is ready
    setTimeout(() => {
      sendMessage()
    }, 100)
  }
})

// Handle creating new session
async function handleCreateSession() {
  const title = newSessionTitle.value.trim() || `Chat ${new Date().toLocaleString()}`
  await createSession(title)
  newSessionTitle.value = ''
  showSessionDialog.value = false
}

// Handle switching to a different session
async function handleSelectSession(session: ChatSession) {
  if (session.id !== activeSession.value?.id) {
    await activateSession(session.id)
  }
  currentView.value = 'chat'
}

// Navigate back to sessions list
function backToSessions() {
  currentView.value = 'sessions'
  // Reload sessions list to ensure it's up-to-date
  loadSessions()
}

// Open rename dialog for a session
function openRenameDialog(session: ChatSession) {
  renameSessionId.value = session.id
  renameSessionTitle.value = session.title || ''
  showRenameDialog.value = true
}

// Handle renaming a session
async function handleRenameSession() {
  if (!renameSessionId.value || !renameSessionTitle.value.trim()) return

  await updateSessionTitle(renameSessionId.value, renameSessionTitle.value.trim())
  renameSessionId.value = null
  renameSessionTitle.value = ''
  showRenameDialog.value = false
}

// Open delete confirmation dialog
function openDeleteConfirmation(sessionId: string) {
  deleteConfirmationSessionId.value = sessionId
  showDeleteConfirmation.value = true
}

// Confirm delete session
async function confirmDeleteSession() {
  if (deleteConfirmationSessionId.value) {
    await deleteSession(deleteConfirmationSessionId.value)
    deleteConfirmationSessionId.value = null
    showDeleteConfirmation.value = false
  }
}

// Format session date
function formatSessionDate(date: string | undefined) {
  if (!date) return ''
  const d = new Date(date)
  const today = new Date()
  const yesterday = new Date(today)
  yesterday.setDate(yesterday.getDate() - 1)

  if (d.toDateString() === today.toDateString()) {
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } else if (d.toDateString() === yesterday.toDateString()) {
    return 'Yesterday'
  }
  return d.toLocaleDateString([], { month: 'short', day: 'numeric' })
}

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

  // If we're in the sessions list view, always create a new session
  // If we're in chat view with no active session, also create one
  if (currentView.value === 'sessions' || !activeSession.value) {
    let sessionName: string
    try {
      // Generate session name from the prompt using AI
      sessionName = await aiUtilityService.generateSessionName(messageContent)
    } catch {
      // Fallback: use first few words from the prompt
      const words = messageContent.split(/\s+/).slice(0, 5).join(' ')
      sessionName = words.length > 40 ? words.substring(0, 40) + '...' : words
    }
    await createSession(sessionName)
  }

  // Switch to chat view once session is ready
  currentView.value = 'chat'

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
    <!-- Sessions List View -->
    <template v-if="currentView === 'sessions'">
      <!-- Header -->
      <div class="ai-header">
        <div class="flex align-items-center justify-content-between w-full gap-2">
          <div class="flex align-items-center gap-2">
            <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
            <span class="font-semibold">AI Assistant</span>
          </div>
          
          <Button
            v-tooltip.top="'New session'"
            icon="pi pi-plus"
            size="small"
            text
            @click="showSessionDialog = true"
          />
        </div>
      </div>

      <!-- Sessions List -->
      <div class="sessions-view">
        <div v-if="isLoadingSessions" class="loading-state">
          <i class="pi pi-spin pi-spinner text-2xl text-color-secondary"></i>
          <p class="text-color-secondary text-sm mt-2">Loading sessions...</p>
        </div>

        <div v-else-if="sessions.length === 0" class="empty-sessions">
          <i class="pi pi-comments text-3xl text-color-secondary mb-2"></i>
          <p class="text-color-secondary text-sm mb-3">No chat sessions yet</p>
          <Button
            label="Start New Chat"
            icon="pi pi-plus"
            size="small"
            @click="showSessionDialog = true"
          />
        </div>

        <div v-else class="sessions-list-container">
          <div
            v-for="session in filteredSessions"
            :key="session.id"
            class="session-card"
            :class="{ active: session.id === activeSession?.id }"
            @click="handleSelectSession(session)"
          >
            <div class="session-card-content">
              <div class="session-card-header">
                <span class="session-card-title">{{ session.title }}</span>
                <div class="session-card-actions" @click.stop>
                  <Button
                    v-tooltip.top="'Rename'"
                    icon="pi pi-pencil"
                    size="small"
                    text
                    severity="secondary"
                    @click="openRenameDialog(session)"
                    class="session-action-btn"
                  />
                  <Button
                    v-tooltip.top="'Delete'"
                    icon="pi pi-trash"
                    size="small"
                    text
                    severity="danger"
                    @click="openDeleteConfirmation(session.id)"
                    class="session-action-btn"
                  />
                </div>
              </div>
              <div class="session-card-footer">
                <span class="session-card-date">{{ formatSessionDate(session.createdAt) }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Input area always visible -->
      <div class="input-area">
        <div class="flex gap-2">
          <InputText
            v-model="userInput"
            placeholder="Ask AI a question..."
            class="flex-1"
            :disabled="isGenerating"
            @keypress="handleKeyPress"
          />
          <Button
            v-tooltip.top="!selectedProvider ? 'Please select an AI provider' : 'Send message'"
            icon="pi pi-send"
            :disabled="!userInput.trim() || isGenerating || !selectedProvider"
            :loading="isGenerating"
            @click="sendMessage"
          />
        </div>
      </div>
    </template>

    <!-- Chat View (Session Detail) -->
    <template v-else>
      <!-- Header with Back Button and Model Selector -->
      <div class="ai-header">
        <div class="flex align-items-center justify-content-between w-full gap-2">
          <div class="flex align-items-center gap-2">
            <Button
              v-tooltip.top="'Back to sessions'"
              icon="pi pi-arrow-left"
              size="small"
              text
              @click="backToSessions"
            />
            <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
            <span class="font-semibold">{{ activeSession?.title || 'AI Assistant' }}</span>
            <Button
              v-if="activeSession"
              v-tooltip.top="'Rename session'"
              icon="pi pi-pencil"
              size="small"
              text
              @click="openRenameDialog(activeSession)"
              class="p-button-text p-button-sm"
            />
          </div>
          
          <!-- Model Selector -->
          <div v-if="availableProviders.length > 0" class="flex align-items-center gap-2">
            <Select
              v-model="selectedProvider"
              :options="availableProviders"
              option-label="displayName"
              option-value="providerName"
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

        <!-- Messages: User in bubbles, AI in timeline -->
        <div v-else class="messages-container">
          <!-- User messages as bubbles, AI messages as timeline -->
          <template v-for="(message, index) in messages" :key="message.id">
            <!-- User message - Chat bubble -->
            <div v-if="message.role === 'user'" class="user-message">
              <div class="message-bubble">{{ message.content }}</div>
            </div>

            <!-- AI message - Timeline -->
            <div v-else class="timeline">
              <div
                class="timeline-item"
                :class="[
                  { 'is-last': index === messages.length - 1 && !isGenerating },
                  message.toolName ? 'has-tool' : 'regular'
                ]"
              >
                <!-- Simple bullet point -->
                <div class="timeline-bullet" :class="[message.toolName ? 'tool' : 'regular']">•</div>

                <!-- Connector line (visible if more messages follow) -->
                <div v-if="index < messages.length - 1 || isGenerating" class="timeline-connector"></div>

                <!-- Message content -->
                <div class="timeline-content">
                  <div class="message-text">{{ message.content }}</div>
                  
                  <!-- Tool execution status -->
                  <div v-if="message.toolStatus" class="tool-status" :class="message.toolStatus">
                    <i v-if="message.toolStatus === 'executing'" class="pi pi-spin pi-spinner"></i>
                    <i v-else-if="message.toolStatus === 'completed'" class="pi pi-check"></i>
                    <i v-else-if="message.toolStatus === 'error'" class="pi pi-times"></i>
                    <span>{{ message.toolStatus }}</span>
                  </div>
                </div>
              </div>
            </div>
          </template>

          <!-- Generating indicator -->
          <div v-if="isGenerating" class="timeline">
            <div class="timeline-item">
              <div class="timeline-bullet">•</div>
              <div class="timeline-content">
                <div class="message-text"><i class="pi pi-spin pi-spinner mr-2"></i>Processing your request...</div>
              </div>
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
            v-tooltip.top="!selectedProvider ? 'Please select an AI provider' : 'Send message'"
            icon="pi pi-send"
            :disabled="!userInput.trim() || isGenerating || !selectedProvider"
            :loading="isGenerating"
            @click="sendMessage"
          />
          <Button
            v-if="hasMessages"
            v-tooltip.top="'Clear conversation'"
            icon="pi pi-trash"
            severity="secondary"
            text
            @click="clearMessages"
          />
        </div>
      </div>
    </template>

    <!-- New Session Dialog -->
    <Dialog
      v-model:visible="showSessionDialog"
      header="New Chat Session"
      :modal="true"
      :style="{ width: '300px' }"
      @update:visible="(val) => !val && (newSessionTitle = '')"
    >
      <div class="flex flex-column gap-3">
        <InputText
          v-model="newSessionTitle"
          placeholder="Session title (optional)"
          @keyup.enter="handleCreateSession"
        />
        <div class="flex gap-2 justify-content-end">
          <Button
            label="Cancel"
            severity="secondary"
            size="small"
            @click="showSessionDialog = false"
          />
          <Button
            label="Create"
            size="small"
            @click="handleCreateSession"
            :loading="isLoadingSessions"
          />
        </div>
      </div>
    </Dialog>

    <!-- Rename Session Dialog -->
    <Dialog
      v-model:visible="showRenameDialog"
      header="Rename Session"
      :modal="true"
      :style="{ width: '300px' }"
      @update:visible="(val) => !val && (renameSessionTitle = '')"
    >
      <div class="flex flex-column gap-3">
        <label class="text-sm">Session title</label>
        <InputText
          v-model="renameSessionTitle"
          placeholder="Enter new title"
          autofocus
          @keyup.enter="handleRenameSession"
        />
        <div class="flex gap-2 justify-content-end">
          <Button
            label="Cancel"
            severity="secondary"
            size="small"
            @click="showRenameDialog = false"
          />
          <Button
            label="Rename"
            size="small"
            @click="handleRenameSession"
          />
        </div>
      </div>
    </Dialog>

    <!-- Delete Confirmation Dialog -->
    <Dialog
      v-model:visible="showDeleteConfirmation"
      header="Delete Session"
      :modal="true"
      :style="{ width: '300px' }"
    >
      <p>Are you sure you want to delete this session? This action cannot be undone.</p>
      <template #footer>
        <Button
          label="Cancel"
          severity="secondary"
          size="small"
          @click="showDeleteConfirmation = false"
        />
        <Button
          label="Delete"
          severity="danger"
          size="small"
          @click="confirmDeleteSession"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.ai-assistant {
  height: 100%;
  display: flex;
  flex-direction: column;
}

/* Sessions View */
.sessions-view {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
}

.empty-sessions {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
  padding: 2rem;
}

.sessions-list-container {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.session-card {
  padding: 1rem;
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  cursor: pointer;
  transition: all 0.15s ease;
  background-color: var(--p-surface-0);
  border-left: 3px solid transparent;
}

.session-card:hover {
  background-color: var(--p-surface-50);
  border-color: var(--p-surface-300);
}

.session-card.active {
  background-color: var(--p-primary-50);
  border-left-color: var(--p-primary-500);
  border-color: var(--p-primary-200);
}

.session-card-content {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.session-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.session-card-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  opacity: 0;
  transition: opacity 0.15s ease;
}

.session-card:hover .session-card-actions {
  opacity: 1;
}

.session-action-btn {
  flex-shrink: 0;
}

.session-card-title {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--p-text-color);
  flex: 1;
  white-space: nowrap;
  text-overflow: ellipsis;
  overflow: hidden;
}

.session-card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.session-card-date {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
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

/* Messages Container */
.messages-container {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

/* User Message Bubble */
.user-message {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  margin-left: 1rem;
}

.user-message .message-label {
  align-self: flex-start;
  margin-bottom: 0.35rem;
}

.message-bubble {
  max-width: 85%;
  padding: 0.625rem 0.875rem;
  background-color: var(--p-primary-500);
  color: white;
  border-radius: 0.75rem;
  font-size: 0.85rem;
  line-height: 1.5;
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
}

/* Timeline Styles */
.timeline {
  display: flex;
  flex-direction: column;
  gap: 0;
  position: relative;
  margin-left: 0.5rem;
}

.timeline-item {
  display: flex;
  gap: 1rem;
  position: relative;
  padding: 0.5rem 0;
  padding-left: 1.5rem;
}

/* Simple bullet */
.timeline-bullet {
  position: absolute;
  left: 0;
  top: 0.25rem;
  font-size: 1.75rem;
  line-height: 1;
  color: var(--p-surface-400);
  flex-shrink: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  transform: translateX(-0.35rem);
}

.timeline-bullet.tool {
  color: var(--p-green-500);
}

.timeline-bullet.regular {
  color: var(--p-surface-400);
}

/* Connecting line */
.timeline-connector {
  position: absolute;
  left: -0.5px;
  top: 1.75rem;
  width: 1px;
  height: calc(100% + 0.5rem);
  background-color: var(--p-surface-300);
}

.timeline-item.is-last .timeline-connector {
  display: none;
}

/* Content */
.timeline-content {
  flex: 1;
  min-width: 0;
  padding-top: 0.125rem;
}

.message-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.25rem;
  font-size: 0.75rem;
  flex-wrap: wrap;
}

.role-name {
  font-weight: 600;
  color: var(--p-text-color);
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
  white-space: nowrap;
}

.message-time {
  color: var(--p-text-secondary);
  font-size: 0.7rem;
  margin-left: auto;
}

.message-text {
  font-size: 0.85rem;
  line-height: 1.5;
  color: var(--p-text-color);
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
}

.tool-status {
  margin-top: 0.5rem;
  margin-left: -1.5rem;
  padding: 0.375rem 0.625rem;
  border-radius: 0.375rem;
  font-size: 0.7rem;
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-weight: 500;
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
:global(.app-dark) .session-card {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .session-card:hover {
  background-color: var(--p-surface-700);
  border-color: var(--p-surface-600);
}

:global(.app-dark) .session-card.active {
  background-color: var(--p-primary-900);
  border-color: var(--p-primary-700);
}

:global(.app-dark) .ai-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .error-banner {
  background-color: var(--p-red-900);
  color: var(--p-red-100);
  border-color: var(--p-red-800);
}

:global(.app-dark) .message-bubble {
  background-color: var(--p-primary-600);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
}

:global(.app-dark) .timeline-connector {
  background-color: var(--p-surface-600);
}

:global(.app-dark) .input-area {
  border-color: var(--p-surface-700);
}
</style>

