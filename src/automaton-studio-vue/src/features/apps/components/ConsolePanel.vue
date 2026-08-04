<script setup lang="ts">
/**
 * ConsolePanel.vue
 * Displays backend runtime action results from the running application and AI testing.
 */
import { computed, nextTick, ref, watch } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { ConsoleEntry } from '../types/console.types'

const props = defineProps<{
  targetId?: string | null
}>()

const editorStore = useAppEditorStore()
const toast = useToast()

const entries = computed(() => editorStore.consoleEntries)
const hasEntries = computed(() => entries.value.length > 0)
const runtimeEntries = computed(() => entries.value.filter((entry) =>
  entry.type === 'runtime-log' && (!props.targetId || entry.deploymentTargetId === props.targetId),
))
const structuredEntries = computed(() => entries.value.filter((entry) => entry.type !== 'runtime-log'))
const hasStructuredEntries = computed(() => structuredEntries.value.length > 0)
const autoScroll = ref(true)
const entriesContainer = ref<HTMLElement | null>(null)

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
  if (entry.type === 'runtime-log') {
    return entry.level === 'error' ? 'pi pi-times-circle' : 'pi pi-circle-fill'
  }

  return entry.result.success ? 'pi pi-check-circle' : 'pi pi-times-circle'
}

function getStatusClass(entry: ConsoleEntry): string {
  if (entry.type === 'runtime-log') {
    return entry.level === 'error' ? 'text-red-500' : 'text-green-500'
  }

  return entry.result.success ? 'text-green-500' : 'text-red-500'
}

function getSourceSeverity(source: 'application' | 'ai-test'): 'info' | 'warning' {
  return source === 'ai-test' ? 'warning' : 'info'
}

function getSourceLabel(source: 'application' | 'ai-test'): string {
  return source === 'ai-test' ? 'AI Test' : 'Application'
}

function formatInputs(inputs: Record<string, unknown>): string {
  if (!inputs || Object.keys(inputs).length === 0) return 'none'
  return JSON.stringify(inputs, null, 2)
}

function formatOutputs(outputs: Record<string, string> | undefined): string {
  if (!outputs || Object.keys(outputs).length === 0) return 'none'
  return JSON.stringify(outputs, null, 2)
}

function formatOutputLines(output: string | undefined): string[] {
  if (!output) return []

  return output
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter((line) => line.length > 0)
}

function clearConsole(): void {
  editorStore.clearConsole()
}

function getRuntimeLevelLabel(entry: ConsoleEntry): string {
  if (entry.level === 'error') return 'ERROR'
  if (entry.level === 'warn') return 'WARN'
  return 'INFO'
}

function normalizeRuntimeMessage(entry: ConsoleEntry): string {
  const rawMessage = entry.result.output ?? ''
  if (entry.type !== 'runtime-log' || !rawMessage) {
    return rawMessage
  }

  const normalizedStepName = entry.stepName.trim()
  if (!normalizedStepName) {
    return rawMessage
  }

  const prefixes = [
    `${normalizedStepName}:`,
    `${normalizedStepName} -`,
    `${normalizedStepName} `,
  ]

  for (const prefix of prefixes) {
    if (rawMessage.startsWith(prefix)) {
      return rawMessage.slice(prefix.length).trimStart()
    }
  }

  return rawMessage
}

function getRuntimeContext(entry: ConsoleEntry): string {
  return entry.stepName.trim() || 'backend'
}

function getRuntimeRowClass(entry: ConsoleEntry): string {
  if (entry.level === 'error') return 'console-raw-line--error'
  if (entry.level === 'warn') return 'console-raw-line--warn'
  return ''
}

function buildRuntimeLogLine(entry: ConsoleEntry): string {
  return `[${formatTime(entry.timestamp)}] ${getRuntimeLevelLabel(entry).padEnd(5, ' ')} ${getRuntimeContext(entry)}  ${normalizeRuntimeMessage(entry)}`
}

