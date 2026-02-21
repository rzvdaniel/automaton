<script setup lang="ts">
/**
 * AiAssistant.vue
 * Generic AI Assistant sidebar component
 * Provides chat interface for AI-assisted development with real backend integration
 * Can be used in any context: Apps, Flows, Steps, etc.
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
const props = withDefaults(
  defineProps<{
    resourceType: string
    resourceId: string
    runnerId?: string | null
    initialPrompt?: string
    systemPrompt?: string
    suggestions?: string[]
  }>(),
  {
    runnerId: null,
    initialPrompt: undefined,
    systemPrompt: undefined,
    suggestions: () => [],
  },
)

// Emits
const emit = defineEmits<{
  (e: 'codeGenerated', code: string): void
  (e: 'toolExecuted', toolName: string, args: any, result?: any): void
  (e: 'sessionCreated'): void
  (e: 'chatMessageSent'): void
}>()

// AI Chat composable with tool execution callback
const {
  messages,
  isGenerating,
  selectedModel,
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
  loadSessionMessages,
  createSession,
  activateSession,
  updateSessionTitle,
  deleteSession,
} = useAiChat(props.resourceType, props.resourceId, {
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
const isGeneratingSessionName = ref(false) // Track when session name is being generated
const isResumingSession = ref(false)
const sortedSessions = computed(() => {
  return [...sessions.value].sort((a, b) => {
    // Active session first, then by creation date descending
    if (a.isActive) return -1
    if (b.isActive) return 1
    return new Date(b.updatedAt || b.createdAt || 0).getTime() - new Date(a.updatedAt || a.createdAt || 0).getTime()
  })
})

const latestSession = computed(() => sortedSessions.value[0] || null)

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

// Quick suggestions - use provided or empty array
const displaySuggestions = computed(() => props.suggestions || [])

// Load providers and session on mount, auto-send initial prompt if provided
onMounted(async () => {
  await loadProviders()
  
  // Load sessions but don't create one automatically
  await loadSessions()
  
  // Always stay in sessions list view by default
  // User can click on a session to open it
  
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
  emit('sessionCreated')
}

// Handle switching to a different session
async function handleSelectSession(session: ChatSession) {
  if (session.id !== activeSession.value?.id) {
    await activateSession(session.id)
  } else {
    await loadSessionMessages(session.id)
  }
  currentView.value = 'chat'
}

// Resume the last active session (if any)
async function handleResumeLastSession() {
  const targetSession = latestSession.value || activeSession.value
  if (!targetSession) return

  isResumingSession.value = true
  try {
    if (targetSession.id !== activeSession.value?.id) {
      await activateSession(targetSession.id)
    } else {
      await loadSessionMessages(targetSession.id)
    }
    currentView.value = 'chat'
  } finally {
    isResumingSession.value = false
  }
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

function formatTokens(tokens: number): string {
  if (tokens >= 1_000_000) return `${(tokens / 1_000_000).toFixed(1)}M`
  if (tokens >= 1_000) return `${(tokens / 1_000).toFixed(1)}K`
  return tokens.toString()
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

// Send message with optimistic UI
async function sendMessage() {
  if (!userInput.value.trim() || isGenerating.value) return

  const messageContent = userInput.value.trim()
  userInput.value = ''

  // If we're in the sessions list view, always create a new session
  // If we're in chat view with no active session, also create one
  if (currentView.value === 'sessions' || !activeSession.value) {
    // Optimistic UI: Create session immediately with temporary name
    // This creates the session on the backend with a temporary title
    const temporaryName = 'Generating...'
    await createSession(temporaryName)
    emit('sessionCreated')
    
    // Switch to chat view immediately (better UX - no waiting in sessions list)
    currentView.value = 'chat'
    
    // Build context for AI message
    const context: Record<string, any> = {
      resourceType: props.resourceType,
      resourceId: props.resourceId,
    }
    
    if (props.runnerId) {
      context.runnerId = props.runnerId
    }

    // Use provided system prompt or a generic default
    const systemPrompt =
      props.systemPrompt ||
      'You are an AI assistant helping with this task. Provide helpful, accurate responses based on the context provided.'

    // Send message to AI (ui already shows it in chat)
    const sendPromise = sendAiMessage(messageContent, systemPrompt, context)
    
    // Generate proper session name in parallel (don't await - let it run in background)
    generateAndUpdateSessionName(messageContent)
    
    // Wait for AI response
    await sendPromise
    
    // Emit event to notify parent that message was sent (for stats refresh, etc.)
    emit('chatMessageSent')
  } else {
    // Already in chat view with active session - just send the message
    currentView.value = 'chat'
    
    // Build context with resource info
    const context: Record<string, any> = {
      resourceType: props.resourceType,
      resourceId: props.resourceId,
    }
    
    if (props.runnerId) {
      context.runnerId = props.runnerId
    }

    // Use provided system prompt or a generic default
    const systemPrompt =
      props.systemPrompt ||
      'You are an AI assistant helping with this task. Provide helpful, accurate responses based on the context provided.'

    await sendAiMessage(messageContent, systemPrompt, context)
    
    // Emit event to notify parent that message was sent (for stats refresh, etc.)
    emit('chatMessageSent')
  }
}

// Generate and update session name in the background
async function generateAndUpdateSessionName(messageContent: string) {
  if (!activeSession.value) return
  
  isGeneratingSessionName.value = true
  let sessionName: string
  try {
    // Generate session name from the prompt using AI
    sessionName = await aiUtilityService.generateSessionName(messageContent)
  } catch {
    // Fallback: use first few words from the prompt
    const words = messageContent.split(/\s+/).slice(0, 5).join(' ')
    sessionName = words.length > 40 ? words.substring(0, 40) + '...' : words
  } finally {
    isGeneratingSessionName.value = false
  }
  
  // Update session title
  if (activeSession.value && sessionName !== 'Generating...') {
    await updateSessionTitle(activeSession.value.id, sessionName)
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
        <div class="flex align-items-center gap-2 mt-2">
          <Button
            v-if="latestSession"
            v-tooltip.top="'Resume last session'"
            icon="pi pi-history"
            label="Resume last session"
            size="small"
            text
            :loading="isResumingSession"
            @click="handleResumeLastSession"
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
                    class="session-action-btn"
                    @click="openRenameDialog(session)"
                  />
                  <Button
                    v-tooltip.top="'Delete'"
                    icon="pi pi-trash"
                    size="small"
                    text
                    severity="danger"
                    class="session-action-btn"
                    @click="openDeleteConfirmation(session.id)"
                  />
                </div>
              </div>
              <div class="session-card-footer">
                <span class="session-card-date">{{ formatSessionDate(session.createdAt) }}</span>
                <!-- Usage metrics badge -->
                <div v-if="session.totalPromptsSubmitted > 0" class="session-metrics-badge">
                  <i class="pi pi-comments" style="font-size: 0.7rem"></i>
                  <span>{{ session.totalPromptsSubmitted }}</span>
                  <span class="text-separator">·</span>
                  <i class="pi pi-zap" style="font-size: 0.7rem"></i>
                  <span>{{ formatTokens(session.totalTokens) }}</span>
                </div>
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
            v-tooltip.top="!selectedModel ? 'Please select an AI model' : 'Send message'"
            icon="pi pi-send"
            :disabled="!userInput.trim() || isGenerating || !selectedModel"
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
            <i v-if="isGeneratingSessionName" v-tooltip.top="'Generating session name...'" class="pi pi-spin pi-spinner text-sm text-color-secondary"></i>
            <Button
              v-if="activeSession && !isGeneratingSessionName"
              v-tooltip.top="'Rename session'"
              icon="pi pi-pencil"
              size="small"
              text
              class="p-button-text p-button-sm"
              @click="openRenameDialog(activeSession)"
            />
          </div>
          
          <!-- Model Selector -->
          <div v-if="availableModels.length > 0" class="flex align-items-center gap-2">
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
            How can I help you today?
          </p>

          <!-- Quick suggestions (if provided) -->
          <div v-if="displaySuggestions.length > 0" class="suggestions">
            <Button
              v-for="suggestion in displaySuggestions"
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

      <!-- Session usage metrics -->
      <div v-if="activeSession && activeSession.totalPromptsSubmitted > 0" class="session-metrics-footer">
        <div class="metrics-row">
          <div class="metric-item">
            <i class="pi pi-comments"></i>
            <span class="metric-label">Prompts:</span>
            <span class="metric-value">{{ activeSession.totalPromptsSubmitted }}</span>
          </div>
          <div class="metric-separator"></div>
          <div class="metric-item">
            <i class="pi pi-zap"></i>
            <span class="metric-label">Tokens:</span>
            <span class="metric-value">{{ formatTokens(activeSession.totalTokens) }}</span>
          </div>
          <div class="metric-separator"></div>
          <div class="metric-breakdown">
            <span class="text-xs text-color-secondary">
              {{ formatTokens(activeSession.totalInputTokens) }} in / {{ formatTokens(activeSession.totalOutputTokens) }} out
            </span>
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
            v-tooltip.top="!selectedModel ? 'Please select an AI model' : 'Send message'"
            icon="pi pi-send"
            :disabled="!userInput.trim() || isGenerating || !selectedModel"
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
            :loading="isLoadingSessions"
            @click="handleCreateSession"
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
  border-left: 3px solid transparent;
}

.session-card:hover {
  border-color: var(--p-primary-300);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.session-card.active {
  border-left-color: var(--p-primary-500);
  border-color: var(--p-primary-300);
  background: var(--p-highlight-background);
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

.session-metrics-badge {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.25rem 0.5rem;
  background-color: var(--p-primary-100);
  color: var(--p-primary-900);
  border-radius: 0.375rem;
  font-size: 0.7rem;
  white-space: nowrap;
}

.text-separator {
  opacity: 0.5;
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

.session-metrics-footer {
  padding: 0.5rem 0.75rem;
  border-top: 1px solid var(--p-surface-200);
  background-color: var(--p-surface-50);
  flex-shrink: 0;
}

.metrics-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8rem;
}

.metric-item {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  color: var(--p-text-color);
}

.metric-item i {
  font-size: 0.75rem;
  opacity: 0.7;
}

.metric-label {
  color: var(--p-text-muted-color);
  font-weight: 500;
}

.metric-value {
  font-weight: 600;
  color: var(--p-primary-600);
}

.metric-separator {
  width: 1px;
  height: 1rem;
  background-color: var(--p-surface-300);
}

.metric-breakdown {
  margin-left: auto;
  display: flex;
  align-items: center;
}

.input-area {
  padding: 0.75rem;
  border-top: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

/* Dark mode - removed session-card overrides as we now use CSS variables */
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

