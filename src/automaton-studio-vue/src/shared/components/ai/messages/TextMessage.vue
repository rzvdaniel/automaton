<script setup lang="ts">
/**
 * TextMessage.vue
 * Renders plain text/markdown AI messages
 */
import { computed } from 'vue'

interface Props {
  content: string
  timestamp: Date
}

const props = defineProps<Props>()

// Simple markdown-like formatting
const formattedContent = computed(() => {
  let text = props.content

  // Convert code blocks
  text = text.replace(/```(\w+)?\n([\s\S]*?)```/g, (_, lang, code) => {
    return `<pre class="code-block ${lang || ''}"><code>${escapeHtml(code.trim())}</code></pre>`
  })

  // Convert inline code
  text = text.replace(/`([^`]+)`/g, '<code class="inline-code">$1</code>')

  // Convert bold
  text = text.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')

  // Convert italic
  text = text.replace(/\*([^*]+)\*/g, '<em>$1</em>')

  // Convert line breaks
  text = text.replace(/\n/g, '<br>')

  return text
})

function escapeHtml(text: string): string {
  const div = document.createElement('div')
  div.textContent = text
  return div.innerHTML
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <div class="text-message">
    <div class="message-content" v-html="formattedContent"></div>
    <div class="message-footer">
      <span class="timestamp">{{ formatTime(timestamp) }}</span>
    </div>
  </div>
</template>

<style scoped>
.text-message {
  line-height: 1.6;
}

.message-content {
  font-size: 0.875rem;
  word-wrap: break-word;
}

.message-content :deep(.code-block) {
  background: var(--p-surface-800);
  color: var(--p-surface-50);
  border-radius: 6px;
  padding: 12px;
  margin: 8px 0;
  overflow-x: auto;
  font-family: 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.8rem;
}

.message-content :deep(.inline-code) {
  background: var(--p-surface-200);
  padding: 2px 6px;
  border-radius: 4px;
  font-family: 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.85em;
}

.message-footer {
  margin-top: 4px;
}

.timestamp {
  font-size: 0.7rem;
  color: var(--p-text-secondary);
}

/* Dark mode */
:global(.app-dark) .message-content :deep(.inline-code) {
  background: var(--p-surface-700);
}
</style>
