<script setup lang="ts">
/**
 * AiAssistant.vue
 * Generic AI Assistant sidebar component
 * Provides chat interface for AI-assisted development with real backend integration
 * Can be used in any context: Apps, Flows, Steps, etc.
 */
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import Textarea from 'primevue/textarea'
import Dialog from 'primevue/dialog'
import { useAiChat } from '@/shared/composables/useAiChat'
import { aiUtilityService } from '@/shared/services/ai-utility.service'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { ConversationMode } from '@/shared/types/ai.types'
import ToolOperationsGroup from './ToolOperationsGroup.vue'
import ReasoningBlock from './ReasoningBlock.vue'
import AiModeModelSelector from '@/shared/components/ui/AiModeModelSelector.vue'
import type { ChatSession, ChatMessage } from '@/shared/types/ai.types'
import type { UiLogEntry } from '../types/console.types'
import { UiAutomationAction } from '../services/preview-automation-bridge'

// Props
const props = withDefaults(
  defineProps<{
    resourceType: string
    resourceId: string
    runnerId?: string | null
    initialPrompt?: string
    initialMode?: string
    initialModel?: string
    systemPrompt?: string
    suggestions?: string[]
    uiLogs?: UiLogEntry[]
    toolExecutionHandler?: (
      toolName: string,
      args: any,
      result?: any,
      toolCallId?: string,
    ) => any | Promise<any>
  }>(),
  {
    runnerId: null,
    initialPrompt: undefined,
    initialMode: undefined,
    initialModel: undefined,
    systemPrompt: undefined,
    suggestions: () => [],
    uiLogs: () => [],
    toolExecutionHandler: undefined,
  },
)

// Emits
const emit = defineEmits<{
  (e: 'codeGenerated', code: string): void
  (e: 'toolExecuted', toolName: string, args: any, result?: any, toolCallId?: string): void
  (e: 'sessionCreated'): void
  (e: 'chatMessageSent'): void
  (e: 'sessionNameGenerated', name: string): void
  (e: 'initialPromptConsumed'): void
  (e: 'liveChatStateUpdated', payload: {
    activeSession: ChatSession | null
    messages: ChatMessage[]
    isBusy: boolean
  }): void
}>()

const { showAiDebugDetails } = useUserSettings()
const router = useRouter()

// AI Chat composable with tool execution callback
const {
  messages,
  isGenerating,
  selectedModel,
  availableModels,
  modelDisplayNames,
  error,
  hasMessages,
  loadProviders,
  sendMessage: sendAiMessage,
  getOrCreateActiveSession,
  cancelGeneration,
  sessions,
  activeSession,
  isLoadingSessions,
  isLoadingMoreSessions,
  hasMoreSessions,
  loadSessions,
  loadMoreSessions,
  loadSessionMessages,
  createSession,
  activateSession,
  updateSessionTitle,
  deleteSession,
  conversationMode,
  isUsingSharedKey,
} = useAiChat(props.resourceType, props.resourceId, {
  // Callback when AI tools are executed
  onToolExecuted: async (toolName, args, result, toolCallId) => {
    emit('toolExecuted', toolName, args, result, toolCallId)
    if (props.toolExecutionHandler) {
      return await props.toolExecutionHandler(toolName, args, result, toolCallId)
    }
    return null
  },
})

// View state: 'sessions' (list view) or 'chat' (detail view)
const currentView = ref<'sessions' | 'chat'>('sessions')

// True when providers have loaded but no API keys are configured for any of them
const hasNoKeys = computed(() => availableModels.value.length === 0)

