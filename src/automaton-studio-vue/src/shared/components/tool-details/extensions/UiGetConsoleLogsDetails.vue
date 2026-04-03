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

const visibleConsoleLogs = computed(() => consoleLogs.value.slice(0, 50))
const hiddenConsoleLogCount = computed(() => Math.max(0, consoleLogs.value.length - visibleConsoleLogs.value.length))
</script>

<template>
  <div v-if="consoleLogs.length > 0" class="tool-console-logs">
    <div class="tool-console-logs-title">
      Console logs captured: {{ consoleLogs.length }}
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
</style>
