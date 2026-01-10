<script setup lang="ts">
/**
 * AppAiAssistant.vue
 * Feature-specific wrapper for AiChat component
 * Configures the generic AiChat for app editor context
 */
import { computed } from 'vue'
import AiChat from '@/shared/components/AiChat.vue'
import type { AiChatConfig, AiChatHandlers } from '@/shared/types/ai-chat.types'
import { useAppEditorStore } from '../stores/app-editor.store'

const editorStore = useAppEditorStore()

// Configuration specific to app editing
const config = computed<AiChatConfig>(() => ({
  title: 'AI Assistant',
  inputPlaceholder: 'Describe what you want to add or change...',
  emptyStateMessage: 'Ask me to create or modify your app',
  suggestions: [
    'Add a login form',
    'Create a navigation bar',
    'Add a contact form',
    'Create a data table',
  ],
  context: {
    appId: editorStore.currentApp?.id,
    appName: editorStore.currentApp?.name,
    activeFile: editorStore.activeFilePath,
  },
}))

// Handlers for app editor AI interactions
const handlers: AiChatHandlers = {
  onSendMessage: async (message: string, context) => {
    // TODO: Integrate with actual MCP server for app generation
    // For now, simulate API call
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve(
          `AI assistant is not yet implemented. This will connect to the MCP server for app generation.\n\nYour request: "${message}"\n\nContext:\n- App: ${context?.appName || 'Unknown'}\n- File: ${context?.activeFile || 'None selected'}`,
        )
      }, 1000)
    })
  },
  
  onSuggestion: (suggestion: string) => {
    // Optional: Handle suggestion selection differently
    console.log('App suggestion selected:', suggestion)
  },
}
</script>

<template>
  <AiChat :config="config" :handlers="handlers" />
</template>