// Track which tool result messages are expanded

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
const contextStartNotice = ref<string | null>(null)
// True while we're setting up a brand-new session before the AI starts streaming.
// Drives loading indicators immediately on submit so the user doesn't wait.
const isPreparingSession = ref(false)
// Combined "busy" flag: true when preparing the session OR when the AI is generating
const isBusy = computed(() => isPreparingSession.value || isGenerating.value)
// Show the generic "Working..." spinner only when busy but no streaming message has arrived yet.
// Once any streaming message exists (including retry notifications), that message IS the status indicator.
const showWorkingIndicator = computed(() =>
  isBusy.value && !messages.value.some((m) => m.role === 'assistant' && m.isStreaming),
)
const sortedSessions = computed(() => {
  // Active session always floats to the top; the rest are already sorted by updatedAt from the server
  const active = sessions.value.find(s => s.isActive)
  const rest = sessions.value.filter(s => !s.isActive)
  return active ? [active, ...rest] : rest
})

const latestSession = computed(() => sortedSessions.value[0] || null)

const getModelDisplayName = (modelId?: string | null) => {
  if (!modelId) return ''
  return modelDisplayNames.value[modelId] || modelId
}

type VersionSummary = { overview: string; changes: string[] }

/**
 * Extract a trailing version-summary JSON block emitted by the AI
 * (e.g. {"overview":"...","changes":[...]} at the end of assistant messages).
 * Returns the main text and the parsed summary (if found).
 */
function extractVersionSummary(content: string): { text: string; summary: VersionSummary | null } {
  const trimmed = content.trimEnd()
  if (!trimmed.endsWith('}')) return { text: content, summary: null }

  // Walk backwards to find the opening { that matches the final }
  let depth = 0
  let startIdx = -1
  for (let i = trimmed.length - 1; i >= 0; i--) {
    if (trimmed[i] === '}') depth++
    else if (trimmed[i] === '{') {
      depth--
      if (depth === 0) {
        startIdx = i
        break
      }
    }
  }

  if (startIdx === -1) return { text: content, summary: null }

  const jsonBlock = trimmed.slice(startIdx)
  if (!jsonBlock.includes('"overview"') || !jsonBlock.includes('"changes"')) {
    return { text: content, summary: null }
  }

  try {
    const parsed = JSON.parse(jsonBlock)
    if (parsed.overview && Array.isArray(parsed.changes)) {
      return {
        text: content.slice(0, startIdx).trimEnd(),
        summary: parsed as VersionSummary,
      }
    }
  } catch {
    // Not valid JSON – render as-is
  }

  return { text: content, summary: null }
}

const formatVerificationStrategy = (strategy?: string | null) => {
  switch (strategy?.toLowerCase()) {
    case 'continuous':
      return 'Continuous'
    case 'final-gate':
      return 'Final Gate'
    case 'balanced':
      return 'Balanced'
    default:
      return strategy || ''
  }
}

const formatExecutionPolicy = (policy?: string | null) => {
  switch (policy?.toLowerCase()) {
    case 'lean-tools':
      return 'Lean Tools'
    case 'default':
      return 'Default'
    default:
      return policy || ''
  }
}

// Search — server-side, debounced
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null
watch(sessionsSearchQuery, (value) => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
  searchDebounceTimer = setTimeout(() => {
    void loadSessions(value.trim() || undefined)
  }, 300)
})

// Infinite scroll sentinel
const scrollSentinelRef = ref<HTMLElement | null>(null)
let intersectionObserver: IntersectionObserver | null = null

function setupIntersectionObserver() {
  if (intersectionObserver) intersectionObserver.disconnect()
  if (!scrollSentinelRef.value) return
  intersectionObserver = new IntersectionObserver(
    (entries) => {
      if (entries[0]?.isIntersecting && hasMoreSessions.value) {
        void loadMoreSessions()
      }
    },
    { threshold: 0.1 }
  )
  intersectionObserver.observe(scrollSentinelRef.value)
}

watch(scrollSentinelRef, (el) => {
  if (el) setupIntersectionObserver()
})

onUnmounted(() => {
  intersectionObserver?.disconnect()
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
})

// UI state
const userInput = ref('')

// Quick suggestions - use provided or empty array
const displaySuggestions = computed(() => props.suggestions || [])

// Group consecutive tool messages for collapsible display
type MessageGroup =
  | { type: 'user' | 'assistant'; message: ChatMessage }
  | { type: 'tool-group'; messages: ChatMessage[]; id: string }

