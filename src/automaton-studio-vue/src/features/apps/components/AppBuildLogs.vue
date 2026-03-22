<script setup lang="ts">
/**
 * AppBuildLogs.vue
 * Shows AI build session timeline for the current app.
 * Displays each session as a collapsible card with a tool-call timeline inside.
 */
import { ref, onMounted, watch } from 'vue'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { ChatSession, ChatMessage } from '@/shared/types/ai.types'

const props = defineProps<{
  appId: string
}>()

const sessions = ref<ChatSession[]>([])
const expandedSessionId = ref<string | null>(null)
const sessionMessages = ref<Record<string, ChatMessage[]>>({})
const isLoading = ref(false)
const loadingSessionId = ref<string | null>(null)

async function loadSessions() {
  if (!props.appId) return
  isLoading.value = true
  try {
    sessions.value = (await aiChatHistoryService.getSessions('2', props.appId)).items
  } catch (err) {
    console.error('Failed to load build sessions', err)
  } finally {
    isLoading.value = false
  }
}

async function toggleSession(sessionId: string) {
  if (expandedSessionId.value === sessionId) {
    expandedSessionId.value = null
    return
  }
  expandedSessionId.value = sessionId
  expandedMessages.value = new Set()

  if (!sessionMessages.value[sessionId]) {
    loadingSessionId.value = sessionId
    try {
      sessionMessages.value[sessionId] = await aiChatHistoryService.getSessionMessages(sessionId)
    } catch (err) {
      console.error('Failed to load session messages', err)
    } finally {
      loadingSessionId.value = null
    }
  }
}

function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleString()
}

function formatDuration(ms: number) {
  if (ms >= 1000) return `${(ms / 1000).toFixed(1)}s`
  return `${ms}ms`
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

function getMessageTypeLabel(msg: ChatMessage): string {
  // For tool calls: show generic "Tool Call" — the tool name is rendered separately below
  if (msg.type === 'toolExecution') return 'Tool Call'
  if (msg.type === 'reasoning') return 'Thinking'
  if (msg.type === 'codeSuccess') return 'Tool Result'
  if (msg.type === 'codeError') return 'Tool Error'
  if (msg.role === 'user') return 'User Prompt'
  if (msg.role === 'assistant') return 'AI Response'
  return 'Message'
}

function getMessageIcon(msg: ChatMessage): string {
  if (msg.type === 'toolExecution') return 'pi pi-wrench'
  if (msg.type === 'reasoning') return 'pi pi-brain'
  if (msg.type === 'codeSuccess') return 'pi pi-check-circle'
  if (msg.type === 'codeError') return 'pi pi-times-circle'
  if (msg.role === 'user') return 'pi pi-user'
  return 'pi pi-sparkles'
}

function getMessageSeverity(msg: ChatMessage): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  if (msg.type === 'codeError') return 'danger'
  if (msg.type === 'codeSuccess') return 'success'
  if (msg.type === 'toolExecution') return 'info'
  if (msg.type === 'reasoning') return 'secondary'
  if (msg.role === 'user') return 'secondary'
  return 'secondary'
}

const expandedMessages = ref<Set<number>>(new Set())

function toggleMessageExpand(idx: number) {
  if (expandedMessages.value.has(idx)) {
    expandedMessages.value.delete(idx)
  } else {
    expandedMessages.value.add(idx)
  }
}

// ── API call grouping ─────────────────────────────────────────────────────────

interface AnnotatedMessage {
  msg: ChatMessage
  idx: number
  /** 1-based LLM API call number this message belongs to (undefined for user messages) */
  apiCallNumber?: number
  /** True when this message is the first in a new LLM API call — renders the divider */
  isNewApiCall: boolean
}

/**
 * Annotates messages with which LLM iteration they belong to.
 *
 * Rule: a new API call starts when a toolExecution or final assistant text message
 * appears after either the user message (call #1) or a batch of tool results (calls #2+).
 */
