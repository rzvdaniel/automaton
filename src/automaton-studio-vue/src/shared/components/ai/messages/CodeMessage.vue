<script setup lang="ts">
/**
 * CodeMessage.vue
 * Renders code blocks with syntax highlighting and apply button
 */
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import type { CodeBlock } from '@/shared/types/ai-chat.types'

interface Props {
  content: string
  codeBlocks?: CodeBlock[]
  timestamp: Date
  onApply?: (code: string, fileName?: string) => void
}

const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'apply', code: string, fileName?: string): void
}>()

const copiedId = ref<string | null>(null)

// Extract code blocks from content if not provided
const blocks = computed<CodeBlock[]>(() => {
  if (props.codeBlocks && props.codeBlocks.length > 0) {
    return props.codeBlocks
  }

  // Parse code blocks from markdown content
  const regex = /```(\w+)?\n([\s\S]*?)```/g
  const matches: CodeBlock[] = []
  let match

  while ((match = regex.exec(props.content)) !== null) {
    matches.push({
      id: `code-${matches.length}`,
      language: match[1] || 'text',
      code: match[2].trim(),
      canApply: true,
    })
  }

  return matches
})

// Get text content without code blocks
const textContent = computed(() => {
  return props.content.replace(/```(\w+)?\n[\s\S]*?```/g, '').trim()
})

function getLanguageLabel(lang: string): string {
  const labels: Record<string, string> = {
    javascript: 'JavaScript',
    typescript: 'TypeScript',
    python: 'Python',
    html: 'HTML',
    css: 'CSS',
    csharp: 'C#',
    json: 'JSON',
    bash: 'Bash',
    powershell: 'PowerShell',
  }
  return labels[lang.toLowerCase()] || lang.toUpperCase()
}

async function copyToClipboard(code: string, blockId: string) {
  try {
    await navigator.clipboard.writeText(code)
    copiedId.value = blockId
    setTimeout(() => {
      copiedId.value = null
    }, 2000)
  } catch (err) {
    console.error('Failed to copy:', err)
  }
}

function handleApply(block: CodeBlock) {
  if (props.onApply) {
    props.onApply(block.code, block.fileName)
  }
  emit('apply', block.code, block.fileName)
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <div class="code-message">
    <!-- Text content before/after code -->
    <div v-if="textContent" class="text-content">{{ textContent }}</div>

    <!-- Code blocks -->
    <div v-for="block in blocks" :key="block.id" class="code-block-container">
      <div class="code-header">
        <span class="language-label">{{ getLanguageLabel(block.language) }}</span>
        <span v-if="block.fileName" class="file-name">{{ block.fileName }}</span>
        <div class="code-actions">
          <Button
            :icon="copiedId === block.id ? 'pi pi-check' : 'pi pi-copy'"
            text
            size="small"
            :severity="copiedId === block.id ? 'success' : 'secondary'"
            @click="copyToClipboard(block.code, block.id)"
            v-tooltip.top="'Copy'"
          />
          <Button
            v-if="block.canApply"
            icon="pi pi-file-edit"
            label="Apply"
            text
            size="small"
            severity="primary"
            @click="handleApply(block)"
            v-tooltip.top="'Apply to editor'"
          />
        </div>
      </div>
      <pre class="code-content"><code>{{ block.code }}</code></pre>
    </div>

    <div class="message-footer">
      <span class="timestamp">{{ formatTime(timestamp) }}</span>
    </div>
  </div>
</template>

<style scoped>
.code-message {
  line-height: 1.6;
}

.text-content {
  font-size: 0.875rem;
  margin-bottom: 8px;
}

.code-block-container {
  border: 1px solid var(--p-surface-300);
  border-radius: 8px;
  overflow: hidden;
  margin: 8px 0;
}

.code-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background: var(--p-surface-100);
  border-bottom: 1px solid var(--p-surface-300);
}

.language-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--p-text-secondary);
}

.file-name {
  font-size: 0.75rem;
  color: var(--p-primary-500);
  font-family: 'JetBrains Mono', monospace;
}

.code-actions {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 4px;
}

.code-content {
  margin: 0;
  padding: 12px;
  background: var(--p-surface-900);
  color: var(--p-surface-50);
  font-family: 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.8rem;
  overflow-x: auto;
  line-height: 1.5;
}

.code-content code {
  display: block;
  white-space: pre;
}

.message-footer {
  margin-top: 4px;
}

.timestamp {
  font-size: 0.7rem;
  color: var(--p-text-secondary);
}

/* Dark mode */
:global(.app-dark) .code-block-container {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .code-header {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .code-content {
  background: var(--p-surface-950);
}
</style>
