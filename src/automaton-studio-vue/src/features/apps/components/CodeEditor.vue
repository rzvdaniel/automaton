<script setup lang="ts">
/**
 * CodeEditor.vue
 * CodeMirror editor wrapper component for the App Editor.
 */
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { EditorView, keymap, lineNumbers, highlightActiveLineGutter, drawSelection, highlightActiveLine } from '@codemirror/view'
import { EditorState, Compartment } from '@codemirror/state'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { searchKeymap, highlightSelectionMatches } from '@codemirror/search'
import { bracketMatching, indentOnInput, foldGutter, syntaxHighlighting, defaultHighlightStyle } from '@codemirror/language'
import { css } from '@codemirror/lang-css'
import { html } from '@codemirror/lang-html'
import { javascript } from '@codemirror/lang-javascript'
import { json } from '@codemirror/lang-json'
import { markdown } from '@codemirror/lang-markdown'
import { python } from '@codemirror/lang-python'
import { xml } from '@codemirror/lang-xml'
import { yaml } from '@codemirror/lang-yaml'
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

interface EditorPosition {
  lineNumber: number
  column: number
}

const editorStore = useAppEditorStore()
const editorContainer = ref<HTMLElement>()
let editorInstance: EditorView | null = null
const readOnlyCompartment = new Compartment()
const languageCompartment = new Compartment()
const themeCompartment = new Compartment()

// Get the file from the store
const file = computed(() => {
  return editorStore.openFiles.find((f) => f.path === props.filePath)
})

const editorContent = computed(() => props.content ?? file.value?.content ?? '')
const supportedLanguages = new Set([
  'css',
  'html',
  'javascript',
  'markdown',
  'plaintext',
  'powershell',
  'python',
  'shell',
  'typescript',
  'xml',
  'yaml',
])

const editorLanguage = computed(() => {
  const requestedLanguage = props.language ?? file.value?.language ?? 'plaintext'
  return supportedLanguages.has(requestedLanguage) ? requestedLanguage : 'plaintext'
})
const isReadOnly = computed(() => props.readOnly ?? file.value?.isReadOnly ?? false)

// Detect if dark mode is enabled
const isDarkMode = computed(() => {
  return document.documentElement.classList.contains('app-dark')
})

function createLanguageExtension(language: string) {
  switch (language) {
    case 'css':
      return css()
    case 'html':
      return html()
    case 'javascript':
      return javascript()
    case 'json':
      return json()
    case 'markdown':
      return markdown()
    case 'python':
      return python()
    case 'typescript':
      return javascript({ typescript: true })
    case 'xml':
      return xml()
    case 'yaml':
      return yaml()
    default:
      return []
  }
}

function createThemeExtension(isDark: boolean) {
  return EditorView.theme(
    isDark
      ? {
          '&': {
            backgroundColor: '#1f2937',
            color: '#e5e7eb',
          },
          '.cm-gutters': {
            backgroundColor: '#111827',
            color: '#9ca3af',
            borderRight: '1px solid #374151',
          },
          '.cm-activeLine': {
            backgroundColor: 'rgba(148, 163, 184, 0.12)',
          },
          '.cm-activeLineGutter': {
            backgroundColor: 'rgba(148, 163, 184, 0.18)',
          },
          '.cm-selectionBackground, &.cm-focused .cm-selectionBackground, ::selection': {
            backgroundColor: 'rgba(96, 165, 250, 0.35)',
          },
          '.cm-cursor, .cm-dropCursor': {
            borderLeftColor: '#f9fafb',
          },
        }
      : {
          '&': {
            backgroundColor: '#ffffff',
            color: '#111827',
          },
          '.cm-gutters': {
            backgroundColor: '#f8fafc',
            color: '#64748b',
            borderRight: '1px solid #e2e8f0',
          },
          '.cm-activeLine': {
            backgroundColor: 'rgba(15, 23, 42, 0.04)',
          },
          '.cm-activeLineGutter': {
            backgroundColor: 'rgba(15, 23, 42, 0.06)',
          },
          '.cm-selectionBackground, &.cm-focused .cm-selectionBackground, ::selection': {
            backgroundColor: 'rgba(59, 130, 246, 0.2)',
          },
          '.cm-cursor, .cm-dropCursor': {
            borderLeftColor: '#111827',
          },
        },
    { dark: isDark },
  )
}

