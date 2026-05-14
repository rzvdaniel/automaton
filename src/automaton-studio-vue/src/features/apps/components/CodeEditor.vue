<script setup lang="ts">
/**
 * CodeEditor.vue
 * Monaco Editor wrapper component for the App Editor
 * Provides syntax highlighting, auto-completion, and code editing features
 */
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
// Import Monaco config BEFORE monaco-editor to prevent worker errors
import '../config/monaco.config'
import * as monaco from 'monaco-editor'
import { useAppEditorStore } from '../stores/app-editor.store'

const props = defineProps<{
  filePath: string
  content?: string
  language?: string
  readOnly?: boolean
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

const editorContent = computed(() => props.content ?? file.value?.content ?? '')
const editorLanguage = computed(() => props.language ?? file.value?.language ?? 'plaintext')

// Detect if dark mode is enabled
const isDarkMode = computed(() => {
  return document.documentElement.classList.contains('app-dark')
})

// Initialize Monaco Editor
onMounted(() => {
  if (!editorContainer.value) return

  // Create editor instance
  editorInstance = monaco.editor.create(editorContainer.value, {
    value: editorContent.value,
    language: editorLanguage.value,
    theme: isDarkMode.value ? 'vs-dark' : 'vs',
    readOnly: props.readOnly,
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
    formatOnPaste: false, // Requires worker
    formatOnType: false, // Requires worker
    suggestOnTriggerCharacters: true,
    quickSuggestions: true,
    bracketPairColorization: { enabled: true },
    guides: {
      bracketPairs: true,
      indentation: true,
    },
    // Disable features that require web workers to prevent errors
    colorDecorators: false,
    links: false,
    occurrencesHighlight: 'off',
  })

  // Listen for content changes
  editorInstance.onDidChangeModelContent(() => {
    if (!editorInstance) return
    if (props.readOnly) return
    const content = editorInstance.getValue()
    editorStore.updateFileContent(props.filePath, content)
    emit('change', content)
  })

  // Add keyboard shortcuts
  editorInstance.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
    if (props.readOnly) return
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
  () => editorContent.value,
  (newContent, oldContent) => {
    if (!editorInstance) {
      console.log('[CodeEditor] Watch triggered but editor or content missing', {
        hasEditor: !!editorInstance,
        newContent: newContent.substring(0, 50),
      })
      return
    }

    // Only update if content changed externally (not from editor)
    const currentValue = editorInstance.getValue()
    console.log('[CodeEditor] Content watch triggered', {
      filePath: props.filePath,
      currentLength: currentValue.length,
      newLength: newContent.length,
      oldLength: oldContent?.length,
      contentChanged: currentValue !== newContent,
      externalChange: newContent !== oldContent,
    })

    if (currentValue !== newContent && newContent !== oldContent) {
      console.log(`[CodeEditor] Updating editor content for ${props.filePath}`)
      // Preserve cursor position
      const position = editorInstance.getPosition()

      editorInstance.setValue(newContent)

      // Restore cursor position
      if (position) {
        editorInstance.setPosition(position)
      }
    } else {
      console.log(`[CodeEditor] Skipping update - no external change detected`)
    }
  },
  { deep: true }
)

// Watch for theme changes
watch(isDarkMode, (dark) => {
  if (editorInstance) {
    monaco.editor.setTheme(dark ? 'vs-dark' : 'vs')
  }
})

// Watch for language changes
watch(
  () => editorLanguage.value,
  (newLanguage) => {
    if (editorInstance && newLanguage) {
      const model = editorInstance.getModel()
      if (model) {
        monaco.editor.setModelLanguage(model, newLanguage)
      }
    }
  },
)

watch(
  () => props.readOnly,
  (readOnly) => {
    editorInstance?.updateOptions({ readOnly })
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