const messageGroups = computed<MessageGroup[]>(() => {
  const groups: MessageGroup[] = []
  let toolBatch: ChatMessage[] = []

  function flushTools() {
    if (toolBatch.length > 0) {
      groups.push({ type: 'tool-group', messages: [...toolBatch], id: `tg-${toolBatch[0].id}` })
      toolBatch = []
    }
  }

  for (const msg of messages.value) {
    if (!showAiDebugDetails.value && msg.type === 'uiTestResult') {
      continue
    }

    // Groupable: regular tool execution (not special cards like planApproval/instructionUpdate)
    const isGroupable =
      msg.role === 'assistant' &&
      msg.toolName &&
      msg.type !== 'instructionUpdate' &&
      (showAiDebugDetails.value || msg.type !== 'uiTestResult')

    if (isGroupable) {
      toolBatch.push(msg)
    } else {
      flushTools()
      groups.push({ type: msg.role === 'user' ? 'user' : 'assistant', message: msg })
    }
  }
  flushTools()
  return groups
})

// Load providers and session on mount, auto-send initial prompt if provided
onMounted(async () => {
  await loadProviders()
  
  // Load sessions but don't create one automatically
  await loadSessions()
  if (activeSession.value?.id) {
    await loadSessionMessages(activeSession.value.id)
    currentView.value = 'chat'
  }

  // Apply initial mode/model from query params (set by the "Generate App" flow)
  // Must run after loadProviders() so availableModels is populated
  if (props.initialMode) {
    conversationMode.value = props.initialMode as typeof conversationMode.value
  }
  if (props.initialModel && availableModels.value.includes(props.initialModel)) {
    selectedModel.value = props.initialModel
  }

  // Always stay in sessions list view by default
  // User can click on a session to open it
  
  // Auto-send initial prompt if provided (e.g., from "Generate App" button)
  if (props.initialPrompt) {
    emit('initialPromptConsumed')

    // If no AI keys are configured, stay on sessions view so the banner is visible
    if (hasNoKeys.value) {
      return
    }

    contextStartNotice.value = 'New app context started from Apps prompt.'
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

watch(
  [activeSession, messages, isBusy],
  () => {
    emit('liveChatStateUpdated', {
      activeSession: activeSession.value ?? null,
      messages: [...messages.value],
      isBusy: isBusy.value,
    })
  },
  { deep: true, immediate: true },
)

// Send message with optimistic UI
async function sendMessage() {
  if (!userInput.value.trim() || isBusy.value) return

  const messageContent = userInput.value.trim()
  userInput.value = ''

  // If we're in the sessions list view, always create a new session
  // If we're in chat view with no active session, also create one
  if (currentView.value === 'sessions' || !activeSession.value) {
    // 1. Clear previous session state so chat view doesn't briefly show old data
    activeSession.value = null
    messages.value = []

    // 2. Navigate to chat view immediately — user should not wait on the sessions list
    currentView.value = 'chat'

    // 3. Add user message optimistically so it appears before any backend calls complete
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'user',
      content: messageContent,
      timestamp: new Date(),
    })

    // 3. Show loading indicator immediately — createSession takes a few backend calls
    isPreparingSession.value = true

    try {
      // 4. Create the session on the backend (activate only — messages are not loaded
      //    because a brand-new session is always empty and we already have the user message above)
      const temporaryName = 'Generating...'
      await createSession(temporaryName)
      emit('sessionCreated')
    } finally {
      // isGenerating will take over once sendAiMessage runs
      isPreparingSession.value = false
    }

    // 5. Build context for AI message
    const context: Record<string, any> = {
      resourceType: props.resourceType,
      resourceId: props.resourceId,
      ...buildUiAutomationContext(),
    }
    
    if (props.runnerId) {
      context.runnerId = props.runnerId
    }

    // Use provided system prompt or a generic default
    const systemPrompt =
      props.systemPrompt ||
      'You are an AI assistant helping with this task. Provide helpful, accurate responses based on the context provided.'

    // 6. Send to AI — pass true so sendAiMessage doesn't add the user message a second time.
    const sendPromise = sendAiMessage(messageContent, systemPrompt, context, true)
    
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
      ...buildUiAutomationContext(),
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
    // Notify parent so it can use the AI-generated name (e.g., update app name)
    emit('sessionNameGenerated', sessionName)
  }
}

function handleSuggestion(suggestion: string) {
  userInput.value = suggestion
}

function parseJsonMaybe(value: unknown): unknown {
  if (typeof value !== 'string') return value
  const trimmed = value.trim()
  if (!trimmed || (trimmed[0] !== '{' && trimmed[0] !== '[')) return value
  try {
    return JSON.parse(trimmed)
  } catch {
    return value
  }
}

function buildUiAutomationContext() {
  const recentUiResults = [...messages.value]
    .filter((message) => message.type === 'uiTestResult' && message.uiTestResult)
    .slice(-8)
    .map((message) => ({
      action: message.uiTestResult?.action,
      success: message.uiTestResult?.success,
      details: message.uiTestResult?.details,
      error: message.uiTestResult?.error,
      timestamp: message.timestamp.toISOString(),
    }))

  const recentConsoleLogToolResults = [...messages.value]
    .filter((message) => message.type === 'toolExecution' && message.toolName === UiAutomationAction.GetConsoleLogs && message.toolResult)
    .slice(-3)
    .map((message) => {
      const parsed = parseJsonMaybe(message.toolResult)
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        return {
          details: typeof message.toolResult === 'string' ? message.toolResult : '',
          logs: [],
          timestamp: message.timestamp.toISOString(),
        }
      }

      const result = parsed as { details?: string; logs?: Array<{ timestamp?: string; level?: string; source?: string; message?: string }> }
      return {
        details: result.details ?? '',
        logs: Array.isArray(result.logs) ? result.logs.slice(0, 50) : [],
        timestamp: message.timestamp.toISOString(),
      }
    })

  const recentPreviewConsoleLogs = [...(props.uiLogs || [])]
    .slice(0, 40)
    .map((entry) => ({
      timestamp: entry.timestamp?.toISOString?.() ?? String(entry.timestamp),
      level: entry.level,
      source: entry.source,
      message: entry.message,
    }))

  const recentPreviewConsoleErrors = recentPreviewConsoleLogs
    .filter((entry) => entry.level === 'error' || entry.level === 'warn')
    .slice(0, 20)

  return {
    availableUiAutomationTools: Object.values(UiAutomationAction),
    recentUiAutomationResults: recentUiResults,
    recentConsoleLogToolResults,
    recentPreviewConsoleLogs,
    recentPreviewConsoleErrors,
  }
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
          
          <div class="flex align-items-center gap-2">
            <Button
              v-tooltip.top="'New session'"
              icon="pi pi-plus"
              size="small"
              text
              @click="showSessionDialog = true"
            />
          </div>
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
        <div class="mt-2">
          <IconField class="w-full">
            <InputIcon
              :class="sessionsSearchQuery ? 'pi pi-times cursor-pointer' : 'pi pi-search'"
              @click="sessionsSearchQuery ? sessionsSearchQuery = '' : undefined"
            />
            <InputText
              v-model="sessionsSearchQuery"
              placeholder="Search sessions..."
              size="small"
              class="w-full"
            />
          </IconField>
        </div>
      </div>

      <!-- Sessions List -->
      <div class="sessions-view">
        <!-- No API keys banner -->
        <div v-if="hasNoKeys && !isLoadingSessions" class="no-keys-banner">
          <i class="pi pi-key text-2xl mb-2" style="color: var(--p-yellow-500)"></i>
          <p class="text-sm font-semibold mb-1">No AI key configured</p>
          <p class="text-xs text-color-secondary mb-3">Add an API key to start using AI features.</p>
          <Button
            label="Add AI Key"
            icon="pi pi-key"
            size="small"
            severity="secondary"
            outlined
            @click="router.push('/ai-keys')"
          />
        </div>

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
            v-for="session in sortedSessions"
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

          <!-- Infinite scroll sentinel -->
          <div ref="scrollSentinelRef" class="scroll-sentinel">
            <i v-if="isLoadingMoreSessions" class="pi pi-spin pi-spinner text-color-secondary"></i>
          </div>
        </div>
      </div>

      <!-- Input area always visible -->
      <div class="input-area">
        <div class="prompt-box" :class="{ 'prompt-box--disabled': hasNoKeys }">
          <Textarea
            v-model="userInput"
            :placeholder="hasNoKeys ? 'Add an AI key to start chatting...' : 'Ask AI a question...'"
            class="prompt-textarea"
            :disabled="isBusy || hasNoKeys"
            :auto-resize="true"
            rows="2"
            @keydown="handleKeyPress"
          />
          <div class="prompt-toolbar">

            <div class="prompt-right">
              <AiModeModelSelector
                v-model:mode="conversationMode"
                v-model:model="selectedModel"
                :available-models="availableModels"
                :model-display-names="modelDisplayNames"
                :disabled="isBusy"
                :shared-key="isUsingSharedKey"
                compact
              />
              <Button
                v-if="isBusy"
                v-tooltip.top="'Cancel'"
                icon="pi pi-stop-circle"
                severity="danger"
                size="small"
                text
                rounded
                @click="cancelGeneration"
              />
              <Button
                v-else
                v-tooltip.top="hasNoKeys ? 'No AI key configured' : !selectedModel ? 'Please select an AI model' : 'Send (Enter)'"
                icon="pi pi-send"
                size="small"
                text
                rounded
                :disabled="!userInput.trim() || !selectedModel || hasNoKeys"
                @click="sendMessage"
              />
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Chat View (Session Detail) -->
    <template v-else>
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
          <div
            v-if="activeSession?.effectiveVerificationStrategy || activeSession?.effectiveExecutionPolicy"
            class="header-strategy-badge"
          >
            <i class="pi pi-shield"></i>
            <span v-if="activeSession?.effectiveVerificationStrategy">
              Strategy: {{ formatVerificationStrategy(activeSession.effectiveVerificationStrategy) }}
            </span>
            <span v-if="activeSession?.effectiveVerificationStrategy && activeSession?.effectiveExecutionPolicy" class="text-separator">|</span>
            <span v-if="activeSession?.effectiveExecutionPolicy">
              Policy: {{ formatExecutionPolicy(activeSession.effectiveExecutionPolicy) }}
            </span>
          </div>
        </div>
      </div>


      <!-- Messages area -->
      <div class="messages-area">
        <div v-if="contextStartNotice" class="context-start-notice">
          <div class="context-start-notice__content">
            <i class="pi pi-info-circle"></i>
            <span>{{ contextStartNotice }}</span>
          </div>
          <Button
            icon="pi pi-times"
            text
            rounded
            size="small"
            class="context-start-notice__close"
            @click="contextStartNotice = null"
          />
        </div>
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

        <!-- Messages: User in bubbles, AI in timeline, tools in collapsible groups -->
        <div v-else class="messages-container">
          <template v-for="(group, gIdx) in messageGroups" :key="group.type === 'tool-group' ? group.id : group.message.id">
            <!-- User message - Chat bubble -->
            <div v-if="group.type === 'user'" class="user-message">
              <div class="message-bubble">{{ group.message.content }}</div>
            </div>

            <!-- Tool operations group - Collapsible -->
            <div v-else-if="group.type === 'tool-group'" class="timeline">
              <div class="timeline-item has-tool" :class="{ 'is-last': gIdx === messageGroups.length - 1 && !isBusy }">
                <div class="timeline-bullet tool">•</div>
                <div v-if="gIdx < messageGroups.length - 1 || isBusy" class="timeline-connector"></div>
                <div class="timeline-content">
                  <ToolOperationsGroup :operations="group.messages" :show-debug-details="showAiDebugDetails" />
                </div>
              </div>
            </div>

            <!-- AI message - Timeline -->
            <div v-else class="timeline">
              <div
                class="timeline-item"
                :class="[
                  { 'is-last': gIdx === messageGroups.length - 1 && !isBusy },
                  'regular'
                ]"
              >
                <div class="timeline-bullet regular">•</div>
                <div v-if="gIdx < messageGroups.length - 1 || isBusy" class="timeline-connector"></div>
                <div class="timeline-content">
                  <!-- Instruction update notification -->
                  <div v-if="group.message.type === 'instructionUpdate'" class="instruction-notification">
                    <i class="pi pi-file-edit"></i>
                    {{ group.message.instructionNotification || 'Instructions updated.' }}
                  </div>
                  <div
                    v-else-if="group.message.type === 'uiTestResult' && showAiDebugDetails"
                    class="ui-test-notification"
                    :class="{ error: !group.message.uiTestResult?.success }"
                  >
                    <div class="font-semibold mb-1">
                      <i :class="group.message.uiTestResult?.success ? 'pi pi-check-circle text-green-500' : 'pi pi-times-circle text-red-500'"></i>
                      {{ group.message.content }}
                    </div>
                    <div v-if="group.message.uiTestResult?.details" class="text-sm">
                      {{ group.message.uiTestResult.details }}
                    </div>
                    <div v-if="group.message.uiTestResult?.value" class="text-xs text-color-secondary mt-1">
                      Value: {{ group.message.uiTestResult.value }}
                    </div>
                    <div v-if="group.message.uiTestResult?.snapshotTitle" class="text-xs text-color-secondary mt-1">
                      Snapshot: {{ group.message.uiTestResult.snapshotTitle }}
                    </div>
                    <div v-if="group.message.uiTestResult?.error" class="text-xs text-red-400 mt-1">
                      {{ group.message.uiTestResult.error }}
                    </div>
                  </div>
                  <!-- Regular AI message -->
                  <template v-else>
                    <ReasoningBlock
                      v-if="group.message.reasoningContent"
                      :content="group.message.reasoningContent"
                    />
                    <!-- Rate-limit notice (works for both shared and personal keys) -->
                    <template v-if="group.message.errorCode === 'RATE_LIMIT'">
                      <div class="rate-limit-notice">
                        <i class="pi pi-info-circle"></i>
                        <span>{{ group.message.content }}</span>
                      </div>
                      <!-- Upgrade CTA only shown when using the shared demo key -->
                      <div v-if="isUsingSharedKey" class="upgrade-key-cta">
                        <router-link to="/ai-keys" class="upgrade-key-link">
                          <i class="pi pi-key"></i> Add your own API key
                        </router-link>
                        <span class="upgrade-key-hint">Free on OpenRouter — takes 30 seconds.</span>
                      </div>
                    </template>
                    <!-- Use v-for single-item to bind extractVersionSummary result as a template variable -->
                    <template v-else v-for="extracted in [extractVersionSummary(group.message.content)]" :key="gIdx">
                      <div v-if="extracted.text" class="message-text">
                        {{ extracted.text }}
                      </div>
                      <div v-if="extracted.summary" class="version-summary">
                        <p class="version-summary__overview">{{ extracted.summary.overview }}</p>
                        <div v-if="extracted.summary.changes.length > 0" class="version-summary__changes">
                          <span class="version-summary__changes-label">Changes:</span>
                          <ul class="version-summary__list">
                            <li v-for="(change, idx) in extracted.summary.changes" :key="idx">{{ change }}</li>
                          </ul>
                        </div>
                      </div>
                      <div v-if="!extracted.text && !extracted.summary" class="message-text">
                        {{ group.message.content }}
                      </div>
                    </template>
                  </template>
                  <!-- Model badge: only shown once model is resolved (not during streaming) -->
                  <span v-if="group.message.model && !group.message.isStreaming" class="message-model-badge">
                    <i class="pi pi-microchip-ai"></i>{{ getModelDisplayName(group.message.model) }}
                  </span>
                </div>
              </div>
            </div>
          </template>

          <!-- Generating indicator — only shown before the first streaming chunk arrives -->
          <div v-if="showWorkingIndicator" class="timeline">
            <div class="timeline-item">
              <div class="timeline-bullet">•</div>
              <div class="timeline-content">
                <div class="message-text"><i class="pi pi-spin pi-spinner mr-2"></i>Working...</div>
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
          <div v-if="activeSession.effectiveVerificationStrategy" class="metric-separator"></div>
          <div v-if="activeSession.effectiveVerificationStrategy" class="metric-breakdown">
            <span class="text-xs text-color-secondary">
              Strategy: {{ formatVerificationStrategy(activeSession.effectiveVerificationStrategy) }}
            </span>
          </div>
          <div v-if="activeSession.effectiveExecutionPolicy" class="metric-separator"></div>
          <div v-if="activeSession.effectiveExecutionPolicy" class="metric-breakdown">
            <span class="text-xs text-color-secondary">
              Policy: {{ formatExecutionPolicy(activeSession.effectiveExecutionPolicy) }}
            </span>
          </div>
        </div>
      </div>

      <!-- Input area -->
      <div class="input-area">
        <div class="prompt-box" :class="{ 'prompt-box--disabled': hasNoKeys }">
          <Textarea
            v-model="userInput"
            :placeholder="hasNoKeys ? 'Add an AI key to start chatting...' : 'Describe what you want...'"
            class="prompt-textarea"
            :disabled="isBusy || hasNoKeys"
            :auto-resize="true"
            rows="2"
            @keydown="handleKeyPress"
          />
          <div class="prompt-toolbar">

            <div class="prompt-right">
              <AiModeModelSelector
                v-model:mode="conversationMode"
                v-model:model="selectedModel"
                :available-models="availableModels"
                :model-display-names="modelDisplayNames"
                :disabled="isBusy"
                :shared-key="isUsingSharedKey"
                compact
              />
              <Button
                v-if="isBusy"
                v-tooltip.top="'Cancel'"
                icon="pi pi-stop-circle"
                severity="danger"
                size="small"
                text
                rounded
                @click="cancelGeneration"
              />
              <Button
                v-else
                v-tooltip.top="hasNoKeys ? 'No AI key configured' : !selectedModel ? 'Please select an AI model' : 'Send (Enter)'"
                icon="pi pi-send"
                size="small"
                text
                rounded
                :disabled="!userInput.trim() || !selectedModel || hasNoKeys"
                @click="sendMessage"
              />
            </div>
          </div>
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

