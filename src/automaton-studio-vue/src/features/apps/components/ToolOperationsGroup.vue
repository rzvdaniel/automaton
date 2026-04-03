<script setup lang="ts">
/**
 * ToolOperationsGroup.vue
 * Renders a collapsible group of consecutive tool operations with human-friendly labels.
 * Expands while operations are running, auto-collapses when all complete.
 */
import { ref, computed, watch } from 'vue'
import type { ChatMessage } from '@/shared/types/ai.types'
import ToolExecutionDetails from '@/shared/components/ToolExecutionDetails.vue'

const props = defineProps<{
  operations: ChatMessage[]
  showDebugDetails?: boolean
}>()

const manualToggle = ref<boolean | null>(null)

const isAnyExecuting = computed(() =>
  props.operations.some((m) => m.toolStatus === 'executing'),
)

const isAllDone = computed(() =>
  props.operations.every((m) => m.toolStatus === 'completed' || m.toolStatus === 'error'),
)

// Auto-expand while executing; auto-collapse when done (unless user manually toggled)
const isExpanded = computed(() => {
  if (manualToggle.value !== null) return manualToggle.value
  return !isAllDone.value
})

// Reset manual toggle when a new batch starts executing
watch(isAnyExecuting, (executing) => {
  if (executing) manualToggle.value = null
})

function toggle() {
  manualToggle.value = !isExpanded.value
}

// --- Tool label registry ---
// Each entry maps a tool name to a formatter function.
// To add a new tool, just add an entry here.
type ToolArgs = Record<string, any>
type LabelFormatter = (file: string, args: ToolArgs) => string

function lineRange(args: ToolArgs): string {
  const s = args.startLine ?? args.start_line
  const e = args.endLine ?? args.end_line
  return s != null && e != null ? `, lines ${s}–${e}` : ''
}

function searchTerm(args: ToolArgs): string {
  return args.pattern || args.query || args.search || ''
}

const toolLabels: Record<string, LabelFormatter> = {
  read_file:      (f, a) => `Read ${f}${lineRange(a)}`,
  write_file:     (f)    => `Created ${f}`,
  edit_file:      (f)    => `Edited ${f}`,
  delete_file:    (f)    => `Deleted ${f}`,
  search_file:    (_f, a) => { const t = searchTerm(a); return t ? `Searched for "${t}"` : 'Searched files' },
  search_files:   (_f, a) => { const t = searchTerm(a); return t ? `Searched for "${t}"` : 'Searched files' },
  list_files:     (f)    => `Listed files${f ? ` in ${f}` : ''}`,
  propose_plan:   ()     => 'Updated plan',
  stage_complete: (_f, a) => `Completed stage${a.stageIndex != null ? ` ${Number(a.stageIndex) + 1}` : ''}`,
}

function getLabel(msg: ChatMessage): string {
  const args = msg.toolArgs || {}
  const rawPath: string = args.path || args.filePath || args.file_path || ''
  const fileName = rawPath ? rawPath.replace(/\\/g, '/').split('/').pop() || rawPath : ''
  const formatter = toolLabels[msg.toolName || '']
  return formatter ? formatter(fileName, args) : (msg.toolName || 'Unknown operation')
}

/** Summary text for the collapsed state */
const summary = computed(() => {
  const total = props.operations.length
  const writes = props.operations.filter((m) =>
    ['write_file', 'edit_file', 'delete_file'].includes(m.toolName || ''),
  ).length
  const reads = props.operations.filter((m) =>
    ['read_file', 'list_files', 'search_file', 'search_files'].includes(m.toolName || ''),
  ).length
  const errors = props.operations.filter((m) => m.toolStatus === 'error').length

  const parts: string[] = []
  if (writes > 0) parts.push(`${writes} change${writes > 1 ? 's' : ''}`)
  if (reads > 0) parts.push(`${reads} read${reads > 1 ? 's' : ''}`)
  const other = total - writes - reads
  if (other > 0 && parts.length > 0) parts.push(`${other} other`)
  if (parts.length === 0) parts.push(`${total} operation${total > 1 ? 's' : ''}`)

  if (errors > 0) parts.push(`${errors} error${errors > 1 ? 's' : ''}`)

  return parts.join(', ')
})
</script>

<template>
  <div class="tool-ops-group" :class="{ expanded: isExpanded, done: isAllDone }">
    <!-- Header (always visible) -->
    <div class="tool-ops-header" @click="toggle">
      <i class="pi" :class="isExpanded ? 'pi-chevron-down' : 'pi-chevron-right'" />
      <span v-if="isAnyExecuting" class="tool-ops-summary">
        <i class="pi pi-spin pi-spinner executing-icon" />
        Running operations...
      </span>
      <span v-else class="tool-ops-summary">{{ summary }}</span>
    </div>

    <!-- Expanded list -->
    <div v-if="isExpanded" class="tool-ops-list">
      <div v-for="op in operations" :key="op.id" class="tool-op-item">
        <i
          v-if="op.toolStatus === 'executing'"
          class="pi pi-spin pi-spinner op-icon executing"
        />
        <i
          v-else-if="op.toolStatus === 'completed'"
          class="pi pi-check op-icon completed"
        />
        <i
          v-else-if="op.toolStatus === 'error'"
          class="pi pi-times op-icon error"
        />
        <span class="op-label">{{ getLabel(op) }}</span>
        <ToolExecutionDetails
          v-if="showDebugDetails && op.toolStatus !== 'executing'"
          class="op-details"
          :tool-name="op.toolName"
          :tool-args="op.toolArgs"
          :tool-result="op.toolResult"
          :tool-status="op.toolStatus"
          :error="op.error"
          :show-debug-details="showDebugDetails"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.tool-ops-group {
  border: 1px solid var(--p-surface-200);
  border-radius: 6px;
  overflow: hidden;
  font-size: 0.8rem;
}

.tool-ops-group.done {
  opacity: 0.85;
}

.tool-ops-header {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.6rem;
  cursor: pointer;
  user-select: none;
  color: var(--p-text-muted-color);
  transition: background 0.15s;
}

.tool-ops-header:hover {
  background: var(--p-surface-100);
}

.tool-ops-header .pi {
  font-size: 0.65rem;
  flex-shrink: 0;
}

.executing-icon {
  font-size: 0.7rem;
  color: var(--p-blue-500);
  margin-right: 0.15rem;
}

.tool-ops-summary {
  font-size: 0.75rem;
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.tool-ops-list {
  border-top: 1px solid var(--p-surface-200);
  padding: 0.25rem 0;
}

.tool-op-item {
  display: flex;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 0.4rem;
  padding: 0.1rem 0.4rem 0.1rem 0.4rem;
  font-size: 0.75rem;
  color: var(--p-text-color);
}

.op-icon {
  font-size: 0.65rem;
  flex-shrink: 0;
}

.op-icon.executing {
  color: var(--p-blue-500);
}

.op-icon.completed {
  color: var(--p-green-500);
}

.op-icon.error {
  color: var(--p-red-500);
}

.op-label {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.op-details {
  margin-left: 1.05rem;
  width: calc(100% - 1.05rem);
}

/* Dark mode */
:global(.app-dark) .tool-ops-group {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .tool-ops-header:hover {
  background: var(--p-surface-800);
}

:global(.app-dark) .tool-ops-list {
  border-color: var(--p-surface-700);
}
</style>
