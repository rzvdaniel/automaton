<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import SelectButton from 'primevue/selectbutton'
import { UiLogFilter, useAppEditorStore } from '../stores/app-editor.store'

const editorStore = useAppEditorStore()

const filterOptions = [
  { label: 'All', value: UiLogFilter.All },
  { label: 'Errors', value: UiLogFilter.Errors },
  { label: 'Console', value: UiLogFilter.Console },
  { label: 'Runtime', value: UiLogFilter.Runtime },
]

const entries = computed(() => {
  const all = editorStore.uiLogEntries
  switch (editorStore.uiLogFilter) {
    case UiLogFilter.Errors:
      return all.filter((entry) => entry.level === 'error')
    case UiLogFilter.Console:
      return all.filter((entry) => entry.source === 'preview-console')
    case UiLogFilter.Runtime:
      return all.filter((entry) => entry.source !== 'preview-console')
    default:
      return all
  }
})
const compactMode = ref(true)
const compactEntries = computed(() => entries.value.slice(0, 250))

function formatTime(date: Date): string {
  return new Date(date).toLocaleTimeString('en-US', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function sourceLabel(source: string): string {
  if (source === 'preview-console') return 'Console'
  if (source === 'preview-network') return 'Network'
  return 'Runtime'
}

function sourceSeverity(source: string): 'secondary' | 'info' | 'warn' {
  if (source === 'preview-network') return 'warn'
  if (source === 'preview-runtime') return 'info'
  return 'secondary'
}

function levelClass(level: string): string {
  if (level === 'debug') return 'text-blue-400'
  if (level === 'error') return 'text-red-500'
  if (level === 'warn') return 'text-orange-500'
  return 'text-color-secondary'
}

function formatIso(date: Date): string {
  return new Date(date).toISOString()
}

function downloadLogsAsJson(): void {
  const payload = {
    exportedAt: new Date().toISOString(),
    totalEntries: entries.value.length,
    filter: editorStore.uiLogFilter,
    logs: entries.value.map((entry) => ({
      id: entry.id,
      timestamp: formatIso(entry.timestamp),
      level: entry.level,
      source: entry.source,
      message: entry.message,
    })),
  }

  const blob = new Blob([JSON.stringify(payload, null, 2)], {
    type: 'application/json',
  })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `live-ui-logs-${new Date().toISOString().replace(/[:.]/g, '-')}.json`
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(url), 10000)
}
</script>

<template>
  <div class="live-logs">
    <div class="live-logs-header">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-wave-pulse text-color-secondary"></i>
        <span class="font-semibold">Live UI Logs</span>
        <Tag :value="editorStore.uiLogEntries.length.toString()" severity="secondary" rounded />
      </div>
      <div class="flex align-items-center gap-2">
        <Button
          icon="pi pi-compress"
          text
          severity="secondary"
          size="small"
          :label="compactMode ? 'Compact' : 'Expanded'"
          @click="compactMode = !compactMode"
        />
        <Button
          v-if="entries.length > 0"
          icon="pi pi-download"
          text
          severity="secondary"
          size="small"
          label="JSON"
          @click="downloadLogsAsJson"
        />
        <SelectButton
          :model-value="editorStore.uiLogFilter"
          :options="filterOptions"
          option-value="value"
          option-label="label"
          size="small"
          :allow-empty="false"
          @update:model-value="editorStore.setUiLogFilter"
        />
        <Button
          v-if="editorStore.uiLogEntries.length > 0"
          icon="pi pi-trash"
          text
          severity="secondary"
          size="small"
          @click="editorStore.clearUiLogs()"
        />
      </div>
    </div>

    <div class="live-logs-content">
      <div v-if="entries.length === 0" class="empty-state">
        <span class="text-color-secondary text-sm">No live logs yet</span>
      </div>
      <div v-else class="entries">
        <div
          v-for="entry in compactMode ? compactEntries : entries"
          :key="entry.id"
          class="entry"
          :class="{ compact: compactMode }"
        >
          <div v-if="!compactMode" class="entry-meta">
            <span class="entry-time">{{ formatTime(entry.timestamp) }}</span>
            <Tag :value="sourceLabel(entry.source)" :severity="sourceSeverity(entry.source)" size="small" />
          </div>
          <div class="entry-message" :class="levelClass(entry.level)">
            {{ entry.message }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.live-logs {
  display: flex;
  flex-direction: column;
  height: 100%;
  border-top: 1px solid var(--p-surface-200);
}

.live-logs-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-200);
  gap: 0.5rem;
}

.live-logs-content {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0.5rem;
}

.empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
}

.entries {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.entry {
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 0.5rem;
  background: var(--p-surface-50);
}

.entry.compact {
  padding: 0.25rem 0.4rem;
  background: transparent;
  border-color: transparent;
  border-bottom: 1px solid var(--p-surface-200);
  border-radius: 0;
}

.entry.compact .entry-message {
  font-size: 0.76rem;
  line-height: 1.2;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.entry-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.3rem;
}

.entry-time {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  font-family: monospace;
}

.entry-message {
  font-size: 0.8125rem;
  white-space: pre-wrap;
  word-break: break-word;
}

:global(.app-dark) .live-logs {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .live-logs-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .entry {
  border-color: var(--p-surface-700);
  background: var(--p-surface-800);
}
</style>
