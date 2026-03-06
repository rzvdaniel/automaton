<script setup lang="ts">
/**
 * ToolOperationsGroup.vue
 * Renders a collapsible group of consecutive tool operations with human-friendly labels.
 * Expands while operations are running, auto-collapses when all complete.
 */
import { ref, computed, watch } from 'vue'
import type { ChatMessage } from '@/shared/types/ai.types'

const props = defineProps<{
  operations: ChatMessage[]
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

/** Map tool name + args to a human-readable label */
function getLabel(msg: ChatMessage): string {
  const args = msg.toolArgs || {}
  const rawPath: string = args.path || args.filePath || args.file_path || ''
  const fileName = rawPath ? rawPath.replace(/\\/g, '/').split('/').pop() || rawPath : ''

  switch (msg.toolName) {
    case 'read_file': {
      const lines =
        args.startLine != null && args.endLine != null
          ? `, lines ${args.startLine}–${args.endLine}`
          : args.start_line != null && args.end_line != null
            ? `, lines ${args.start_line}–${args.end_line}`
            : ''
      return `Read ${fileName || 'file'}${lines}`
    }
    case 'write_file':
      return `Created ${fileName || 'file'}`
    case 'edit_file':
      return `Edited ${fileName || 'file'}`
    case 'delete_file':
      return `Deleted ${fileName || 'file'}`
    case 'search_file':
    case 'search_files': {
      const pattern = args.pattern || args.query || args.search || ''
      return pattern ? `Searched for "${pattern}"` : 'Searched files'
    }
    case 'list_files':
      return `Listed files${fileName ? ` in ${fileName}` : ''}`
    case 'propose_plan':
      return 'Updated plan'
    case 'stage_complete':
      return `Completed stage${args.stageIndex != null ? ` ${Number(args.stageIndex) + 1}` : ''}`
    default:
      return msg.toolName || 'Unknown operation'
  }
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
  align-items: center;
  gap: 0.4rem;
  padding: 0.2rem 0.6rem 0.2rem 1.4rem;
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