function getDocumentText(): string {
  return editorInstance?.state.doc.toString() ?? ''
}

function getDocumentOffset(position: EditorPosition): number {
  if (!editorInstance) return 0

  const line = Math.max(1, position.lineNumber)
  const lineInfo = editorInstance.state.doc.line(Math.min(line, editorInstance.state.doc.lines))
  return Math.min(lineInfo.from + Math.max(0, position.column - 1), lineInfo.to)
}

function getEditorPosition(): EditorPosition | null {
  if (!editorInstance) return null

  const offset = editorInstance.state.selection.main.head
  const line = editorInstance.state.doc.lineAt(offset)
  return {
    lineNumber: line.number,
    column: offset - line.from + 1,
  }
}

const saveKeymap = keymap.of([
  {
    key: 'Mod-s',
    preventDefault: true,
    run: () => {
      if (!isReadOnly.value) {
        emit('save')
      }
      return true
    },
  },
])

// Initialize CodeMirror
onMounted(() => {
  if (!editorContainer.value) return

  editorInstance = new EditorView({
    parent: editorContainer.value,
    state: EditorState.create({
      doc: editorContent.value,
      extensions: [
        lineNumbers(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        foldGutter(),
        bracketMatching(),
        indentOnInput(),
        highlightSelectionMatches(),
        highlightActiveLine(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        EditorState.tabSize.of(2),
        EditorView.lineWrapping,
        EditorView.theme({
          '&': {
            height: '100%',
            fontSize: '14px',
          },
          '.cm-scroller': {
            overflow: 'auto',
            fontFamily: 'Consolas, "Courier New", monospace',
          },
          '.cm-content': {
            minHeight: '100%',
          },
          '.cm-focused': {
            outline: 'none',
          },
        }),
        keymap.of([
          indentWithTab,
          ...defaultKeymap,
          ...historyKeymap,
          ...searchKeymap,
        ]),
        saveKeymap,
        languageCompartment.of(createLanguageExtension(editorLanguage.value)),
        readOnlyCompartment.of(EditorState.readOnly.of(isReadOnly.value)),
        themeCompartment.of(createThemeExtension(isDarkMode.value)),
        EditorView.updateListener.of((update) => {
          if (!update.docChanged || isReadOnly.value) return
          const content = update.state.doc.toString()
          editorStore.updateFileContent(props.filePath, content)
          emit('change', content)
        }),
      ],
    }),
  })
})

// Cleanup on unmount
onUnmounted(() => {
  editorInstance?.destroy()
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

    const currentValue = getDocumentText()
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
      const position = getEditorPosition()

      editorInstance.dispatch({
        changes: { from: 0, to: editorInstance.state.doc.length, insert: newContent },
      })

      if (position) {
        const anchor = getDocumentOffset(position)
        editorInstance.dispatch({
          selection: { anchor },
        })
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
    editorInstance.dispatch({
      effects: themeCompartment.reconfigure(createThemeExtension(dark)),
    })
  }
})

// Watch for language changes
watch(
  () => editorLanguage.value,
  (newLanguage) => {
    if (editorInstance && newLanguage) {
      editorInstance.dispatch({
        effects: languageCompartment.reconfigure(createLanguageExtension(newLanguage)),
      })
    }
  },
)

watch(
  () => isReadOnly.value,
  (readOnly) => {
    if (editorInstance) {
      editorInstance.dispatch({
        effects: readOnlyCompartment.reconfigure(EditorState.readOnly.of(readOnly)),
      })
    }
  },
)

// Expose methods for parent component
defineExpose({
  focus: () => editorInstance?.focus(),
  getValue: () => getDocumentText(),
  setValue: (value: string) =>
    editorInstance?.dispatch({
      changes: { from: 0, to: editorInstance.state.doc.length, insert: value },
    }),
  getPosition: () => getEditorPosition(),
  setPosition: (position: EditorPosition) => {
    if (!editorInstance) return
    editorInstance.dispatch({
      selection: { anchor: getDocumentOffset(position) },
    })
  },
  layout: () => editorInstance?.requestMeasure(),
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

.code-editor :deep(.cm-editor) {
  height: 100%;
}
</style>