function getAnnotatedMessages(messages: ChatMessage[]): AnnotatedMessage[] {
  const filtered = messages.filter(m =>
    m.role === 'user' ||
    m.role === 'assistant' ||
    m.type === 'toolExecution' ||
    m.type === 'codeSuccess' ||
    m.type === 'codeError' ||
    m.type === 'reasoning'
  )

  const result: AnnotatedMessage[] = []
  let apiCallNum = 0
  let awaitingNextCall = true // true after user message or tool results batch

  for (let i = 0; i < filtered.length; i++) {
    const msg = filtered[i]

    if (msg.role === 'user') {
      result.push({ msg, idx: i, isNewApiCall: false })
      awaitingNextCall = true
      continue
    }

    const isToolCall = msg.type === 'toolExecution'
    const isToolResult = msg.type === 'codeSuccess' || msg.type === 'codeError'
    const isAssistantText = msg.role === 'assistant' && (msg.type === 'text' || !msg.type)

    let isNewApiCall = false

    if (awaitingNextCall && (isToolCall || isAssistantText)) {
      apiCallNum++
      isNewApiCall = true
      awaitingNextCall = false
    }

    // After the last tool result in a batch, the next call boundary is ready
    if (isToolResult) {
      // Peek: if next non-result message is a tool call or assistant text, mark boundary
      const next = filtered[i + 1]
      if (!next || next.type === 'toolExecution' || (next.role === 'assistant' && (next.type === 'text' || !next.type))) {
        awaitingNextCall = true
      }
    }

    result.push({ msg, idx: i, apiCallNumber: apiCallNum, isNewApiCall })
  }

  return result
}

function formatTokens(n?: number) {
  if (!n) return '0'
  if (n >= 1000) return `${(n / 1000).toFixed(1)}K`
  return n.toString()
}

onMounted(loadSessions)
watch(() => props.appId, loadSessions)

defineExpose({ reload: loadSessions })
</script>

<template>
  <div class="app-build-logs">
    <div class="build-logs-header">
      <span class="build-logs-title">
        <i class="pi pi-history mr-2"></i>AI Build Sessions
      </span>
      <Button
        icon="pi pi-refresh"
        text
        rounded
        severity="secondary"
        size="small"
        :loading="isLoading"
        v-tooltip.bottom="'Refresh'"
        @click="loadSessions"
      />
    </div>

    <div v-if="isLoading" class="flex justify-content-center align-items-center py-5">
      <ProgressSpinner style="width: 32px; height: 32px" />
    </div>

    <div v-else-if="sessions.length === 0" class="empty-state">
      <i class="pi pi-comments text-4xl text-color-secondary mb-2"></i>
      <p class="text-color-secondary">No AI build sessions yet.</p>
    </div>

    <div v-else class="sessions-list">
      <div
        v-for="session in sessions"
        :key="session.id"
        class="session-card"
        :class="{ expanded: expandedSessionId === session.id }"
      >
        <!-- Session header (clickable) -->
        <div class="session-header" @click="toggleSession(session.id)">
          <div class="session-meta">
            <i
              class="pi"
              :class="expandedSessionId === session.id ? 'pi-chevron-down' : 'pi-chevron-right'"
              style="font-size: 0.75rem"
            ></i>
            <span class="session-title">{{ session.title || 'Untitled session' }}</span>
            <Tag
              v-if="session.complexityTier"
              :value="session.complexityTier"
              :severity="getTierSeverity(session.complexityTier)"
              class="session-tier-tag"
            />
          </div>
          <div class="session-stats">
            <span class="stat">
              <i class="pi pi-comments"></i> {{ session.totalPromptsSubmitted }}
            </span>
            <span class="stat" title="LLM API calls this session">
              <i class="pi pi-sync"></i> {{ session.totalApiCalls }}
            </span>
            <span class="stat">
              <i class="pi pi-bolt"></i> {{ formatTokens(session.totalTokens) }}
            </span>
            <span class="stat text-color-secondary">
              {{ formatDate(session.createdAt) }}
            </span>
          </div>
        </div>

        <!-- Session messages timeline -->
        <div v-if="expandedSessionId === session.id" class="session-body">
          <div v-if="loadingSessionId === session.id" class="flex justify-content-center py-3">
            <ProgressSpinner style="width: 24px; height: 24px" />
          </div>

          <div v-else-if="sessionMessages[session.id]" class="timeline">
            <template
              v-for="item in getAnnotatedMessages(sessionMessages[session.id])"
              :key="item.idx"
            >
              <!-- Message row -->
              <div
                class="timeline-item"
                :class="`timeline-${item.msg.type ?? item.msg.role}`"
              >
                <div class="timeline-icon">
                  <i :class="getMessageIcon(item.msg)"></i>
                </div>
                <div class="timeline-content">
                  <div class="timeline-label">
                    <Tag
                      :value="getMessageTypeLabel(item.msg)"
                      :severity="getMessageSeverity(item.msg)"
                      style="font-size: 0.7rem"
                    />
                    <span v-if="item.apiCallNumber" class="api-call-badge">
                      #{{ item.apiCallNumber }}
                    </span>
                    <span v-if="item.msg.totalTokens" class="token-count">
                      {{ formatTokens(item.msg.totalTokens) }} tokens
                    </span>
                  </div>
                  <div v-if="item.msg.type === 'toolExecution'" class="tool-detail">
                    <span class="tool-name">{{ item.msg.toolName }}</span>
                    <span v-if="item.msg.toolArgs" class="tool-args">
                      {{ JSON.stringify(item.msg.toolArgs).slice(0, 120) }}{{ JSON.stringify(item.msg.toolArgs).length > 120 ? '…' : '' }}
                    </span>
                  </div>
                  <div v-else-if="item.msg.content" class="message-excerpt">
                    <template v-if="expandedMessages.has(item.idx)">
                      {{ item.msg.content }}
                    </template>
                    <template v-else>
                      {{ item.msg.content.slice(0, 300) }}{{ item.msg.content.length > 300 ? '…' : '' }}
                    </template>
                    <button
                      v-if="item.msg.content.length > 300"
                      class="show-more-btn"
                      @click.stop="toggleMessageExpand(item.idx)"
                    >
                      {{ expandedMessages.has(item.idx) ? 'Show less' : 'Show more' }}
                    </button>
                  </div>
                </div>
              </div>
            </template>

            <div v-if="getAnnotatedMessages(sessionMessages[session.id]).length === 0" class="text-color-secondary text-sm py-2">
              No messages found in this session.
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.app-build-logs {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.build-logs-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.build-logs-title {
  font-weight: 600;
  font-size: 0.9rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  padding: 2rem;
}

.sessions-list {
  flex: 1;
  overflow-y: auto;
  padding: 0.5rem;
}

.session-card {
  border: 1px solid var(--p-surface-200);
  border-radius: 6px;
  margin-bottom: 0.5rem;
  overflow: hidden;
}

.session-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 0.75rem;
  cursor: pointer;
  gap: 0.5rem;
}

