<script setup lang="ts">
/**
 * ReasoningMessage.vue
 * Collapsible AI reasoning/thinking message
 */
import { ref } from 'vue'

interface Props {
  content: string
  timestamp: Date
  isCollapsed?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  isCollapsed: true,
})

const collapsed = ref(props.isCollapsed)

function formatTime(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
  })
}

// Truncate for preview
function getPreview(text: string, maxLength: number = 80): string {
  if (text.length <= maxLength) return text
  return text.substring(0, maxLength) + '...'
}
</script>

<template>
  <div class="reasoning-message">
    <div class="reasoning-header" @click="collapsed = !collapsed">
      <i class="pi pi-lightbulb"></i>
      <span class="reasoning-label">AI Thinking</span>
      <span v-if="collapsed" class="reasoning-preview">{{ getPreview(content) }}</span>
      <i :class="collapsed ? 'pi pi-chevron-down' : 'pi pi-chevron-up'" class="toggle-icon"></i>
    </div>

    <div v-show="!collapsed" class="reasoning-content">
      {{ content }}
    </div>

    <div class="message-footer">
      <span class="timestamp">{{ formatTime(timestamp) }}</span>
    </div>
  </div>
</template>

<style scoped>
.reasoning-message {
  background: var(--p-surface-100);
  border-radius: 8px;
  padding: 10px 12px;
  border-left: 3px solid var(--p-amber-400);
}

.reasoning-header {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  user-select: none;
}

.reasoning-header i:first-child {
  color: var(--p-amber-500);
  font-size: 0.9rem;
}

.reasoning-label {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--p-amber-600);
}

.reasoning-preview {
  flex: 1;
  font-size: 0.75rem;
  color: var(--p-text-secondary);
  font-style: italic;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.toggle-icon {
  font-size: 0.75rem;
  color: var(--p-text-secondary);
}

.reasoning-content {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--p-surface-300);
  font-size: 0.8rem;
  line-height: 1.5;
  color: var(--p-text-secondary);
  white-space: pre-wrap;
}

.message-footer {
  margin-top: 6px;
}

.timestamp {
  font-size: 0.7rem;
  color: var(--p-text-secondary);
}

/* Dark mode */
:global(.app-dark) .reasoning-message {
  background: var(--p-surface-800);
}

:global(.app-dark) .reasoning-label {
  color: var(--p-amber-400);
}

:global(.app-dark) .reasoning-content {
  border-color: var(--p-surface-700);
}
</style>
