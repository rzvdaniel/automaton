<script setup lang="ts">
/**
 * FlowAiAssistant.vue
 * Feature-specific wrapper for AiChat component
 * Configures the generic AiChat for flow editor context
 */
import { computed } from 'vue'
import AiChat from '@/shared/components/AiChat.vue'
import type { AiChatConfig, AiChatHandlers } from '@/shared/types/ai-chat.types'

// NOTE: Replace with actual flow store when available
// import { useFlowEditorStore } from '../stores/flow-editor.store'
// const flowStore = useFlowEditorStore()

// Configuration specific to flow editing
const config = computed<AiChatConfig>(() => ({
  title: 'AI Workflow Assistant',
  inputPlaceholder: 'Describe the workflow step or automation you want to create...',
  emptyStateMessage: 'Ask me to help design or modify your workflow',
  suggestions: [
    'Add email notification step',
    'Create a data validation step',
    'Add conditional logic',
    'Chain multiple steps',
  ],
  context: {
    // TODO: Add flow context
    // flowId: flowStore.currentFlow?.id,
    // flowName: flowStore.currentFlow?.name,
  },
}))

// Handlers for flow editor AI interactions
const handlers: AiChatHandlers = {
  onSendMessage: async (message: string, context) => {
    // TODO: Call flow-specific AI endpoint
    // TODO: Return suggestions for workflow steps
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve(
          `Flow AI assistant will help design workflows.\n\nYour request: "${message}"\n\nThis will integrate with the MCP server to suggest steps and flow configurations.`,
        )
      }, 1000)
    })
  },

  onSuggestion: (suggestion: string) => {
    // Optional: Pre-populate step creation dialog or similar
    console.log('Flow suggestion selected:', suggestion)
  },
}
</script>

<template>
  <AiChat :config="config" :handlers="handlers" />
</template>
