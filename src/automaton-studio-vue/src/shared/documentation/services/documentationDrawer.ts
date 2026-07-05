import { reactive } from 'vue'

interface DocumentationDrawerState {
  visible: boolean
  topicKey: string | null
  fallbackTitle: string
}

const state = reactive<DocumentationDrawerState>({
  visible: false,
  topicKey: null,
  fallbackTitle: 'Help',
})

export function useDocumentationDrawer() {
  function openDocumentation(topicKey: string, fallbackTitle = 'Help') {
    state.topicKey = topicKey
    state.fallbackTitle = fallbackTitle
    state.visible = true
  }

  function closeDocumentation() {
    state.visible = false
  }

  return {
    state,
    openDocumentation,
    closeDocumentation,
  }
}