async function copyVisibleLogs(): Promise<void> {
  const visibleRuntimeLogs = runtimeEntries.value.map(buildRuntimeLogLine)
  const visibleStructuredLogs = structuredEntries.value.flatMap((entry) => {
    const lines = [`[${formatTime(entry.timestamp)}] ${entry.result.success ? 'INFO ' : 'ERROR'} ${entry.stepName}  ${entry.result.status}`]

    if (entry.result.error) {
      lines.push(`  Error: ${entry.result.error}`)
    }

    if (entry.result.logs?.length) {
      lines.push(...entry.result.logs.map((line) => `  ${line}`))
    }

    return lines
  })

  const text = [...visibleRuntimeLogs, ...visibleStructuredLogs].join('\n').trim()
  if (!text) {
    return
  }

  try {
    await navigator.clipboard.writeText(text)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Visible backend logs copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Copy failed',
      detail: 'Unable to copy backend logs',
      life: 3000,
    })
  }
}

watch(
  () => runtimeEntries.value.length,
  async () => {
    if (!autoScroll.value) {
      return
    }

    await nextTick()
    const container = entriesContainer.value
    if (!container) {
      return
    }

    container.scrollTop = 0
  },
)
</script>

<template>
  <div class="console-panel">
    <!-- Header -->
    <div class="console-header">
      <div class="flex align-items-center gap-2">
        <Tag v-if="hasEntries" :value="entries.length.toString()" severity="secondary" rounded />
      </div>
      <div class="console-actions">
        <label class="console-toggle">
          <span>Auto-scroll</span>
          <ToggleSwitch v-model="autoScroll" size="small" />
        </label>
        <Button
          v-if="hasEntries"
          label="Copy"
          icon="pi pi-copy"
          text
          size="small"
          severity="secondary"
          @click="copyVisibleLogs"
        />
        <Button
          v-if="hasEntries"
          v-tooltip.left="'Clear console'"
          icon="pi pi-trash"
          text
          size="small"
          severity="secondary"
          @click="clearConsole"
        />
      </div>
    </div>

    <!-- Entries -->
    <div ref="entriesContainer" class="console-entries">
      <div v-if="!hasEntries" class="empty-state">
        <i class="pi pi-inbox text-4xl text-color-secondary mb-3"></i>
        <p class="text-color-secondary m-0">No backend messages yet.</p>
        <p class="text-color-secondary text-sm m-0 mt-1">
          Messages from your application's backend will appear here while it is running.
        </p>
      </div>

      <div
        v-for="entry in runtimeEntries"
        :key="entry.id"
        class="console-raw-line"
        :class="getRuntimeRowClass(entry)"
        :title="entry.runnerName || undefined"
      >
        <span class="raw-time">[{{ formatTime(entry.timestamp) }}]</span>
        <span class="raw-level">{{ getRuntimeLevelLabel(entry) }}</span>
        <span class="raw-context">{{ getRuntimeContext(entry) }}</span>
        <span class="raw-message">{{ normalizeRuntimeMessage(entry) }}</span>
      </div>

      <template v-if="hasStructuredEntries">
        <div class="console-structured-separator">
          <span>Execution Records</span>
        </div>

        <div v-for="entry in structuredEntries" :key="entry.id" class="console-entry" :class="{ 'entry-error': !entry.result.success }">
          <!-- Entry header -->
          <div class="entry-header">
            <div class="flex align-items-center gap-2">
              <i :class="[getStatusIcon(entry), getStatusClass(entry)]"></i>
              <span class="step-name">
                {{ entry.runnerName ? `${entry.runnerName} · ${entry.stepName}` : entry.stepName }}
              </span>
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
            <div class="detail-row">
              <span class="detail-label">Status:</span>
              <span :class="getStatusClass(entry)">{{ entry.result.status }}</span>
            </div>

            <div v-if="entry.result.error" class="detail-row error-message">
              <span class="detail-label">Error:</span>
              <span class="text-red-500">{{ entry.result.error }}</span>
            </div>

            <div class="detail-row">
              <span class="detail-label">Inputs:</span>
              <pre class="detail-code">{{ formatInputs(entry.inputs) }}</pre>
            </div>

            <div v-if="entry.result.outputVariables" class="detail-row">
              <span class="detail-label">Outputs:</span>
              <pre class="detail-code">{{ formatOutputs(entry.result.outputVariables) }}</pre>
            </div>

            <div v-if="entry.result.logs && entry.result.logs.length > 0" class="detail-row">
              <span class="detail-label">Logs:</span>
              <pre class="detail-code logs">{{ entry.result.logs.join('\n') }}</pre>
            </div>

            <div v-if="formatOutputLines(entry.result.output).length > 0" class="detail-row">
              <span class="detail-label">Backend output:</span>
              <pre class="detail-code logs">{{ formatOutputLines(entry.result.output).join('\n') }}</pre>
            </div>
          </div>
        </div>
      </template>
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
  border-bottom: 1px solid color-mix(in srgb, var(--p-surface-300) 72%, transparent);
  flex-shrink: 0;
}

