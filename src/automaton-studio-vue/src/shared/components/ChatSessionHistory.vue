<script setup lang="ts">
/**
 * ChatSessionHistory.vue
 * Generic component to display AI chat session history for any resource type
 * Reusable across Apps, Steps, Flows, etc.
 */
import { ref, onMounted, computed } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Card from 'primevue/card'
import Divider from 'primevue/divider'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import ScrollPanel from 'primevue/scrollpanel'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { ChatSession, ChatResourceType } from '@/shared/types/ai.types'

interface Props {
  resourceType: ChatResourceType
  resourceId: string
  /** Optional custom title for the panel */
  title?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: 'Chat Session History',
})

const emit = defineEmits<{
  sessionSelected: [session: ChatSession]
  sessionActivated: [session: ChatSession]
}>()

const toast = useToast()
const confirm = useConfirm()

const isLoading = ref(false)
const sessions = ref<ChatSession[]>([])
const searchQuery = ref('')
const isExporting = ref(false)
const exportingSessionId = ref<string | null>(null)

// Dialog state
const showNewSessionDialog = ref(false)
const newSessionTitle = ref('')
const isCreatingSession = ref(false)

const showRenameDialog = ref(false)
const renameSessionId = ref<string | null>(null)
const renameSessionTitle = ref('')
const isRenamingSession = ref(false)

/**
 * Sorted sessions: Active session first, then by creation date descending
 */
const sortedSessions = computed(() => {
  return [...sessions.value].sort((a, b) => {
    if (a.isActive) return -1
    if (b.isActive) return 1
    return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  })
})

/**
 * Filtered sessions by search query
 */
const filteredSessions = computed(() => {
  if (!searchQuery.value.trim()) return sortedSessions.value

  const query = searchQuery.value.toLowerCase()
  return sortedSessions.value.filter((s) => s.title?.toLowerCase().includes(query))
})

/**
 * Load all sessions for the resource
 */
async function loadSessions() {
  if (!props.resourceId) return

  isLoading.value = true
  try {
    const result = await aiChatHistoryService.getSessions(props.resourceType, props.resourceId)
    sessions.value = result.items
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load chat sessions',
      life: 5000,
    })
  } finally {
    isLoading.value = false
  }
}

/**
 * Export all sessions/messages for this resource to a JSON file.
 */
async function exportSessions() {
  if (!props.resourceId) return

  isExporting.value = true
  try {
    const { blob, fileName } = await aiChatHistoryService.exportSessions(props.resourceType, props.resourceId)

    const url = window.URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    window.URL.revokeObjectURL(url)

    toast.add({
      severity: 'success',
      summary: 'Export Ready',
      detail: 'Chat sessions exported successfully',
      life: 3000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Export Failed',
      detail: error instanceof Error ? error.message : 'Failed to export chat sessions',
      life: 5000,
    })
  } finally {
    isExporting.value = false
  }
}

/**
 * Export a single session with all its messages to JSON.
 */
async function exportSession(session: ChatSession) {
  exportingSessionId.value = session.id
  try {
    const full = await aiChatHistoryService.getSessionWithMessages(session.id)

    const payload = {
      resourceType: props.resourceType,
      resourceId: props.resourceId,
      exportedAtUtc: new Date().toISOString(),
      sessionCount: 1,
      sessions: [
        {
          session: full.session,
          messages: full.messages,
        },
      ],
    }

    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
    const url = window.URL.createObjectURL(blob)
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)
    const fileName = `ai-chat-session-${session.id}-${timestamp}.json`

    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    window.URL.revokeObjectURL(url)

    toast.add({
      severity: 'success',
      summary: 'Export Ready',
      detail: `Session "${session.title}" exported successfully`,
      life: 3000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Export Failed',
      detail: error instanceof Error ? error.message : 'Failed to export this session',
      life: 5000,
    })
  } finally {
    exportingSessionId.value = null
  }
}

/**
 * Create a new session
 */
async function createNewSession() {
  const title = newSessionTitle.value.trim() || `Chat ${new Date().toLocaleString()}`

  isCreatingSession.value = true
  try {
    const newSession = await aiChatHistoryService.createSession({
      resourceType: props.resourceType,
      resourceId: props.resourceId,
      title,
    })

    sessions.value.push(newSession)
    newSessionTitle.value = ''
    showNewSessionDialog.value = false

    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'New chat session created',
      life: 3000,
    })

    // Emit event so parent can switch to the new session
    emit('sessionSelected', newSession)
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to create session',
      life: 5000,
    })
  } finally {
    isCreatingSession.value = false
  }
}

