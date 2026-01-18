<script setup lang="ts">
/**
 * ConsolePanel.vue
 * Displays step execution results from preview and AI testing
 */
import { computed } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { ConsoleEntry } from '../types/console.types'

const editorStore = useAppEditorStore()

const entries = computed(() => editorStore.consoleEntries)
const hasEntries = computed(() => entries.value.length > 0)

function formatTime(date: Date): string {
  return new Date(date).toLocaleTimeString('en-US', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatDuration(ms: number | undefined): string {
  if (ms === undefined) return ''
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

function getStatusIcon(entry: ConsoleEntry): string {
  return entry.result.success ? 'pi pi-check-circle' : 'pi pi-times-circle'
}

function getStatusClass(entry: ConsoleEntry): string {
  return entry.result.success ? 'text-green-500' : 'text-red-500'
}

function getSourceSeverity(source: 'preview' | 'ai-test'): 'info' | 'warning' {
  return source === 'ai-test' ? 'warning' : 'info'
}

function getSourceLabel(source: 'preview' | 'ai-test'): string {
  return source === 'ai-test' ? 'AI Test' : 'Preview'
}

function formatInputs(inputs: Record<string, unknown>): string {
  if (!inputs || Object.keys(inputs).length === 0) return 'none'
  return JSON.stringify(inputs, null, 2)
}

function formatOutputs(outputs: Record<string, string> | undefined): string {
  if (!outputs || Object.keys(outputs).length === 0) return 'none'
  return JSON.stringify(outputs, null, 2)
}

function clearConsole(): void {
  editorStore.clearConsole()
}
</script>

<template>
  <div class="console-panel">
    <!-- Header -->
    <div class="console-header">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-terminal text-color-secondary"></i>
        <span class="font-semibold">Console</span>
        <Tag v-if="hasEntries" :value="entries.length.toString()" severity="secondary" rounded />
      </div>
      <Button
        v-if="hasEntries"
        icon="pi pi-trash"
        text
        size="small"
        severity="secondary"
        v-tooltip.left="'Clear console'"
        @click="clearConsole"
      />
    </div>

    <!-- Entries -->
    <div class="console-entries">
      <div v-if="!hasEntries" class="empty-state">
        <i class="pi pi-inbox text-4xl text-color-secondary mb-3"></i>
        <p class="text-color-secondary m-0">No step executions yet</p>
        <p class="text-color-secondary text-sm m-0 mt-1">
          Trigger a step from the preview or AI assistant
        </p>
      </div>

      <div v-for="entry in entries" :key="entry.id" class="console-entry" :class="{ 'entry-error': !entry.result.success }">
        <!-- Entry header -->
        <div class="entry-header">
          <div class="flex align-items-center gap-2">
            <i :class="[getStatusIcon(entry), getStatusClass(entry)]"></i>
            <span class="step-name">{{ entry.stepName }}</span>
            <Tag
              :value="getSourceLabel(entry.source)"
              :severity="getSourceSeverity(entry.source)"
              size="small"
            />
          </div>
          <div class="flex align-items-center gap-2 text-color-secondary text-sm">
            <span v-if="entry.duration">{{ formatDuration(entry.duration) }}</span>
            <span>{{ formatTime(entry.timestamp) }}</span>
          </div>
        </div>

        <!-- Entry details -->
        <div class="entry-details">
          <!-- Status -->
          <div class="detail-row">
            <span class="detail-label">Status:</span>
            <span :class="getStatusClass(entry)">{{ entry.result.status }}</span>
          </div>

          <!-- Error message -->
          <div v-if="entry.result.error" class="detail-row error-message">
            <span class="detail-label">Error:</span>
            <span class="text-red-500">{{ entry.result.error }}</span>
          </div>

          <!-- Inputs -->
          <div class="detail-row">
            <span class="detail-label">Inputs:</span>
            <pre class="detail-code">{{ formatInputs(entry.inputs) }}</pre>
          </div>

          <!-- Outputs -->
          <div v-if="entry.result.outputVariables" class="detail-row">
            <span class="detail-label">Outputs:</span>
            <pre class="detail-code">{{ formatOutputs(entry.result.outputVariables) }}</pre>
          </div>

          <!-- Logs -->
          <div v-if="entry.result.logs && entry.result.logs.length > 0" class="detail-row">
            <span class="detail-label">Logs:</span>
            <pre class="detail-code logs">{{ entry.result.logs.join('\n') }}</pre>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.console-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.console-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.console-entries {
  flex: 1;
  overflow-y: auto;
  padding: 0.5rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  padding: 2rem;
  text-align: center;
}

.console-entry {
  margin-bottom: 0.5rem;
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  overflow: hidden;
}

.console-entry.entry-error {
  border-color: var(--p-red-200);
}

.entry-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-50);
  border-bottom: 1px solid var(--p-surface-200);
}

.entry-error .entry-header {
  background: var(--p-red-50);
  border-color: var(--p-red-200);
}

.step-name {
  font-weight: 600;
  font-family: monospace;
}

.entry-details {
  padding: 0.5rem 0.75rem;
  font-size: 0.875rem;
}

.detail-row {
  margin-bottom: 0.5rem;
}

.detail-row:last-child {
  margin-bottom: 0;
}

.detail-label {
  color: var(--p-text-muted-color);
  margin-right: 0.5rem;
}

.detail-code {
  margin: 0.25rem 0 0 0;
  padding: 0.5rem;
  background: var(--p-surface-100);
  border-radius: var(--p-border-radius);
  font-family: monospace;
  font-size: 0.8125rem;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 150px;
  overflow-y: auto;
}

.detail-code.logs {
  max-height: 200px;
}

.error-message {
  padding: 0.5rem;
  background: var(--p-red-50);
  border-radius: var(--p-border-radius);
}

/* Dark mode */
:global(.app-dark) .console-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .console-entry {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .console-entry.entry-error {
  border-color: var(--p-red-800);
}

:global(.app-dark) .entry-header {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .entry-error .entry-header {
  background: rgba(239, 68, 68, 0.15);
  border-color: var(--p-red-800);
}

:global(.app-dark) .detail-code {
  background: var(--p-surface-800);
}

:global(.app-dark) .error-message {
  background: rgba(239, 68, 68, 0.1);
}
</style>
