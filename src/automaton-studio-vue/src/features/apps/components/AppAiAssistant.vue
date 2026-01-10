<script setup lang="ts">
/**
 * AppAiAssistant.vue
 * Feature-specific wrapper for AiChatPanel component
 * Configures the AI chat for app editor context with file integration
 */
import { computed } from 'vue'
import { useToast } from 'primevue/usetoast'
import AiChatPanel from '@/shared/components/ai/AiChatPanel.vue'
import type { AiChatConfig } from '@/shared/types/ai-chat.types'
import { useAppEditorStore } from '../stores/app-editor.store'

const toast = useToast()
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
    'Style the page with modern CSS',
    'Add form validation',
  ],
  // Resource context for the AI
  resourceType: 'app',
  resourceId: editorStore.currentApp?.id,
  language: detectActiveFileLanguage(),
  // Additional context passed to the AI
  context: {
    appId: editorStore.currentApp?.id,
    appName: editorStore.currentApp?.name,
    activeFile: editorStore.activeFilePath,
    openFiles: editorStore.openFiles.map((f) => f.path),
  },
}))

// Detect language from active file
function detectActiveFileLanguage(): string {
  const path = editorStore.activeFilePath
  if (!path) return 'html'

  const ext = path.split('.').pop()?.toLowerCase()
  switch (ext) {
    case 'html':
    case 'htm':
      return 'html'
    case 'css':
      return 'css'
    case 'js':
      return 'javascript'
    case 'ts':
      return 'typescript'
    case 'json':
      return 'json'
    default:
      return 'html'
  }
}

// Handle applying generated code to the editor
async function handleApplyCode(code: string, fileName?: string) {
  try {
    // Determine target file
    const targetFile = fileName || editorStore.activeFilePath

    if (!targetFile) {
      toast.add({
        severity: 'warn',
        summary: 'No File Selected',
        detail: 'Please select a file in the editor to apply the code.',
        life: 3000,
      })
      return
    }

    // Check if file exists, if not create it
    const existingFile = editorStore.openFiles.find((f) => f.path === targetFile)

    if (!existingFile) {
      // Create new file with the code
      await editorStore.createFile(targetFile, code)
      toast.add({
        severity: 'success',
        summary: 'File Created',
        detail: `Created ${targetFile} with generated code`,
        life: 3000,
      })
    } else {
      // Update existing file
      // For now, replace content - could be enhanced to merge/append
      editorStore.updateFileContent(targetFile, code)
      toast.add({
        severity: 'success',
        summary: 'Code Applied',
        detail: `Updated ${targetFile}`,
        life: 3000,
      })
    }

    // Ensure the file is open and active
    if (editorStore.activeFilePath !== targetFile) {
      editorStore.activeFilePath = targetFile
    }
  } catch (error) {
    console.error('Failed to apply code:', error)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to apply the generated code',
      life: 5000,
    })
  }
}
</script>

<template>
  <AiChatPanel :config="config" :on-apply-code="handleApplyCode" />
</template>