/**
 * Activate a session
 */
async function activateSession(session: ChatSession) {
  if (session.isActive) {
    // Already active, just select it
    emit('sessionSelected', session)
    return
  }

  try {
    await aiChatHistoryService.activateSession(session.id)

    // Update local state
    sessions.value.forEach((s) => {
      s.isActive = s.id === session.id
    })

    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Session activated',
      life: 3000,
    })

    emit('sessionActivated', session)
    emit('sessionSelected', session)
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to activate session',
      life: 5000,
    })
  }
}

/**
 * Open rename dialog
 */
function openRenameDialog(session: ChatSession) {
  renameSessionId.value = session.id
  renameSessionTitle.value = session.title
  showRenameDialog.value = true
}

/**
 * Rename session
 */
async function renameSession() {
  if (!renameSessionId.value) return

  const newTitle = renameSessionTitle.value.trim()
  if (!newTitle) {
    toast.add({
      severity: 'warn',
      summary: 'Warning',
      detail: 'Title cannot be empty',
      life: 3000,
    })
    return
  }

  isRenamingSession.value = true
  try {
    await aiChatHistoryService.updateSessionTitle(renameSessionId.value, newTitle)

    // Update local state
    const session = sessions.value.find((s) => s.id === renameSessionId.value)
    if (session) {
      session.title = newTitle
    }

    showRenameDialog.value = false
    renameSessionId.value = null
    renameSessionTitle.value = ''

    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Session renamed',
      life: 3000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to rename session',
      life: 5000,
    })
  } finally {
    isRenamingSession.value = false
  }
}

/**
 * Delete session with confirmation
 */
