<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  toolResult?: unknown
}>()

interface UiConsoleLogEntry {
  timestamp?: string | Date
  level?: string
  source?: string
  message?: string
}

interface UiConsoleLogSummary {
  totalLogs?: number
  returnedLogs?: number
  droppedLogs?: number
  levelCounts?: {
    debug?: number
    info?: number
    warn?: number
    error?: number
  }
  hasBlockingIssues?: boolean
  blockingIssueCount?: number
  blockingIssues?: UiConsoleLogEntry[]
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

const consoleLogs = computed<UiConsoleLogEntry[]>(() => {
  const parsed = parseJsonMaybe(props.toolResult)
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return []
  }

  const logs = (parsed as Record<string, unknown>).logs
  return Array.isArray(logs) ? (logs as UiConsoleLogEntry[]) : []
})

const consoleSummary = computed<UiConsoleLogSummary | null>(() => {
  const parsed = parseJsonMaybe(props.toolResult)
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return null
  }

  const summary = (parsed as Record<string, unknown>).logSummary
  if (!summary || typeof summary !== 'object' || Array.isArray(summary)) {
    return null
  }

  return summary as UiConsoleLogSummary
})

const visibleConsoleLogs = computed(() => consoleLogs.value.slice(0, 50))
const hiddenConsoleLogCount = computed(() => Math.max(0, consoleLogs.value.length - visibleConsoleLogs.value.length))
</script>

<template>
  <div v-if="consoleLogs.length > 0" class="tool-console-logs">
    <div class="tool-console-logs-title">
      Console logs captured: {{ consoleLogs.length }}
    </div>
    <div v-if="consoleSummary" class="tool-console-summary" :class="{ 'has-blockers': consoleSummary.hasBlockingIssues }">
      <span>Total: {{ consoleSummary.totalLogs ?? consoleLogs.length }}</span>
      <span>Returned: {{ consoleSummary.returnedLogs ?? consoleLogs.length }}</span>
      <span>Dropped: {{ consoleSummary.droppedLogs ?? 0 }}</span>
      <span>Errors: {{ consoleSummary.levelCounts?.error ?? 0 }}</span>
      <span>Warnings: {{ consoleSummary.levelCounts?.warn ?? 0 }}</span>
      <span v-if="consoleSummary.hasBlockingIssues">Blocking: {{ consoleSummary.blockingIssueCount ?? 0 }}</span>
    </div>
    <div
      v-if="consoleSummary?.hasBlockingIssues && (consoleSummary.blockingIssues?.length ?? 0) > 0"
      class="tool-console-blockers"
    >
      <div class="tool-console-blockers-title">Blocking errors</div>
      <div
        v-for="(issue, idx) in (consoleSummary.blockingIssues ?? []).slice(0, 5)"
        :key="`issue-${idx}-${issue.timestamp}-${issue.message}`"
        class="tool-console-blocker-item"
      >
        <span class="tool-console-log-time">{{ issue.timestamp ? new Date(issue.timestamp).toLocaleTimeString() : '-' }}</span>
        <span class="tool-console-log-source">{{ issue.source || 'preview' }}</span>
        <span class="tool-console-log-message">{{ issue.message || '' }}</span>
      </div>
    </div>
    <div class="tool-console-logs-list">
      <div v-for="(log, idx) in visibleConsoleLogs" :key="`${idx}-${log.timestamp}-${log.message}`" class="tool-console-log-item">
        <span class="tool-console-log-time">{{ log.timestamp ? new Date(log.timestamp).toLocaleTimeString() : '-' }}</span>
        <span class="tool-console-log-level">{{ log.level || 'info' }}</span>
        <span class="tool-console-log-source">{{ log.source || 'preview' }}</span>
        <span class="tool-console-log-message">{{ log.message || '' }}</span>
      </div>
    </div>
    <div v-if="hiddenConsoleLogCount > 0" class="tool-console-log-more">
      +{{ hiddenConsoleLogCount }} more entries (open logs panel for full list)
    </div>
  </div>
</template>

<style scoped>
.tool-console-logs-title {
  font-size: 0.68rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  margin-bottom: 0.2rem;
}

.tool-console-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem 0.55rem;
  margin-bottom: 0.3rem;
  font-size: 0.63rem;
  color: var(--p-text-muted-color);
}

.tool-console-summary.has-blockers {
  color: var(--p-red-500);
}

.tool-console-blockers {
  border: 1px solid var(--p-red-300);
  border-radius: 4px;
  background: color-mix(in srgb, var(--p-red-100) 20%, transparent);
  margin-bottom: 0.3rem;
}

.tool-console-blockers-title {
  font-size: 0.64rem;
  font-weight: 700;
  color: var(--p-red-500);
  padding: 0.2rem 0.35rem;
  border-bottom: 1px solid var(--p-red-200);
}

.tool-console-blocker-item {
  display: grid;
  grid-template-columns: 4rem 6rem 1fr;
  gap: 0.35rem;
  padding: 0.22rem 0.35rem;
  font-size: 0.64rem;
  border-bottom: 1px solid var(--p-red-200);
}

.tool-console-blocker-item:last-child {
  border-bottom: 0;
}

.tool-console-logs-list {
  border: 1px solid var(--p-surface-300);
  border-radius: 4px;
  max-height: 12rem;
  overflow: auto;
}

.tool-console-log-item {
  display: grid;
  grid-template-columns: 4rem 3rem 6rem 1fr;
  gap: 0.35rem;
  padding: 0.22rem 0.35rem;
  font-size: 0.65rem;
  border-bottom: 1px solid var(--p-surface-200);
}

.tool-console-log-item:last-child {
  border-bottom: 0;
}

.tool-console-log-time,
.tool-console-log-level,
.tool-console-log-source {
  color: var(--p-text-muted-color);
  font-family: monospace;
}

.tool-console-log-message {
  color: var(--p-text-color);
  word-break: break-word;
}

.tool-console-log-more {
  margin-top: 0.2rem;
  font-size: 0.65rem;
  color: var(--p-text-muted-color);
}

:global(.app-dark) .tool-console-logs-list {
  border-color: var(--p-surface-600);
  background: var(--p-surface-900);
}

:global(.app-dark) .tool-console-log-item {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .tool-console-blockers {
  border-color: var(--p-red-700);
  background: color-mix(in srgb, var(--p-red-900) 45%, transparent);
}

:global(.app-dark) .tool-console-blockers-title,
:global(.app-dark) .tool-console-blocker-item {
  border-color: var(--p-red-800);
}
</style>
