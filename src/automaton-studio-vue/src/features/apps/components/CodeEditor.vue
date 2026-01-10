<script setup lang="ts">
/**
 * CodeEditor.vue
 * Monaco Editor wrapper component for the App Editor
 * Provides syntax highlighting, auto-completion, and code editing features
 */
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import * as monaco from 'monaco-editor'
import { useAppEditorStore } from '../stores/app-editor.store'

const props = defineProps<{
  filePath: string
}>()

const emit = defineEmits<{
  change: [content: string]
  save: []
}>()

const editorStore = useAppEditorStore()
const editorContainer = ref<HTMLElement>()
let editorInstance: monaco.editor.IStandaloneCodeEditor | null = null

// Get the file from the store
const file = computed(() => {
  return editorStore.openFiles.find((f) => f.path === props.filePath)
})

// Detect if dark mode is enabled
const isDarkMode = computed(() => {
  return document.documentElement.classList.contains('app-dark')
})

// Initialize Monaco Editor
onMounted(() => {
  if (!editorContainer.value || !file.value) return

  // Configure Monaco environment for web workers
  self.MonacoEnvironment = {
    getWorker(_moduleId: string, label: string) {
      // Use basic editor without web workers for simplicity
      // This avoids CORS issues and complex worker setup
      return null as any
    },
  }

  // Create editor instance
  editorInstance = monaco.editor.create(editorContainer.value, {
    value: file.value.content,
    language: file.value.language,
    theme: isDarkMode.value ? 'vs-dark' : 'vs',
    automaticLayout: true,
    minimap: { enabled: true },
    fontSize: 14,
    tabSize: 2,
    wordWrap: 'on',
    lineNumbers: 'on',
    renderLineHighlight: 'all',
    scrollBeyondLastLine: false,
    folding: true,
    autoIndent: 'full',
    formatOnPaste: true,
    formatOnType: true,
    suggestOnTriggerCharacters: true,
    quickSuggestions: true,
    bracketPairColorization: { enabled: true },
    guides: {
      bracketPairs: true,
      indentation: true,
    },
  })

  // Listen for content changes
  editorInstance.onDidChangeModelContent(() => {
    if (!editorInstance) return
    const content = editorInstance.getValue()
    editorStore.updateFileContent(props.filePath, content)
    emit('change', content)
  })

  // Add keyboard shortcuts
  editorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
    emit('save')
  })
})

// Cleanup on unmount
onUnmounted(() => {
  editorInstance?.dispose()
  editorInstance = null
})

// Watch for external content changes (e.g., from AI updates)
watch(
  () => file.value?.content,
  (newContent, oldContent) => {
    if (!editorInstance || !newContent) return

    // Only update if content changed externally (not from editor)
    const currentValue = editorInstance.getValue()
    if (currentValue !== newContent && newContent !== oldContent) {
      // Preserve cursor position
      const position = editorInstance.getPosition()

      editorInstance.setValue(newContent)

      // Restore cursor position
      if (position) {
        editorInstance.setPosition(position)
      }
    }
  },
)

// Watch for theme changes
watch(isDarkMode, (dark) => {
  if (editorInstance) {
    monaco.editor.setTheme(dark ? 'vs-dark' : 'vs')
  }
})

// Watch for language changes
watch(
  () => file.value?.language,
  (newLanguage) => {
    if (editorInstance && newLanguage) {
      const model = editorInstance.getModel()
      if (model) {
        monaco.editor.setModelLanguage(model, newLanguage)
      }
    }
  },
)

// Expose methods for parent component
defineExpose({
  focus: () => editorInstance?.focus(),
  getValue: () => editorInstance?.getValue() ?? '',
  setValue: (value: string) => editorInstance?.setValue(value),
  getPosition: () => editorInstance?.getPosition(),
  setPosition: (position: monaco.IPosition) => editorInstance?.setPosition(position),
  layout: () => editorInstance?.layout(),
})
</script>

<template>
  <div ref="editorContainer" class="code-editor"></div>
</template>

<style scoped>
.code-editor {
  width: 100%;
  height: 100%;
  min-height: 200px;
}
</style>
