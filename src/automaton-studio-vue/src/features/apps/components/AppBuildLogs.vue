<script setup lang="ts">
/**
 * AppBuildLogs.vue
 * AI build sessions browser for the current app.
 * Uses a master-detail layout: session list on the left, selected session detail on the right.
 */
import { computed, onMounted, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import ToolExecutionDetails from '@/shared/components/ToolExecutionDetails.vue'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { ChatMessage, ChatSession } from '@/shared/types/ai.types'

type VersionSummary = {
  overview: string
  changes: string[]
}

const props = defineProps<{
  appId: string
  liveActiveSession?: ChatSession | null
  liveMessages?: ChatMessage[]
  liveIsBusy?: boolean
}>()

const sessions = ref<ChatSession[]>([])
const sessionMessages = ref<Record<string, ChatMessage[]>>({})
const selectedSessionId = ref<string | null>(null)
const activeDetailTab = ref<'summary' | 'changes' | 'conversation' | 'logs'>('summary')
const isLoading = ref(false)
const loadingSessionId = ref<string | null>(null)
const isExportingAll = ref(false)
const exportingSessionId = ref<string | null>(null)

const toast = useToast()
const { showAiDebugDetails } = useUserSettings()

const displayedSessions = computed(() => {
  const sorted = [...sessions.value].sort(
    (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  )

  const liveSession = props.liveActiveSession
  if (!liveSession) {
    return sorted
  }

  const existingIndex = sorted.findIndex((session) => session.id === liveSession.id)
  if (existingIndex >= 0) {
    sorted[existingIndex] = {
      ...sorted[existingIndex],
      ...liveSession,
    }
    return sorted.sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
  }

  return [liveSession, ...sorted]
})

const selectedSession = computed(() =>
  displayedSessions.value.find((session) => session.id === selectedSessionId.value) ?? null,
)

const selectedMessages = computed(() => {
  if (!selectedSession.value) {
    return []
  }

  if (isLiveSession(selectedSession.value.id) && props.liveMessages?.length) {
    return props.liveMessages
  }

  return sessionMessages.value[selectedSession.value.id] ?? []
})

const conversationMessages = computed(() =>
  selectedMessages.value.filter((message) =>
    message.role === 'user' ||
    message.role === 'assistant' ||
    message.type === 'reasoning',
  ),
)

const logMessages = computed(() =>
  selectedMessages.value.filter((message) =>
    message.type === 'toolExecution' ||
    message.type === 'codeSuccess' ||
    message.type === 'codeError',
  ),
)

const extractedSummary = computed<{ text: string; summary: VersionSummary | null }>(() => {
  const assistantMessages = [...selectedMessages.value]
    .filter((message) => message.role === 'assistant' && message.content?.trim())
    .reverse()

  for (const message of assistantMessages) {
    const extracted = extractVersionSummary(message.content)
    if (extracted.summary || extracted.text.trim()) {
      return extracted
    }
  }

  return { text: '', summary: null }
})

const changeItems = computed(() => {
  if (extractedSummary.value.summary?.changes?.length) {
    return extractedSummary.value.summary.changes
  }

  const toolNames = selectedMessages.value
    .filter((message) => message.type === 'toolExecution' && message.toolName)
    .map((message) => String(message.toolName))

  return [...new Set(toolNames)]
})

const selectedSessionDuration = computed(() => {
  if (!selectedSession.value) {
    return 'Not available'
  }

  const startedAt = new Date(selectedSession.value.createdAt).getTime()
  const endedAt = new Date(selectedSession.value.updatedAt).getTime()
  const durationMs = Math.max(endedAt - startedAt, 0)

  return formatDuration(durationMs)
})

const selectedSessionFileCount = computed(() => changeItems.value.length)

const selectedSessionEstimatedCost = computed(() => {
  if (!selectedSession.value) {
    return 'Not available'
  }

  return 'Not available'
})

const selectedSessionResponses = computed(() => {
  if (!selectedMessages.value.length) {
    return selectedSession.value?.totalApiCalls ?? 0
  }

  return selectedMessages.value.filter((message) => message.role === 'assistant').length
})

const selectedSessionLoops = computed(() => {
  const reviewerMentions = selectedMessages.value.filter((message) =>
    message.role === 'assistant' &&
    typeof message.content === 'string' &&
    /reviewer/i.test(message.content),
  ).length

  return reviewerMentions > 0 ? reviewerMentions : null
})

function downloadBlob(blob: Blob, fileName: string) {
  const url = window.URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.URL.revokeObjectURL(url)
}

async function exportAllSessions() {
  if (!props.appId) return

  isExportingAll.value = true
  try {
    const { blob, fileName } = await aiChatHistoryService.exportSessions('2', props.appId)
    downloadBlob(blob, fileName)

    toast.add({
      severity: 'success',
      summary: 'Export Ready',
      detail: 'All AI build sessions exported',
      life: 3000,
    })
  } catch (err: unknown) {
    console.error('Failed to export all build sessions', err)
    toast.add({
      severity: 'error',
      summary: 'Export Failed',
      detail: err instanceof Error ? err.message : 'Failed to export AI build sessions',
      life: 5000,
    })
  } finally {
    isExportingAll.value = false
  }
}

async function exportSelectedSession() {
  if (!selectedSession.value) return

  exportingSessionId.value = selectedSession.value.id
  try {
    const full = await aiChatHistoryService.getSessionWithMessages(selectedSession.value.id)
    const payload = {
      resourceType: 'WebApp',
      resourceId: props.appId,
      exportedAtUtc: new Date().toISOString(),
      sessionCount: 1,
      sessions: [
        {
          session: full.session,
          messages: full.messages,
        },
      ],
    }

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)
    const fileName = `ai-build-session-${selectedSession.value.id}-${timestamp}.json`
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
    downloadBlob(blob, fileName)

    toast.add({
      severity: 'success',
      summary: 'Export Ready',
      detail: `Session "${selectedSession.value.title || 'Untitled session'}" exported`,
      life: 3000,
    })
  } catch (err: unknown) {
    console.error('Failed to export session', err)
    toast.add({
      severity: 'error',
      summary: 'Export Failed',
      detail: err instanceof Error ? err.message : 'Failed to export session',
      life: 5000,
    })
  } finally {
    exportingSessionId.value = null
  }
}

async function loadSessions() {
  if (!props.appId) return
  isLoading.value = true
  try {
    sessions.value = (await aiChatHistoryService.getSessions('2', props.appId)).items
    ensureSelectedSession()
  } catch (err) {
    console.error('Failed to load build sessions', err)
  } finally {
    isLoading.value = false
  }
}

function ensureSelectedSession() {
  const selectedId = selectedSessionId.value
  if (selectedId && displayedSessions.value.some((session) => session.id === selectedId)) {
    return
  }

  selectedSessionId.value = props.liveActiveSession?.id ?? displayedSessions.value[0]?.id ?? null
}

async function selectSession(sessionId: string) {
  selectedSessionId.value = sessionId
  activeDetailTab.value = 'summary'
  await loadSessionMessages(sessionId)
}

async function loadSessionMessages(sessionId: string) {
  if (!sessionId || sessionMessages.value[sessionId]) {
    return
  }

  loadingSessionId.value = sessionId
  try {
    sessionMessages.value[sessionId] = await aiChatHistoryService.getSessionMessages(sessionId)
  } catch (err) {
    console.error('Failed to load session messages', err)
  } finally {
    loadingSessionId.value = null
  }
}

function extractVersionSummary(content: string): { text: string; summary: VersionSummary | null } {
  const trimmed = content.trimEnd()
  if (!trimmed.endsWith('}')) return { text: content, summary: null }

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
    // Render the original content if the trailing block is not valid JSON.
  }

  return { text: content, summary: null }
}

