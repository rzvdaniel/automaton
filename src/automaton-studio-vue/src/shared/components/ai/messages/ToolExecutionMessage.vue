<script setup lang="ts">
/**
 * ToolExecutionMessage.vue
 * Renders tool execution status and results
 */
import { ref, computed } from 'vue'
import type { ToolExecutionStatus, ToolExecutionResult } from '@/shared/types/ai-chat.types'

interface Props {
  toolName: string
  status: ToolExecutionStatus
  result?: ToolExecutionResult
  timestamp: Date
}

const props = defineProps<Props>()

const isExpanded = ref(false)

const statusIcon = computed(() => {
  switch (props.status) {
    case 'executing':
      return 'pi pi-spin pi-spinner'
    case 'completed':
      return props.result?.isSuccess ? 'pi pi-check-circle' : 'pi pi-times-circle'
    case 'error':
      return 'pi pi-exclamation-triangle'
    default:
      return 'pi pi-cog'
  }
})

const statusColor = computed(() => {
  switch (props.status) {
    case 'executing':
      return 'var(--p-primary-500)'
    case 'completed':
      return props.result?.isSuccess ? 'var(--p-green-500)' : 'var(--p-red-500)'
    case 'error':
      return 'var(--p-orange-500)'
    default:
      return 'var(--p-text-secondary)'
  }
})

const statusLabel = computed(() => {
  switch (props.status) {
    case 'executing':
      return 'Running...'
    case 'completed':
      return props.result?.isSuccess ? 'Completed' : 'Failed'
    case 'error':
      return 'Error'
    default:
      return 'Pending'
  }
})

function getToolDisplayName(name: string): string {
  // Convert snake_case to Title Case
  return name
    .split('_')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ')
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <div class="tool-message" :class="{ executing: status === 'executing' }">
    <div class="tool-header" @click="isExpanded = !isExpanded">
      <i :class="statusIcon" :style="{ color: statusColor }"></i>
      <div class="tool-info">
        <span class="tool-name">{{ getToolDisplayName(toolName) }}</span>
        <span class="tool-status" :style="{ color: statusColor }">{{ statusLabel }}</span>
      </div>
      <i
        v-if="result?.data || result?.error"
        :class="isExpanded ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
        class="expand-icon"
      ></i>
    </div>

    <!-- Expanded result -->
    <div v-if="isExpanded && (result?.data || result?.error)" class="tool-result">
      <div v-if="result?.error" class="error-content">
        {{ result.error }}
      </div>
      <pre v-else-if="result?.data" class="result-content">{{
        typeof result.data === 'string' ? result.data : JSON.stringify(result.data, null, 2)
      }}</pre>
    </div>

    <div class="message-footer">
      <span class="timestamp">{{ formatTime(timestamp) }}</span>
    </div>
  </div>
</template>

<style scoped>
.tool-message {
  background: var(--p-surface-100);
  border-radius: 8px;
  padding: 10px 12px;
  border-left: 3px solid var(--p-surface-400);
}

.tool-message.executing {
  border-left-color: var(--p-primary-500);
}

.tool-header {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
}

.tool-header i:first-child {
  font-size: 1rem;
}

.tool-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.tool-name {
  font-size: 0.85rem;
  font-weight: 500;
}

.tool-status {
  font-size: 0.75rem;
}

.expand-icon {
  font-size: 0.75rem;
  color: var(--p-text-secondary);
}

.tool-result {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--p-surface-300);
}

.result-content {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.75rem;
  background: var(--p-surface-0);
  padding: 10px;
  border-radius: 6px;
  overflow-x: auto;
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
}

.error-content {
  font-size: 0.8rem;
  color: var(--p-red-600);
  padding: 8px;
  background: var(--p-red-50);
  border-radius: 6px;
}

.message-footer {
  margin-top: 6px;
}

.timestamp {
  font-size: 0.7rem;
  color: var(--p-text-secondary);
}

/* Dark mode */
:global(.app-dark) .tool-message {
  background: var(--p-surface-800);
}

:global(.app-dark) .tool-result {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .result-content {
  background: var(--p-surface-900);
}

:global(.app-dark) .error-content {
  background: rgba(239, 68, 68, 0.15);
  color: var(--p-red-400);
}
</style>