function confirmDeleteSession(sessionId: string) {
  const session = sessions.value.find((s) => s.id === sessionId)
  if (!session) return

  confirm.require({
    message: `Delete chat session "${session.title}"? This will remove all messages.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    acceptClass: 'p-button-danger',
    accept: async () => {
      await deleteSession(sessionId)
    },
  })
}

/**
 * Delete session
 */
async function deleteSession(sessionId: string) {
  try {
    await aiChatHistoryService.deleteSession(sessionId)

    // Remove from local state
    sessions.value = sessions.value.filter((s) => s.id !== sessionId)

    toast.add({
      severity: 'success',
      summary: 'Success',
      detail: 'Session deleted',
      life: 3000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to delete session',
      life: 5000,
    })
  }
}

/**
 * Format date for display
 */
function formatDate(dateStr: string): string {
  const date = new Date(dateStr)
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

onMounted(() => {
  loadSessions()
})

// Expose refresh method
defineExpose({
  refresh: loadSessions,
})
</script>

<template>
  <div class="chat-session-history-panel">
    <!-- Header -->
    <div class="panel-header">
      <div class="header-title">
        <i class="pi pi-comments text-lg"></i>
        <h3>{{ title }}</h3>
      </div>
      <div class="header-actions">
        <Button
          v-tooltip.left="'Export sessions as JSON'"
          icon="pi pi-download"
          size="small"
          rounded
          text
          :loading="isExporting"
          @click="exportSessions"
        />
        <Button
          v-tooltip.left="'New chat session'"
          icon="pi pi-plus"
          size="small"
          rounded
          text
          @click="showNewSessionDialog = true"
        />
      </div>
    </div>

    <Divider />

    <!-- Search -->
    <div v-if="sessions.length > 0" class="search-container">
      <div class="search-field">
        <i class="pi pi-search search-icon"></i>
        <InputText
          v-model="searchQuery"
          placeholder="Search sessions..."
          class="w-full"
          size="small"
        />
      </div>
    </div>

    <!-- Loading state -->
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner :style="{ width: '40px', height: '40px' }" stroke-width="4" />
      <p class="mt-2 text-sm text-color-secondary">Loading sessions...</p>
    </div>

    <!-- Empty state -->
    <div v-else-if="sessions.length === 0" class="empty-state">
      <i class="pi pi-comments text-3xl text-color-secondary mb-2"></i>
      <p>No chat sessions yet</p>
      <p class="text-sm text-color-secondary mb-3">
        Create a new session to start chatting with AI
      </p>
      <Button
        label="New Session"
        icon="pi pi-plus"
        size="small"
        @click="showNewSessionDialog = true"
      />
    </div>

    <!-- Sessions list -->
    <ScrollPanel v-else class="sessions-scroll">
      <div class="sessions-list">
        <Card
          v-for="session in filteredSessions"
          :key="session.id"
          class="session-card"
          :class="{ active: session.isActive }"
          @click="activateSession(session)"
        >
          <template #header>
            <div class="card-header">
              <div class="session-info">
                <span class="session-title">{{ session.title }}</span>
                <Tag
                  v-if="session.isActive"
                  value="Active"
                  severity="success"
                  size="small"
                  icon="pi pi-check-circle"
                />
              </div>
            </div>
          </template>

          <template #content>
            <div class="session-meta">
              <div class="meta-item">
                <i class="pi pi-calendar text-xs"></i>
                <span class="text-xs">{{ formatDate(session.createdAt) }}</span>
              </div>
              <div v-if="session.provider" class="meta-item">
                <i class="pi pi-bolt text-xs"></i>
                <span class="text-xs">{{ session.provider }}</span>
              </div>
            </div>
          </template>

          <template #footer>
            <div class="card-actions" @click.stop>
              <Button
                v-tooltip.top="'Export this session'"
                icon="pi pi-download"
                size="small"
                text
                :loading="exportingSessionId === session.id"
                @click="exportSession(session)"
              />
              <Button
                v-tooltip.top="'Rename'"
                icon="pi pi-pencil"
                size="small"
                text
                @click="openRenameDialog(session)"
              />
              <Button
                v-tooltip.top="'Delete'"
                icon="pi pi-trash"
                size="small"
                text
                severity="danger"
                :disabled="session.isActive"
                @click="confirmDeleteSession(session.id)"
              />
            </div>
          </template>
        </Card>

        <!-- No results -->
        <div v-if="filteredSessions.length === 0" class="no-results">
          <i class="pi pi-search text-2xl text-color-secondary mb-2"></i>
          <p class="text-color-secondary">No sessions match your search</p>
        </div>
      </div>
    </ScrollPanel>

    <!-- New Session Dialog -->
    <Dialog
      v-model:visible="showNewSessionDialog"
      modal
      header="New Chat Session"
      :style="{ width: '400px' }"
    >
      <div class="dialog-content">
        <label for="session-title" class="block text-sm font-medium mb-2">Session Title</label>
        <InputText
          id="session-title"
          v-model="newSessionTitle"
          placeholder="Enter session title..."
          class="w-full"
          autofocus
          @keypress.enter="createNewSession"
        />
      </div>

      <template #footer>
        <Button
          label="Cancel"
          icon="pi pi-times"
          text
          @click="showNewSessionDialog = false"
        />
        <Button
          label="Create"
          icon="pi pi-check"
          :loading="isCreatingSession"
          @click="createNewSession"
        />
      </template>
    </Dialog>

    <!-- Rename Session Dialog -->
    <Dialog
      v-model:visible="showRenameDialog"
      modal
      header="Rename Session"
      :style="{ width: '400px' }"
    >
      <div class="dialog-content">
        <label for="rename-title" class="block text-sm font-medium mb-2">New Title</label>
        <InputText
          id="rename-title"
          v-model="renameSessionTitle"
          placeholder="Enter new title..."
          class="w-full"
          autofocus
          @keypress.enter="renameSession"
        />
      </div>

      <template #footer>
        <Button label="Cancel" icon="pi pi-times" text @click="showRenameDialog = false" />
        <Button
          label="Rename"
          icon="pi pi-check"
          :loading="isRenamingSession"
          @click="renameSession"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.chat-session-history-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  flex-shrink: 0;
}

.header-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.header-title h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
}

.search-container {
  padding: 0 1rem 0.75rem 1rem;
  flex-shrink: 0;
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem 1rem;
  text-align: center;
  color: var(--p-text-muted-color);
}

.sessions-scroll {
  flex: 1;
  overflow: auto;
}

.sessions-list {
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.session-card {
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid var(--p-surface-200);
}

.session-card:hover {
  border-color: var(--p-primary-300);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.session-card.active {
  border-color: var(--p-primary-500);
  background: var(--p-primary-50);
}

.card-header {
  padding: 0.5rem 0;
}

.session-info {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
}

.session-title {
  font-weight: 600;
  font-size: 0.9rem;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.session-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  padding: 0.5rem 0;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  color: var(--p-text-muted-color);
}

.card-actions {
  display: flex;
  gap: 0.25rem;
  justify-content: flex-end;
  padding: 0.5rem 0 0 0;
  border-top: 1px solid var(--p-surface-200);
}

.no-results {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 2rem 1rem;
  text-align: center;
}

.dialog-content {
  padding: 1rem 0;
}
</style>