.console-actions {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.console-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
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
  border-radius: 10px;
  overflow: hidden;
  background: color-mix(in srgb, var(--p-surface-100) 60%, transparent);
}

.console-raw-line {
  display: grid;
  grid-template-columns: 6.25rem 4.5rem minmax(10rem, 14rem) minmax(0, 1fr);
  gap: 0.75rem;
  align-items: start;
  padding: 0.2rem 0.75rem;
  border-bottom: 1px solid color-mix(in srgb, var(--p-surface-300) 35%, transparent);
  font-family: monospace;
  font-size: 0.82rem;
  line-height: 1.45;
  user-select: text;
  -webkit-user-select: text;
}

.console-raw-line--error {
  background: color-mix(in srgb, var(--p-red-50) 60%, transparent);
}

.console-raw-line--warn {
  background: color-mix(in srgb, var(--p-orange-50) 72%, transparent);
}

.raw-time {
  color: var(--p-text-muted-color);
  white-space: nowrap;
}

.raw-level {
  white-space: nowrap;
  font-weight: 600;
}

.raw-context {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.raw-message {
  min-width: 0;
  white-space: pre-wrap;
  word-break: break-word;
}

.console-structured-separator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0.85rem 0 0.6rem;
  color: var(--p-text-muted-color);
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.console-structured-separator::before,
.console-structured-separator::after {
  content: '';
  flex: 1;
  height: 1px;
  background: color-mix(in srgb, var(--p-surface-300) 55%, transparent);
}

.console-entry.entry-error {
  background: color-mix(in srgb, var(--p-red-50) 68%, transparent);
}

.entry-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.75rem;
  background: transparent;
  border-bottom: 1px solid color-mix(in srgb, var(--p-surface-300) 62%, transparent);
}

.entry-error .entry-header {
  border-color: color-mix(in srgb, var(--p-red-200) 60%, transparent);
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
  background: color-mix(in srgb, var(--p-surface-0) 45%, transparent);
  border-radius: 8px;
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
  background: color-mix(in srgb, var(--p-red-50) 72%, transparent);
  border-radius: 8px;
}

/* Dark mode */
:global(.app-dark) .console-header {
  border-color: color-mix(in srgb, var(--p-surface-600) 68%, transparent);
}

:global(.app-dark) .console-entry {
  background: color-mix(in srgb, var(--p-surface-800) 72%, transparent);
}

:global(.app-dark) .console-raw-line {
  border-color: color-mix(in srgb, var(--p-surface-600) 45%, transparent);
}

:global(.app-dark) .console-entry.entry-error {
  background: color-mix(in srgb, var(--p-red-900) 28%, transparent);
}

:global(.app-dark) .console-raw-line--error {
  background: color-mix(in srgb, var(--p-red-900) 20%, transparent);
}

:global(.app-dark) .console-raw-line--warn {
  background: color-mix(in srgb, var(--p-orange-900) 20%, transparent);
}

:global(.app-dark) .console-structured-separator::before,
:global(.app-dark) .console-structured-separator::after {
  background: color-mix(in srgb, var(--p-surface-600) 55%, transparent);
}

:global(.app-dark) .entry-header {
  background: transparent;
  border-color: color-mix(in srgb, var(--p-surface-600) 68%, transparent);
}

:global(.app-dark) .entry-error .entry-header {
  border-color: color-mix(in srgb, var(--p-red-700) 55%, transparent);
}

:global(.app-dark) .detail-code {
  background: color-mix(in srgb, var(--p-surface-900) 55%, transparent);
}

:global(.app-dark) .error-message {
  background: color-mix(in srgb, var(--p-red-900) 22%, transparent);
}

@media (max-width: 900px) {
  .console-actions {
    gap: 0.25rem;
  }

  .console-toggle {
    gap: 0.35rem;
    font-size: 0.75rem;
  }

  .console-raw-line {
    grid-template-columns: 5.75rem 4rem minmax(7rem, 10rem) minmax(0, 1fr);
    gap: 0.5rem;
    padding-inline: 0.5rem;
    font-size: 0.78rem;
  }
}
</style>