function isLiveSession(sessionId: string) {
  return props.liveActiveSession?.id === sessionId
}

function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleString()
}

function formatSessionDate(dateStr: string) {
  return new Date(dateStr).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

function formatTokens(n?: number) {
  if (!n) return '0'
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return n.toString()
}

function formatPercent(value?: number) {
  if (!value) return '0%'
  return `${value.toFixed(1)}%`
}

function formatDuration(durationMs: number) {
  const totalSeconds = Math.floor(durationMs / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60

  if (minutes <= 0) {
    return `${Math.max(totalSeconds, 1)}s`
  }

  if (seconds === 0) {
    return `${minutes}m`
  }

  return `${minutes}m ${seconds}s`
}

function getTierSeverity(tier?: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (tier?.toLowerCase()) {
    case 'simple': return 'success'
    case 'medium': return 'info'
    case 'large': return 'warn'
    case 'enterprise': return 'danger'
    default: return 'secondary'
  }
}

function getStrategyLabel(strategy?: string): string {
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

function getExecutionPolicyLabel(policy?: string): string {
  switch (policy?.toLowerCase()) {
    case 'lean-tools':
      return 'Lean Tools'
    case 'default':
      return 'Default'
    default:
      return policy || ''
  }
}

function getMessageLabel(message: ChatMessage): string {
  if (message.type === 'reasoning') return 'Reasoning'
  if (message.role === 'user') return 'User'
  if (message.role === 'assistant') return 'AI'
  return 'Message'
}

function getMessageSeverity(message: ChatMessage): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  if (message.role === 'user') return 'secondary'
  if (message.type === 'reasoning') return 'warn'
  return 'info'
}

function getMessageIcon(message: ChatMessage) {
  if (message.type === 'reasoning') return 'pi pi-compass'
  return message.role === 'user' ? 'pi pi-user' : 'pi pi-sparkles'
}

watch(
  displayedSessions,
  async () => {
    ensureSelectedSession()
    if (selectedSessionId.value) {
      await loadSessionMessages(selectedSessionId.value)
    }
  },
  { immediate: true },
)

watch(
  () => props.appId,
  async () => {
    sessionMessages.value = {}
    selectedSessionId.value = null
    await loadSessions()
  },
)

onMounted(loadSessions)

defineExpose({ reload: loadSessions })
</script>

<template>
  <div class="app-build-logs">
    <div class="build-logs-header">
      <div>
        <div class="build-logs-title">
          <i class="pi pi-history"></i>
          <span>Build Sessions</span>
        </div>
        <p class="build-logs-subtitle">AI build history for this app.</p>
      </div>
      <div class="header-actions">
        <Button
          v-tooltip.bottom="'Export all sessions as JSON'"
          icon="pi pi-download"
          text
          rounded
          severity="secondary"
          size="small"
          :loading="isExportingAll"
          @click="exportAllSessions"
        />
        <Button
          v-tooltip.bottom="'Refresh'"
          icon="pi pi-refresh"
          text
          rounded
          severity="secondary"
          size="small"
          :loading="isLoading"
          @click="loadSessions"
        />
      </div>
    </div>

    <div v-if="isLoading" class="center-state">
      <ProgressSpinner style="width: 32px; height: 32px" />
    </div>

    <div v-else-if="displayedSessions.length === 0" class="empty-state">
      <i class="pi pi-comments text-4xl text-color-secondary"></i>
      <p>No AI build sessions yet.</p>
      <span class="text-color-secondary">Start chatting with the assistant to create a session.</span>
    </div>

    <div v-else class="build-sessions-layout">
      <aside class="sessions-pane">
        <button
          v-for="session in displayedSessions"
          :key="session.id"
          type="button"
          class="session-list-item"
          :class="{ active: selectedSessionId === session.id }"
          @click="selectSession(session.id)"
        >
          <div class="session-list-item__header">
            <span class="session-list-item__title">{{ session.title || 'Untitled session' }}</span>
            <Tag
              v-if="isLiveSession(session.id) && props.liveIsBusy"
              value="Live"
              severity="info"
              class="session-list-item__tag"
            />
          </div>
          <div class="session-list-item__meta">
            <span>{{ formatSessionDate(session.updatedAt) }}</span>
          </div>
          <div class="session-list-item__stats-row">
            <div class="session-list-item__metric">{{ formatTokens(session.totalTokens) }} tokens</div>
            <div class="session-list-item__stats">
              <span>{{ session.totalApiCalls }} responses</span>
            </div>
          </div>
          <div v-if="isLiveSession(session.id) && props.liveIsBusy" class="session-list-item__status">
            <span>In progress</span>
          </div>
        </button>
      </aside>

      <section v-if="selectedSession" class="session-detail">
        <div class="session-detail__header">
          <div class="session-detail__title-row">
            <h2>{{ selectedSession.title || 'Untitled session' }}</h2>
            <Tag
              v-if="selectedSession.complexityTier"
              :value="selectedSession.complexityTier"
              :severity="getTierSeverity(selectedSession.complexityTier)"
            />
            <Tag
              v-if="selectedSession.effectiveVerificationStrategy"
              :value="`Strategy: ${getStrategyLabel(selectedSession.effectiveVerificationStrategy)}`"
              severity="secondary"
            />
            <Tag
              v-if="selectedSession.effectiveExecutionPolicy"
              :value="`Policy: ${getExecutionPolicyLabel(selectedSession.effectiveExecutionPolicy)}`"
              severity="secondary"
            />
          </div>
          <div class="header-actions">
            <Button
              v-tooltip.bottom="'Export this session as JSON'"
              icon="pi pi-download"
              text
              rounded
              severity="secondary"
              size="small"
              :loading="exportingSessionId === selectedSession.id"
              @click="exportSelectedSession"
            />
          </div>
        </div>

        <div v-if="loadingSessionId === selectedSession.id && !selectedMessages.length" class="center-state">
          <ProgressSpinner style="width: 28px; height: 28px" />
        </div>

        <div v-else class="session-detail__body">
          <div class="detail-tabs" role="tablist" aria-label="Build session detail sections">
            <button
              type="button"
              class="detail-tab"
              :class="{ active: activeDetailTab === 'summary' }"
              @click="activeDetailTab = 'summary'"
            >
              Summary
            </button>
            <button
              type="button"
              class="detail-tab"
              :class="{ active: activeDetailTab === 'changes' }"
              @click="activeDetailTab = 'changes'"
            >
              Changes
            </button>
            <button
              type="button"
              class="detail-tab"
              :class="{ active: activeDetailTab === 'conversation' }"
              @click="activeDetailTab = 'conversation'"
            >
              Conversation
            </button>
            <button
              type="button"
              class="detail-tab"
              :class="{ active: activeDetailTab === 'logs' }"
              @click="activeDetailTab = 'logs'"
            >
              Logs
            </button>
          </div>

          <section v-show="activeDetailTab === 'summary'" class="detail-section">
            <div class="detail-section__heading">Summary</div>
            <div class="summary-grid">
              <div class="summary-card">
                <span class="summary-card__label">Started</span>
                <strong>{{ formatDate(selectedSession.createdAt) }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Updated</span>
                <strong>{{ formatDate(selectedSession.updatedAt) }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Provider / Model</span>
                <strong>{{ selectedSession.provider || 'Unknown' }} / {{ selectedSession.model || 'Unknown' }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Responses</span>
                <strong>{{ selectedSessionResponses }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Duration</span>
                <strong>{{ selectedSessionDuration }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Total Tokens</span>
                <strong>{{ formatTokens(selectedSession.totalTokens) }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Input / Output</span>
                <strong>{{ formatTokens(selectedSession.totalInputTokens) }} / {{ formatTokens(selectedSession.totalOutputTokens) }}</strong>
              </div>
              <div class="summary-card">
                <span class="summary-card__label">Prompts</span>
                <strong>{{ selectedSession.totalPromptsSubmitted }}</strong>
              </div>
              <div v-if="selectedSessionLoops !== null" class="summary-card">
                <span class="summary-card__label">Loops</span>
                <strong>{{ selectedSessionLoops }}</strong>
              </div>
            </div>

            <div class="summary-notes">
              <div v-if="extractedSummary.summary?.overview" class="summary-note">
                <span class="summary-note__label">Overview</span>
                <p>{{ extractedSummary.summary.overview }}</p>
              </div>
              <div v-else-if="extractedSummary.text" class="summary-note">
                <span class="summary-note__label">Latest AI Response</span>
                <p>{{ extractedSummary.text }}</p>
              </div>
              <div class="summary-note summary-note--muted">
                <span class="summary-note__label">Usage</span>
                <p>Token usage is available above. Estimated cost is hidden until it can be calculated from real pricing data.</p>
              </div>
            </div>
          </section>

          <section v-show="activeDetailTab === 'changes'" class="detail-section">
            <div class="detail-section__heading">Changes</div>
            <div v-if="changeItems.length" class="changes-list">
              <div v-for="change in changeItems" :key="change" class="change-item">
                <i class="pi pi-file-edit"></i>
                <span>{{ change }}</span>
              </div>
            </div>
            <div v-else class="empty-inline-state">
              No structured change summary was recorded for this session.
            </div>
          </section>

          <section v-show="activeDetailTab === 'conversation'" class="detail-section">
            <div class="detail-section__heading">Conversation</div>
            <div v-if="conversationMessages.length" class="conversation-list">
              <article
                v-for="message in conversationMessages"
                :key="message.id"
                class="conversation-item"
              >
                <div class="conversation-item__meta">
                  <Tag :value="getMessageLabel(message)" :severity="getMessageSeverity(message)" />
                  <span class="text-color-secondary">{{ formatDate(message.timestamp.toISOString()) }}</span>
                </div>
                <div class="conversation-item__body">
                  <i :class="getMessageIcon(message)"></i>
                  <pre>{{ message.content }}</pre>
                </div>
              </article>
            </div>
            <div v-else class="empty-inline-state">
              No conversation messages are available for this session.
            </div>
          </section>

          <section v-show="activeDetailTab === 'logs'" class="detail-section">
            <div class="detail-section__heading">Logs</div>
            <div v-if="logMessages.length" class="logs-list">
              <article
                v-for="message in logMessages"
                :key="message.id"
                class="log-item"
              >
                <div class="log-item__meta">
                  <Tag
                    :value="message.toolName || message.type || message.role"
                    :severity="message.type === 'codeError' ? 'danger' : 'secondary'"
                  />
                  <span class="text-color-secondary">{{ formatDate(message.timestamp.toISOString()) }}</span>
                </div>
                <div v-if="message.type === 'toolExecution'" class="tool-log">
                  <p class="tool-log__summary">
                    <strong>{{ message.toolName }}</strong>
                    <span v-if="message.toolStatus" class="text-color-secondary">{{ message.toolStatus }}</span>
                  </p>
                  <ToolExecutionDetails
                    :tool-name="message.toolName"
                    :tool-args="message.toolArgs"
                    :tool-result="message.toolResult"
                    :tool-status="message.toolStatus"
                    :error="message.error"
                    :show-debug-details="showAiDebugDetails"
                  />
                </div>
                <pre v-else class="log-item__body">{{ message.content }}</pre>
              </article>
            </div>
            <div v-else-if="isLiveSession(selectedSession.id) && props.liveIsBusy" class="empty-inline-state">
              Live generation in progress...
            </div>
            <div v-else class="empty-inline-state">
              No logs are available for this session.
            </div>
          </section>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.app-build-logs {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
  background: #f5f9ff;
}

.build-logs-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.1rem 1.35rem 0.9rem;
  border-bottom: 1px solid #d8e6f8;
  background: #f5f9ff;
}

.build-logs-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 1.05rem;
  font-weight: 700;
}

.build-logs-subtitle {
  margin: 0.35rem 0 0;
  color: #5a6f91;
  font-size: 0.92rem;
  line-height: 1.45;
  max-width: 24rem;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.center-state,
.empty-state {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  gap: 0.5rem;
  padding: 2rem;
}

.build-sessions-layout {
  display: grid;
  grid-template-columns: minmax(22rem, 22rem) minmax(0, 1fr);
  flex: 1;
  min-height: 0;
  gap: 1rem;
  padding: 0 1rem 1rem;
}

.sessions-pane {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0.65rem 0.65rem 0.85rem;
  border: 1px solid #d3e1f4;
  border-radius: 1.25rem;
  background: #ffffff;
  overflow-y: auto;
}

.session-list-item {
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
  width: 100%;
  padding: 0.95rem 0.95rem 0.85rem;
  border: 1px solid transparent;
  border-radius: 1rem;
  background: #ffffff;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s ease, background-color 0.15s ease;
}

.session-list-item:hover {
  border-color: #d7e5f8;
  background: #f9fbff;
}

.session-list-item.active {
  border-color: #a8cdfb;
  background: #eaf4ff;
}

.session-list-item__header,
.session-list-item__meta,
.session-list-item__stats,
.session-detail__header,
.session-detail__title-row,
.conversation-item__meta,
.log-item__meta,
.tool-log__summary {
  display: flex;
  align-items: center;
}

.session-list-item__header,
.session-detail__header,
.session-detail__title-row {
  justify-content: space-between;
  gap: 0.5rem;
}

.session-list-item__title {
  font-weight: 600;
  line-height: 1.35;
  font-size: 0.98rem;
  color: #102544;
}

.session-list-item__meta,
.conversation-item__meta,
.log-item__meta,
.tool-log__summary {
  gap: 0.75rem;
  flex-wrap: wrap;
  color: #5f7394;
  font-size: 0.82rem;
  line-height: 1.35;
}

.session-list-item__stats span {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
}

.session-list-item__stats-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.session-list-item__metric {
  font-size: 0.86rem;
  font-weight: 600;
  color: #102544;
}

.session-list-item__stats {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  color: #5f7394;
  font-size: 0.82rem;
  text-align: right;
}

.session-list-item__status {
  color: #5f7394;
  font-size: 0.8rem;
}

.session-detail {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  border: 1px solid #d3e1f4;
  border-radius: 1.25rem;
  background: #ffffff;
}

.session-detail__header {
  padding: 1.1rem 1.2rem 0.9rem;
  border-bottom: 1px solid #dbe7f8;
  background: #ffffff;
}

.session-detail__header h2 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: #102544;
}

.session-detail__body {
  display: flex;
  flex-direction: column;
  gap: 0;
  padding: 0;
  overflow-y: auto;
  background: #ffffff;
}

.detail-tabs {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0 1.2rem;
  border-bottom: 1px solid #dbe9fb;
  background: #ffffff;
}

.detail-tab {
  position: relative;
  padding: 0.9rem 0 0.9rem;
  border: none;
  background: transparent;
  color: #4d6284;
  font-size: 0.92rem;
  font-weight: 600;
  cursor: pointer;
  transition: color 0.15s ease;
}

.detail-tab::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: -1px;
  height: 2px;
  border-radius: 999px;
  background: transparent;
  transition: background-color 0.15s ease;
}

.detail-tab:hover {
  color: #102544;
}

.detail-tab.active {
  color: #00a86b;
}

.detail-tab.active::after {
  background: #00b773;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin: 0;
  padding: 1.1rem 1.2rem 1rem;
  border: none;
  border-radius: 0;
  background: transparent;
  box-shadow: none;
}

.detail-section__heading,
.summary-note__label {
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #607391;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.75rem;
}

.summary-card {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding: 0.85rem;
  border-radius: 0.85rem;
  background: #f9fbff;
  border: 1px solid #e3edf9;
}

.summary-card__label {
  color: #5f7394;
  font-size: 0.76rem;
}

.summary-notes {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.summary-grid + .summary-notes,
.detail-section__heading + .changes-list,
.detail-section__heading + .conversation-list,
.detail-section__heading + .logs-list,
.detail-section__heading + .empty-inline-state {
  padding-top: 0.25rem;
}

.summary-note {
  padding: 0.9rem;
  border-radius: 0.85rem;
  background: #fbfdff;
  border: 1px solid #e3edf9;
}

.summary-note--muted {
  color: #5f7394;
}

.summary-note p {
  margin: 0.4rem 0 0;
  white-space: pre-wrap;
  line-height: 1.5;
}

.changes-list,
.conversation-list,
.logs-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.change-item,
.conversation-item,
.log-item {
  padding: 0.9rem;
  border: 1px solid #dce9fb;
  border-radius: 0.85rem;
  background: #ffffff;
}

.change-item {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

.conversation-item__body {
  display: grid;
  grid-template-columns: 1rem minmax(0, 1fr);
  gap: 0.75rem;
  margin-top: 0.65rem;
}

.conversation-item__body pre,
.log-item__body {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  line-height: 1.5;
}

.tool-log {
  margin-top: 0.65rem;
}

.empty-inline-state {
  color: #5f7394;
  font-size: 0.9rem;
}

.detail-section + .detail-section {
  border-top: 1px solid #dbe7f8;
}

:global(.app-dark) .build-logs-header,
:global(.app-dark) .sessions-pane,
:global(.app-dark) .session-detail__header,
:global(.app-dark) .detail-tabs,
:global(.app-dark) .detail-section,
:global(.app-dark) .session-list-item,
:global(.app-dark) .change-item,
:global(.app-dark) .conversation-item,
:global(.app-dark) .log-item {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .session-list-item,
:global(.app-dark) .detail-section,
:global(.app-dark) .change-item,
:global(.app-dark) .conversation-item,
:global(.app-dark) .log-item {
  background: var(--p-surface-900);
}

:global(.app-dark) .session-list-item.active {
  background: color-mix(in srgb, var(--p-primary-900) 58%, var(--p-surface-900));
}

:global(.app-dark) .summary-card,
:global(.app-dark) .summary-note {
  background: var(--p-surface-800);
}

:global(.app-dark) .detail-tab.active {
  color: var(--p-primary-300);
}

@media (max-width: 960px) {
  .build-sessions-layout {
    grid-template-columns: 1fr;
  }

  .sessions-pane {
    min-height: 16rem;
  }

  .session-detail__header,
  .build-logs-header {
    align-items: flex-start;
    flex-direction: column;
  }
}

</style>
