<script setup lang="ts">
/**
 * AiMessage.vue
 * Wrapper component that renders the correct message type
 */
import { computed } from 'vue'
import TextMessage from './TextMessage.vue'
import CodeMessage from './CodeMessage.vue'
import ToolExecutionMessage from './ToolExecutionMessage.vue'
import ReasoningMessage from './ReasoningMessage.vue'
import type { AiChatMessage } from '@/shared/types/ai-chat.types'

interface Props {
  message: AiChatMessage
  onApplyCode?: (code: string, fileName?: string) => void
}

const props = defineProps<Props>()

const isUser = computed(() => props.message.role === 'user')
const isSystem = computed(() => props.message.role === 'system')

// Determine component to use
const messageComponent = computed(() => {
  switch (props.message.type) {
    case 'code':
      return CodeMessage
    case 'tool_execution':
      return ToolExecutionMessage
    case 'reasoning':
      return ReasoningMessage
    case 'error':
    case 'text':
    case 'generating':
    default:
      return TextMessage
  }
})

// Generate props for the message component
const componentProps = computed(() => {
  const base = {
    content: props.message.content,
    timestamp: props.message.timestamp,
  }

  switch (props.message.type) {
    case 'code':
      return {
        ...base,
        codeBlocks: props.message.codeBlocks,
        onApply: props.onApplyCode,
      }
    case 'tool_execution':
      return {
        ...base,
        toolName: props.message.toolName || 'unknown',
        status: props.message.toolStatus || 'executing',
        result: props.message.toolResult,
      }
    case 'reasoning':
      return {
        ...base,
        isCollapsed: props.message.isCollapsed,
      }
    default:
      return base
  }
})
</script>

<template>
  <div
    class="ai-message"
    :class="{
      user: isUser,
      assistant: !isUser && !isSystem,
      system: isSystem,
      streaming: message.isStreaming,
      error: message.type === 'error',
    }"
  >
    <div class="message-role">
      <i v-if="isUser" class="pi pi-user"></i>
      <i v-else-if="isSystem" class="pi pi-info-circle"></i>
      <i v-else class="pi pi-sparkles"></i>
      <span>{{ isUser ? 'You' : isSystem ? 'System' : 'AI' }}</span>
    </div>

    <div class="message-body">
      <!-- Generating indicator -->
      <div v-if="message.type === 'generating'" class="generating-indicator">
        <i class="pi pi-spin pi-spinner"></i>
        <span>{{ message.content || 'Thinking...' }}</span>
      </div>

      <!-- Error styling -->
      <div v-else-if="message.type === 'error'" class="error-message">
        <i class="pi pi-exclamation-triangle"></i>
        <span>{{ message.content }}</span>
      </div>

      <!-- Dynamic message component -->
      <component v-else :is="messageComponent" v-bind="componentProps" />
    </div>
  </div>
</template>

<style scoped>
.ai-message {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px;
  border-radius: 12px;
  max-width: 90%;
}

.ai-message.user {
  background: var(--p-primary-100);
  margin-left: 10%;
  align-self: flex-end;
}

.ai-message.assistant {
  background: var(--p-surface-100);
  margin-right: 10%;
  align-self: flex-start;
}

.ai-message.system {
  background: var(--p-blue-50);
  margin: 0 5%;
  align-self: center;
  max-width: 95%;
}

.ai-message.streaming {
  border: 1px dashed var(--p-primary-300);
}

.ai-message.error {
  background: var(--p-red-50);
  border: 1px solid var(--p-red-200);
}

.message-role {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--p-text-secondary);
}

.message-role i {
  font-size: 0.8rem;
}

.user .message-role i {
  color: var(--p-primary-600);
}

.assistant .message-role i {
  color: var(--p-purple-500);
}

.message-body {
  padding-left: 22px;
}

.generating-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--p-text-secondary);
  font-size: 0.875rem;
}

.generating-indicator i {
  color: var(--p-primary-500);
}

.error-message {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  color: var(--p-red-600);
  font-size: 0.875rem;
}

.error-message i {
  margin-top: 2px;
}

/* Dark mode */
:global(.app-dark) .ai-message.user {
  background: var(--p-primary-900);
}

:global(.app-dark) .ai-message.assistant {
  background: var(--p-surface-800);
}

:global(.app-dark) .ai-message.system {
  background: rgba(59, 130, 246, 0.15);
}

:global(.app-dark) .ai-message.error {
  background: rgba(239, 68, 68, 0.15);
  border-color: var(--p-red-800);
}

:global(.app-dark) .error-message {
  color: var(--p-red-400);
}
</style>