.session-header:hover {
  background-color: var(--p-surface-100);
}

.session-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.session-title {
  font-size: 0.85rem;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 200px;
}

.session-tier-tag {
  font-size: 0.65rem;
  flex-shrink: 0;
}

.session-stats {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-shrink: 0;
}

.stat {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.session-body {
  border-top: 1px solid var(--p-surface-200);
  padding: 0.5rem 0.75rem;
  background-color: var(--p-surface-50);
}

.timeline {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

/* ── API call badge (inline, right of type tag) ───────────────────────────── */
.api-call-badge {
  font-size: 0.6rem;
  font-weight: 700;
  color: var(--p-primary-color);
  background-color: color-mix(in srgb, var(--p-primary-color) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--p-primary-color) 30%, transparent);
  border-radius: 4px;
  padding: 0.05rem 0.35rem;
  letter-spacing: 0.03em;
}

/* ── Timeline items ───────────────────────────────────────────────────────── */
.timeline-item {
  display: flex;
  gap: 0.5rem;
  align-items: flex-start;
}

.timeline-icon {
  width: 20px;
  flex-shrink: 0;
  font-size: 0.75rem;
  margin-top: 2px;
  color: var(--p-text-muted-color);
}

.timeline-content {
  flex: 1;
  min-width: 0;
}

.timeline-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.15rem;
}

.token-count {
  font-size: 0.65rem;
  color: var(--p-text-muted-color);
}

.tool-detail {
  font-size: 0.75rem;
}

.tool-name {
  font-weight: 600;
  font-family: monospace;
  color: var(--p-primary-color);
}

.tool-args {
  display: block;
  font-family: monospace;
  color: var(--p-text-muted-color);
  word-break: break-all;
}

.message-excerpt {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  white-space: pre-wrap;
  word-break: break-word;
}

.show-more-btn {
  display: inline-block;
  margin-top: 0.25rem;
  background: none;
  border: none;
  padding: 0;
  font-size: 0.7rem;
  color: var(--p-primary-color);
  cursor: pointer;
  text-decoration: underline;
}
</style>