.no-keys-banner {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 1.25rem 1rem;
  margin-bottom: 1rem;
  border-radius: 8px;
  background: var(--p-surface-100);
  border: 1px solid var(--p-surface-200);
}

:root.app-dark .no-keys-banner {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
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

.scroll-sentinel {
  display: flex;
  justify-content: center;
  padding: 0.5rem 0;
  min-height: 1px;
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

.header-strategy-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.25rem 0.5rem;
  border-radius: 9999px;
  border: 1px solid var(--p-primary-300);
  background-color: var(--p-primary-50);
  color: var(--p-primary-900);
  font-size: 0.72rem;
  white-space: nowrap;
}

:global(.app-dark) .header-strategy-badge {
  border-color: color-mix(in srgb, var(--p-primary-400) 55%, transparent);
  background-color: color-mix(in srgb, var(--p-primary-900) 25%, transparent);
  color: var(--p-primary-100);
}

.model-select {
  min-width: 120px;
  font-size: 0.875rem;
}

.messages-area {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
}

.context-start-notice {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  padding: 0.5rem 0.75rem;
  border-radius: 8px;
  border: 1px solid var(--p-surface-300);
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
}

.context-start-notice__content {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.context-start-notice__close {
  flex: 0 0 auto;
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

.version-summary {
  margin-top: 0.75rem;
  padding: 0.75rem 1rem;
  border-left: 3px solid var(--p-primary-color);
  background: var(--p-surface-50);
  border-radius: 0 0.375rem 0.375rem 0;
}

.version-summary__overview {
  font-size: 0.85rem;
  line-height: 1.6;
  margin: 0 0 0.625rem 0;
  color: var(--p-text-color);
}

.version-summary__changes-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  margin-bottom: 0.375rem;
  color: var(--p-text-color);
}

.version-summary__list {
  margin: 0;
  padding-left: 1.25rem;
  list-style-type: disc;
}

.version-summary__list li {
  font-size: 0.8rem;
  line-height: 1.5;
  margin-bottom: 0.3rem;
  color: var(--p-text-color);
}

.version-summary__list li:last-child {
  margin-bottom: 0;
}

.message-model-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 0.5rem;
  padding: 2px 8px;
  font-size: 0.68rem;
  font-family: monospace;
  color: var(--p-surface-400);
  background: var(--p-surface-100);
  border: 1px solid var(--p-surface-200);
  border-radius: 999px;

  .app-dark & {
    background: var(--p-surface-800);
    border-color: var(--p-surface-700);
  }
}

.rate-limit-notice {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  color: var(--p-orange-600);
  font-size: 0.875rem;
  margin-bottom: 0.5rem;

  .app-dark & {
    color: var(--p-orange-400);
  }
}

.upgrade-key-cta {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: 0.25rem;
}

.upgrade-key-link {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.3rem 0.75rem;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  background-color: var(--p-primary-500);
  color: #fff;
  text-decoration: none;
  transition: background-color 0.15s;

  &:hover {
    background-color: var(--p-primary-600);
  }
}

.upgrade-key-hint {
  font-size: 0.75rem;
  color: var(--p-surface-500);
}

.tool-call-line {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.tool-call-name {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--p-text-color);
}

.instruction-notification {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  font-style: italic;
}

.ui-test-notification {
  border: 1px solid var(--p-green-200);
  background: var(--p-green-50);
  border-radius: var(--p-border-radius);
  padding: 0.6rem 0.75rem;
}

.ui-test-notification.error {
  border-color: var(--p-red-200);
  background: var(--p-red-50);
}

.tool-call-icon.executing {
  color: var(--p-blue-500);
  font-size: 0.7rem;
}

.tool-call-icon.completed {
  color: var(--p-green-500);
  font-size: 0.7rem;
}

.tool-call-icon.error {
  color: var(--p-red-500);
  font-size: 0.7rem;
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

.prompt-box {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--p-surface-300);
  border-radius: 10px;
  background: var(--p-surface-0);
  padding: 6px 8px 4px 10px;
  transition: border-color 0.15s;
}

.prompt-box:focus-within {
  border-color: var(--p-primary-400);
}

.prompt-box--disabled {
  opacity: 0.5;
  cursor: not-allowed;
  background: var(--p-surface-100) !important;
}

.prompt-textarea {
  border: none !important;
  box-shadow: none !important;
  background: transparent !important;
  padding: 0 !important;
  min-height: 40px;
  max-height: 180px;
  resize: none;
  line-height: 1.5;
  width: 100%;
}

.prompt-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 4px;
  padding-top: 5px;
  border-top: 1px solid var(--p-surface-200);
}

.prompt-right {
  display: flex;
  align-items: center;
  gap: 2px;
}

.model-select-inline {
  min-width: 130px;
  font-size: 0.75rem;
}

/* Dark mode - removed session-card overrides as we now use CSS variables */
:global(.app-dark) .ai-header {
  border-color: var(--p-surface-700);
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

:global(.app-dark) .prompt-box {
  border-color: var(--p-surface-600);
  background: var(--p-surface-800);
}

:global(.app-dark) .prompt-box--disabled {
  background: var(--p-surface-900) !important;
}

:global(.app-dark) .prompt-box:focus-within {
  border-color: var(--p-primary-400);
}

:global(.app-dark) .prompt-toolbar {
  border-color: var(--p-surface-600);
}

</style>
