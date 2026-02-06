<script setup lang="ts">
/**
 * StepAiAssistant.vue
 * Feature-specific wrapper for AiChat component
 * Configures the generic AiChat for step editor context
 */
import { computed } from 'vue'
import AiChat from '@/shared/components/AiChat.vue'
import type { AiChatConfig, AiChatHandlers } from '@/shared/types/ai-chat.types'

// NOTE: Replace with actual step store when available
// import { useStepEditorStore } from '../stores/step-editor.store'
// const stepStore = useStepEditorStore()

// Configuration specific to step editing
const config = computed<AiChatConfig>(() => ({
  title: 'AI Code Assistant',
  inputPlaceholder: 'Describe the step logic or request code help...',
  emptyStateMessage: 'Ask me to help write or debug your custom step',
  suggestions: [
    'Generate Python code',
    'Add input validation',
    'Create error handling',
    'Write documentation',
  ],
  context: {
    // TODO: Add step context
    // stepId: stepStore.currentStep?.id,
    // stepName: stepStore.currentStep?.name,
    // language: stepStore.currentStep?.language,
  },
}))

// Handlers for step editor AI interactions
const handlers: AiChatHandlers = {
  onSendMessage: async (message: string, _context) => {
    // TODO: Call step-specific AI endpoint
    // TODO: Generate or fix step code
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve(
          `Step AI assistant will help write and debug custom steps.\n\nYour request: "${message}"\n\nThis will integrate with the MCP server to generate step code and provide suggestions.`,
        )
      }, 1000)
    })
  },

  onSuggestion: (suggestion: string) => {
    // Optional: Insert code snippets or templates
    console.log('Step suggestion selected:', suggestion)
  },
}
</script>

<template>
  <AiChat :config="config" :handlers="handlers" />
</template>
