<script setup lang="ts">
/**
 * AppEditorView.vue
 * Main Visual IDE for editing apps
 * AI-first layout: Files | Editor | AI Assistant
 * Preview opens as full-screen overlay
 */
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'

// Import components
import FileTree from '../components/FileTree.vue'
import CodeEditor from '../components/CodeEditor.vue'
import AiAssistant from '../components/AiAssistant.vue'
import PreviewOverlay from '../components/PreviewOverlay.vue'
import PublishDialog from '../components/PublishDialog.vue'
import CreateFileDialog from '../components/CreateFileDialog.vue'
import StatusBar from '../components/StatusBar.vue'
import AppSettings from '../components/AppSettings.vue'
import ConsolePanel from '../components/ConsolePanel.vue'
import AppVersionHistory from '../components/AppVersionHistory.vue'
import AppBuildLogs from '../components/AppBuildLogs.vue'
import AppRuntimeLogs from '../components/AppRuntimeLogs.vue'
import AppUsageStats from '../components/AppUsageStats.vue'
import BuildVisualWorkspace from '../components/BuildVisualWorkspace.vue'
import AiConsoleView from '../components/AiConsoleView.vue'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'

import { useAppEditorStore } from '../stores/app-editor.store'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import { aiConversationClient } from '@/shared/services/ai/ai-conversation-client'
import type { PublishAppRequest } from '../types/apps.types'
import type { PreviewConsoleMessage, StepResultMessage } from '../types/console.types'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { AppChatStats, ChatSession, ChatMessage } from '@/shared/types/ai.types'
import {
  executeUiAutomationAction,
  UiAutomationAction,
  type UiAutomationResult,
} from '../services/preview-automation-bridge'

const route = useRoute()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

// Get app ID from route
const appId = computed(() => route.params.id as string)

// Always prefer route app ID to avoid leaking previous app chat context/memory.
// The store may still hold the previously opened app until loadApp finishes.
const resolvedAppId = computed(() => appId.value || editorStore.currentApp?.id || '')

// Capture initial prompt once at setup time — using ref so clearing the URL query
// via router.replace() doesn't wipe the value before AiAssistant.onMounted reads it.
const initialPrompt = ref(route.query.prompt as string | undefined)
const initialMode  = ref(route.query.mode  as string | undefined)
const initialModel = ref(route.query.model as string | undefined)

// Dialog visibility
const showPublishDialog = ref(false)
const showCreateFileDialog = ref(false)

// App name editing
const isEditingName = ref(false)
const editingName = ref('')
// Tracks whether app name has already been auto-updated from AI session name
const appNameAutoUpdated = ref(false)

// Runner selection for AI tool execution
const selectedRunnerId = ref<string | null>(null)
const hasRunners = ref<boolean | null>(null) // null = not yet loaded

// Active view tab
const activeTab = ref<'build' | 'code' | 'monitor' | 'settings'>('build')

// Active sub-tab in monitor view
const activeMonitorTab = ref<'console' | 'ai-console' | 'build-logs' | 'runtime-logs' | 'usage'>('console')

// Ref to runtime logs component for programmatic refresh
const runtimeLogsRef = ref<{ loadLogs: () => void } | null>(null)
const buildLogsRef   = ref<{ reload: () => void } | null>(null)

// Live AI chat mirror used by Logs -> AI Build Logs so it reflects in-progress streaming state.
const liveBuildSession = ref<ChatSession | null>(null)
const liveBuildMessages = ref<ChatMessage[]>([])
const liveBuildIsBusy = ref(false)

// Refresh runtime logs and usage whenever the user navigates to those monitor sub-tabs
watch([activeTab, activeMonitorTab], ([tab, monitorTab]) => {
  if (tab === 'monitor' && monitorTab === 'runtime-logs') {
    nextTick(() => runtimeLogsRef.value?.loadLogs())
  }
  if (tab === 'monitor' && monitorTab === 'usage') {
    usageRefreshTick.value += 1
  }
})

// Right panel mode (aiAssistant or versionHistory)
const rightPanelMode = ref<'aiAssistant' | 'versionHistory'>('aiAssistant')

// Ref to version history component for manual refresh
const versionHistoryRef = ref<any>(null)

// AI Chat app statistics
const chatStats = ref<AppChatStats | null>(null)
const usageRefreshTick = ref(0)

// AI Assistant configuration
const aiSystemPrompt = `You are an AI assistant helping to build a web application. 
You can create and modify HTML, CSS, and JavaScript code.
When a runner is attached, you can also execute backend steps for data processing.`

const aiSuggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

// Panel sizes (in percentages) - localStorage keys
const STORAGE_KEY = 'app-editor-panel-sizes'
const defaultSizes = [15, 60, 25] // 15% tree, 60% editor, 25% AI

// Right panel (AI/History) width - stored separately so it applies to both build modes
const RIGHT_PANEL_SIZE_KEY = 'app-editor-right-panel-size'
const DEFAULT_RIGHT_PANEL_SIZE = 25

function loadRightPanelSize(): number {
  try {
    const stored = localStorage.getItem(RIGHT_PANEL_SIZE_KEY)
    if (stored) {
      const n = Number(stored)
      if (n > 0 && n < 100) return n
    }
  } catch {
    // ignore
  }
  return DEFAULT_RIGHT_PANEL_SIZE
}

function saveRightPanelSize(size: number) {
  try {
    localStorage.setItem(RIGHT_PANEL_SIZE_KEY, String(size))
  } catch {
    // ignore
  }
}

const rightPanelSize = ref<number>(loadRightPanelSize())

function handleRightPanelSplitterResize(event: any) {
  isResizing.value = false
  isVisualResizing.value = false
  if (event?.sizes && Array.isArray(event.sizes) && event.sizes.length === 2) {
    rightPanelSize.value = event.sizes[1]
    saveRightPanelSize(event.sizes[1])
  }
}

// Load sizes from localStorage or use defaults
function loadPanelSizes(): number[] {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const sizes = JSON.parse(stored)
      if (Array.isArray(sizes) && sizes.length === 3 && sizes.every((s) => typeof s === 'number')) {
        return sizes
      }
    }
  } catch (e) {
    console.warn('Failed to load panel sizes from localStorage')
  }
  return defaultSizes
}

// Save sizes to localStorage
function savePanelSizes(sizes: number[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sizes))
  } catch (e) {
    console.warn('Failed to save panel sizes to localStorage')
  }
}

// Normalize sizes to always add up to 100%
function normalizeSizes(sizes: number[]): number[] {
  if (!Array.isArray(sizes) || sizes.length !== 3) {
    return defaultSizes
  }
  
  const total = sizes.reduce((sum, size) => sum + size, 0)
  if (total === 0) {
    return defaultSizes
  }
  
  // Normalize to 100% and round to avoid floating point precision issues
  return sizes.map((size) => Math.round((size / total) * 10000) / 100)
}

const panelSizes = ref<number[]>(normalizeSizes(loadPanelSizes()))
const isVisualResizing = ref(false)
// True during any splitter drag — applied as pointer-events:none on sibling panels
// to prevent nested splitter event conflicts that cause the drag to hang.
const isResizing = ref(false)

// Handle splitter resize
function handleSplitterResize(event: any) {
  if (event && event.sizes && Array.isArray(event.sizes)) {
    // Normalize sizes to ensure they always add up to 100%
    const normalized = normalizeSizes(event.sizes)
    panelSizes.value = normalized
    savePanelSizes(normalized)
  }
  isVisualResizing.value = false
  isResizing.value = false
}

function handleSplitterResizing() {
  isVisualResizing.value = true
  isResizing.value = true
}

// Active tab in editor
const activeTabIndex = ref(0)

// Get file index by path
function getFileTabIndex(path: string): number {
  return editorStore.openFiles.findIndex((f) => f.path === path)
}

// Update active tab when active file changes
watch(
  () => editorStore.activeFilePath,
  (path) => {
    if (path) {
      const index = getFileTabIndex(path)
      if (index !== -1) {
        activeTabIndex.value = index
      }
    }
  },
)

// Update active file when tab changes
watch(activeTabIndex, (index) => {
  const file = editorStore.openFiles[index]
  if (file && editorStore.activeFilePath !== file.path) {
    editorStore.activeFilePath = file.path
  }
})

// Existing file paths for create file dialog
const existingFilePaths = computed(() => {
  const paths: string[] = []
  function collectPaths(nodes: typeof editorStore.fileTree) {
    for (const node of nodes) {
      if (node.type === 'file') {
        paths.push(node.path)
      }
      if (node.children) {
        collectPaths(node.children)
      }
    }
  }
  collectPaths(editorStore.fileTree)
  return paths
})

// Load app on mount
onMounted(async () => {
  // Clear ?prompt from URL immediately so navigating back doesn't re-submit it
  if (route.query.prompt) {
    const { prompt: _, ...rest } = route.query
    router.replace({ ...route, query: rest })
  }

  if (appId.value) {
    try {
      await editorStore.loadApp(appId.value)
      // Load the execution runner ID from the app
      if (editorStore.currentApp?.executionRunnerId) {
        selectedRunnerId.value = editorStore.currentApp.executionRunnerId
      }
      
      // Load chat statistics for this app
      try {
        chatStats.value = await aiChatHistoryService.getAppChatStats(appId.value)
      } catch (err) {
        console.warn('Failed to load chat stats:', err)
        // Chat stats are optional, don't fail the load
      }
    } catch (err: unknown) {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: err instanceof Error ? err.message : 'Failed to load app',
        life: 5000,
      })
      router.push('/apps')
    }
  }
})

// Watch for runner selection changes and save to backend
watch(selectedRunnerId, async (newRunnerId) => {
  if (editorStore.currentApp && newRunnerId !== editorStore.currentApp.executionRunnerId) {
    try {
      await editorStore.updateAppRunner(newRunnerId)
    } catch (err: unknown) {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: err instanceof Error ? err.message : 'Failed to update runner',
        life: 5000,
      })
      // Revert on error
      if (editorStore.currentApp?.executionRunnerId) {
        selectedRunnerId.value = editorStore.currentApp.executionRunnerId
      }
    }
  }
})

// Cleanup on unmount
onUnmounted(() => {
  editorStore.reset()
})

// Format token count for display (K/M notation)
function formatTokenCount(tokens: number): string {
  if (tokens >= 1_000_000) return `${(tokens / 1_000_000).toFixed(1)}M`
  if (tokens >= 1_000) return `${(tokens / 1_000).toFixed(1)}K`
  return tokens.toString()
}

// Refresh chat statistics from the backend
async function refreshChatStats() {
  if (appId.value) {
    try {
      chatStats.value = await aiChatHistoryService.getAppChatStats(appId.value)
      usageRefreshTick.value += 1
    } catch (err) {
      console.warn('Failed to refresh chat stats:', err)
      // Non-critical error, don't interrupt user workflow
    }
  }
}

// Warn before leaving with unsaved changes
function handleBeforeUnload(e: BeforeUnloadEvent) {
  if (editorStore.hasUnsavedChanges) {
    e.preventDefault()
    e.returnValue = 'You have unsaved changes. Are you sure you want to leave?'
  }
}

// Token refresh request from preview iframe — reply with a guaranteed-fresh token.
// The handler is async so it can proactively refresh the JWT if it has expired or is about to.
async function handleTokenRequest(event: MessageEvent) {
  const { requestId } = event.data
  let token = tokenStorage.getAccessToken() ?? ''

  if (isTokenExpired(token)) {
    const refreshToken = tokenStorage.getRefreshToken()
    if (refreshToken) {
      try {
        const response = await fetch(
          `${import.meta.env.VITE_API_BASE_URL}/api/Token/RefreshAccessToken`,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token: refreshToken }),
          }
        )
        if (response.ok) {
          const data = await response.json()
          tokenStorage.setAccessToken(data.accessToken)
          tokenStorage.setRefreshToken(data.refreshToken)
          token = data.accessToken
        }
      } catch {
        // Best-effort; fall through with the stale token
      }
    }
  }

  ;(event.source as WindowProxy)?.postMessage(
    { type: 'tokenResponse', requestId, token },
    '*'
  )
}

function isTokenExpired(token: string): boolean {
  if (!token) return true
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    // Treat token as expired if it expires within the next 30 seconds
    return payload.exp * 1000 < Date.now() + 30_000
  } catch {
    return true
  }
}

// Handle preview iframe postMessage for console logging
function handlePreviewMessage(event: MessageEvent) {
  // Token refresh request from preview iframe — reply with a fresh access token
  if (event.data?.type === 'requestToken') {
    void handleTokenRequest(event)
    return
  }


  if (event.data?.type === 'automatonStepResult') {
    const message = event.data as StepResultMessage
    editorStore.addConsoleEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(message.data.timestamp),
      type: 'step-execution',
      stepName: message.data.stepName,
      inputs: message.data.inputs,
      result: message.data.result,
      duration: message.data.duration,
      source: message.data.source,
    })
  }

  if (event.data?.type === 'previewResourceError') {
    const url = event.data?.data?.url as string | undefined
    const tagName = event.data?.data?.tagName as string | undefined
    if (url) {
      const label = tagName ? `${tagName.toUpperCase()} ` : ''
      editorStore.setPreviewWarning(`Missing preview asset: ${label}${url}`)
    } else {
      editorStore.setPreviewWarning('Missing preview asset: a referenced file could not be loaded')
    }
  }

  // A new preview page started loading — clear stale logs from the previous session
  // immediately, before the app's own scripts have a chance to emit new entries.
  if (event.data?.type === 'previewConsoleClear') {
    editorStore.clearUiLogs()
    editorStore.addUiLogEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(),
      level: 'info',
      source: 'preview-runtime',
      message: '[Automaton] Preview restarted — log window reset',
    })
  }

  if (event.data?.type === 'previewConsoleEvent') {
    const message = event.data as PreviewConsoleMessage
    const level = message.data.level
    const content = message.data.message

    editorStore.addUiLogEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(message.data.timestamp),
      level,
      message: content,
      source: message.data.source,
    })
  }
}

// Add beforeunload listener, fullscreen class, and message listener
onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  window.addEventListener('message', handlePreviewMessage)
  // Add class to app-main for fullscreen editor mode
  document.querySelector('.app-main')?.classList.add('app-main--fullscreen')
})

onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  window.removeEventListener('message', handlePreviewMessage)
  // Remove fullscreen class
  document.querySelector('.app-main')?.classList.remove('app-main--fullscreen')
})

// Handle back navigation
function handleBack() {
  if (editorStore.hasUnsavedChanges) {
    confirm.require({
      message: 'You have unsaved changes. Are you sure you want to leave?',
      header: 'Unsaved Changes',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Leave',
      rejectLabel: 'Stay',
      accept: () => {
        router.push('/apps')
      },
    })
  } else {
    router.push('/apps')
  }
}

// Handle save all
async function handleSaveAll() {
  try {
    await editorStore.saveAllFiles()
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'All files saved successfully',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to save files',
      life: 5000,
    })
  }
}

// Handle save current file
async function handleSaveFile() {
  if (!editorStore.activeFilePath) return

  try {
    await editorStore.saveFile(editorStore.activeFilePath)
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to save file',
      life: 5000,
    })
  }
}

// Handle close tab
function handleCloseTab(event: Event, path: string) {
  event.stopPropagation()

  const file = editorStore.openFiles.find((f) => f.path === path)
  if (file?.isDirty) {
    confirm.require({
      message: `Save changes to "${path}" before closing?`,
      header: 'Unsaved Changes',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Save',
      rejectLabel: "Don't Save",
      accept: async () => {
        await editorStore.saveFile(path)
        editorStore.closeFile(path, true)
      },
      reject: () => {
        editorStore.closeFile(path, true)
      },
    })
  } else {
    editorStore.closeFile(path)
  }
}

// Handle deploy backend
async function handleDeployBackend() {
  try {
    const result = await editorStore.deployBackend()
    if (result.totalScripts === 0) {
      toast.add({ severity: 'info', summary: 'No Scripts', detail: 'No backend scripts found to deploy', life: 4000 })
    } else if (!result.deployedCount) {
      toast.add({ severity: 'warn', summary: 'Deploy Failed', detail: result.message, life: 5000 })
    } else {
      const lastInstallLog = result.installLogs?.at(-1)
      const packagesNote = lastInstallLog
        ? ` — ${lastInstallLog}`
        : (result.hasRequirements ? ' (packages installed)' : '')
      toast.add({
        severity: 'success',
        summary: 'Backend Deployed',
        detail: `Deployed ${result.deployedCount} script(s) to ${result.runnerCount} runner(s)${packagesNote}`,
        life: 4000,
      })
    }
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Deploy Failed',
      detail: err instanceof Error ? err.message : 'Failed to deploy backend',
      life: 5000,
    })
  }
}

// Handle publish
async function handlePublish(request: PublishAppRequest) {
  try {
    await editorStore.publishApp(request)
    showPublishDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: `App published at /web/${request.publicUrl}`,
      life: 5000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to publish app',
      life: 5000,
    })
  }
}

// Handle unpublish
async function handleUnpublish() {
  confirm.require({
    message: 'Are you sure you want to unpublish this app? It will no longer be accessible at the public URL.',
    header: 'Confirm Unpublish',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unpublish',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unpublishApp()
        toast.add({
          severity: 'success',
          summary: 'Unpublished',
          detail: 'App has been unpublished',
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to unpublish app',
          life: 5000,
        })
      }
    },
  })
}

// Handle create file
async function handleCreateFile(path: string, content: string) {
  try {
    await editorStore.createFile(path, content)
    showCreateFileDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `File "${path}" created`,
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to create file',
      life: 5000,
    })
  }
}

// Handle delete file
async function handleDeleteFile(path: string) {
  confirm.require({
    message: `Are you sure you want to delete "${path}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.deleteFile(path)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `File "${path}" deleted`,
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to delete file',
          life: 5000,
        })
      }
    },
  })
}

// Handle version created - refresh the history list
async function handleVersionCreated() {
  if (versionHistoryRef.value?.loadVersions) {
    await versionHistoryRef.value.loadVersions()
  }
  buildLogsRef.value?.reload()
}

function handleLiveChatStateUpdated(payload: {
  activeSession: ChatSession | null
  messages: ChatMessage[]
  isBusy: boolean
}) {
  liveBuildSession.value = payload.activeSession
  liveBuildMessages.value = payload.messages
  liveBuildIsBusy.value = payload.isBusy
}

// Handle version restored
async function handleVersionRestored(versionNumber: number) {
  try {
    // Reload app files from the restored version
    await editorStore.loadApp(appId.value)
    toast.add({
      severity: 'success',
      summary: 'Restored',
      detail: `App restored to version ${versionNumber}. Files have been reloaded.`,
      life: 5000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to reload app after restoration',
      life: 5000,
    })
  }
}

// Watch for version history tab activation - reload versions when tab becomes visible
watch(rightPanelMode, async (mode) => {
  if (mode === 'versionHistory' && versionHistoryRef.value?.loadVersions) {
    await versionHistoryRef.value.loadVersions()
  }
})

// Start editing app name
function startEditingName() {
  if (editorStore.currentApp) {
    editingName.value = editorStore.currentApp.name
    isEditingName.value = true
  }
}

// Cancel editing name
function cancelEditingName() {
  isEditingName.value = false
  editingName.value = ''
}

// Save app name
async function saveAppName() {
  isEditingName.value = false
  const newName = editingName.value.trim()
  editingName.value = ''
  if (!newName || newName === editorStore.currentApp?.name) return
  if (newName.length > 100) {
    toast.add({
      severity: 'warn',
      summary: 'Name too long',
      detail: 'App name must be 100 characters or fewer',
      life: 3000,
    })
    return
  }
  try {
    await editorStore.updateAppName(newName)
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to update app name',
      life: 3000,
    })
  }
}

// Handle AI-generated session name — use it to rename the app on first session
// (only when arriving from the "Generate App" flow via ?prompt= query param)
async function handleSessionNameGenerated(name: string) {
  if (!appNameAutoUpdated.value && initialPrompt.value?.trim()) {
    appNameAutoUpdated.value = true
    try {
      await editorStore.updateAppName(name)
    } catch {
      // Best-effort: silently ignore — the app name is non-critical at this point
    }
  }
}

function handleInitialPromptConsumed() {
  initialPrompt.value = undefined
}

// Handle AI tool execution for real-time file updates
async function handleAiToolExecuted(toolName: string, args: any, result?: any, toolCallId?: string) {
  console.log('[AppEditor] AI tool executed:', { toolName, args, result, toolCallId })

  const uiActionNames = new Set<string>(Object.values(UiAutomationAction))

  if (uiActionNames.has(toolName)) {
    const envelope = result && typeof result === 'object' ? (result as Record<string, unknown>) : null
    const actionArgs =
      envelope && typeof envelope.arguments === 'object'
        ? (envelope.arguments as Record<string, unknown>)
        : ((args ?? {}) as Record<string, unknown>)

    // New navigation starts a fresh diagnostic window.
    // This keeps AI context focused on current-run errors after preview refresh.
    if (toolName === UiAutomationAction.Navigate) {
      editorStore.clearUiLogs()
    }

    const actionResult: UiAutomationResult = await executeUiAutomationAction(
      toolName as UiAutomationAction,
      actionArgs,
      () => editorStore.uiLogEntries,
    )

    const level = actionResult.success ? 'info' : 'error'
    const summary = actionResult.success
      ? `[AI UI Test] ${toolName} OK`
      : `[AI UI Test] ${toolName} FAILED`

    const detailsParts = [
      actionResult.details,
      actionResult.value ? `Value: ${actionResult.value}` : undefined,
      actionResult.snapshot ? `Snapshot: "${actionResult.snapshot.title}"` : undefined,
      actionResult.logs ? `Logs: ${actionResult.logs.length}` : undefined,
      actionResult.error ? `Error: ${actionResult.error}` : undefined,
    ].filter(Boolean)

    editorStore.addUiLogEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(),
      level,
      source: 'preview-runtime',
      message: `${summary}${detailsParts.length > 0 ? ` — ${detailsParts.join(' | ')}` : ''}`,
    })

    if (toolCallId) {
      try {
        await aiConversationClient.reportUiActionResult(toolCallId, JSON.stringify(actionResult))
      } catch (reportError) {
        console.error('[AppEditor] Failed to report UI action result:', reportError)
      }
    }

    return {
      success: actionResult.success,
      action: toolName,
      details: actionResult.details,
      value: actionResult.value,
      error: actionResult.error,
      logCount: actionResult.logs?.length,
      snapshotTitle: actionResult.snapshot?.title,
      toolResult: actionResult,
    }
  }

  // propose_plan, write_instruction, update_instruction write files internally — just refresh the tree
  if (toolName === 'propose_plan' || toolName === 'write_instruction' || toolName === 'update_instruction') {
    await editorStore.refreshFileTree()
    return null
  }

  const fileModificationTools = [
    'write_file',
    'edit_file',
    'save_app_code',
    'delete_file',
  ]

  if (fileModificationTools.includes(toolName)) {
    console.log(`[AppEditor] File modification tool "${toolName}" detected, refreshing files...`)

    const affectedFiles: string[] = []

    if ((toolName === 'write_file' || toolName === 'edit_file') && args.path) {
      console.log(`[AppEditor] ${toolName} detected for path: ${args.path}`)
      affectedFiles.push(args.path)
    } else if (toolName === 'save_app_code') {
      console.log('[AppEditor] save_app_code detected, affecting index.html')
      affectedFiles.push('index.html')
    } else if (toolName === 'delete_file' && args.path) {
      await editorStore.refreshFileTree()
      return null
    }

    // Refresh files after AI update
    await editorStore.refreshFilesAfterAiUpdate(affectedFiles)

  }

  return null
}
</script>

<template>
  <div class="app-editor-view">
    <!-- Loading state -->
    <div v-if="editorStore.isLoading" class="loading-container">
      <ProgressSpinner />
      <p class="mt-3">Loading app...</p>
    </div>

    <!-- Main editor -->
    <template v-else-if="editorStore.currentApp">
      <!-- Header -->
      <div class="editor-header">
        <div class="header-left">
          <Button
            v-tooltip.bottom="'Back to Apps'"
            icon="pi pi-arrow-left"
            text
            rounded
            severity="secondary"
            @click="handleBack"
          />
          <div class="app-info">
            <div class="flex align-items-center gap-2">
              <i class="pi pi-globe" style="color: var(--p-primary-500)"></i>
              <span v-if="!isEditingName" class="app-name" @click="startEditingName">
                {{ editorStore.currentApp.name }}
              </span>
              <InputText
                v-else
                v-model="editingName"
                class="app-name-input"
                @blur="saveAppName"
                @keyup.enter="saveAppName"
                @keyup.escape="cancelEditingName"
              />
              <Tag
                v-if="editorStore.isPublished"
                value="Published"
                severity="success"
              />
              <Tag v-else value="Draft" severity="warn" />
            </div>
            <!-- Tab buttons for Build/Code/Monitor/Settings -->
            <div class="view-tabs">
              <button
                class="view-tab"
                :class="{ active: activeTab === 'build' }"
                @click="activeTab = 'build'"
              >
                <i class="pi pi-eye"></i>
                Build
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'code' }"
                @click="activeTab = 'code'"
              >
                <i class="pi pi-file-edit"></i>
                Code
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'monitor' }"
                @click="activeTab = 'monitor'"
              >
                <i class="pi pi-chart-bar"></i>
                Monitor
                <span v-if="editorStore.consoleEntries?.length > 0" class="console-badge">
                  {{ editorStore.consoleEntries.length }}
                </span>
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'settings' }"
                @click="activeTab = 'settings'"
              >
                <i class="pi pi-cog"></i>
                Settings
              </button>
            </div>
          </div>
        </div>

        <div class="header-right">
          <!-- Build and Code tabs: show action buttons -->
          <template v-if="activeTab === 'build' || activeTab === 'code'">
            <!-- Runner selector (hidden when confirmed no runners; shown while loading or when runners exist) -->
            <div v-if="hasRunners !== false" class="flex align-items-center gap-2">
              <span
                v-tooltip.bottom="'Choose which runner executes this app\'s backend'"
                class="text-sm text-color-secondary runner-label"
              >Runs on:</span>
              <RunnerSelector
                v-model="selectedRunnerId"
                :disabled="editorStore.isSaving"
                @runners-loaded="(count) => hasRunners = count > 0"
              />
            </div>

            <!-- Chat statistics display -->
            <div v-if="chatStats" class="flex align-items-center gap-2">
              <Button
                v-tooltip.bottom="`${chatStats.totalPromptsSubmitted} prompts submitted`"
                :label="`${chatStats.totalPromptsSubmitted}`"
                icon="pi pi-comments"
                text
                severity="secondary"
                size="small"
              />
              <Button
                v-tooltip.bottom="`${chatStats.totalTokens} tokens used (${chatStats.inputTokens} in / ${chatStats.outputTokens} out)`"
                :label="formatTokenCount(chatStats.totalTokens)"
                icon="pi pi-zap"
                text
                severity="secondary"
                size="small"
              />
            </div>

            <Button
              v-if="editorStore.hasUnsavedChanges && activeTab === 'code'"
              label="Save All"
              icon="pi pi-save"
              :loading="editorStore.isSaving"
              outlined
              severity="secondary"
              @click="handleSaveAll"
            />

            <Button
              v-tooltip.bottom="'Open full-screen preview'"
              icon="pi pi-eye"
              label="Preview"
              outlined
              severity="secondary"
              @click="editorStore.openPreviewOverlay()"
            />

            <!-- Publish CTA -->
            <template v-if="editorStore.isPublished">
              <a
                :href="`/web/${editorStore.publicUrl}`"
                target="_blank"
                rel="noopener noreferrer"
                class="no-underline"
              >
                <Button
                  v-tooltip.bottom="'Open published app in new tab'"
                  label="Open"
                  icon="pi pi-external-link"
                  outlined
                  severity="secondary"
                />
              </a>
              <Button
                label="Unpublish"
                icon="pi pi-cloud-download"
                outlined
                severity="secondary"
                @click="handleUnpublish"
              />
            </template>
            <Button
              v-else
              label="Publish App"
              icon="pi pi-cloud-upload"
              severity="success"
              @click="showPublishDialog = true"
            />

            <!-- Deploy Backend (only when app has backend scripts) -->
            <template v-if="editorStore.hasBackendFolder">
              <Button
                v-tooltip.bottom="'Push backend scripts to runner'"
                label="Deploy Backend"
                icon="pi pi-server"
                outlined
                severity="secondary"
                :loading="editorStore.isDeployingBackend"
                :disabled="editorStore.isDeployingBackend"
                @click="handleDeployBackend"
              />
              <span v-if="editorStore.deployStatusMessage" class="text-sm text-color-secondary">
                {{ editorStore.deployStatusMessage }}
              </span>
            </template>
          </template>
        </div>
      </div>

      <!-- Monitor View -->
      <div v-show="activeTab === 'monitor'" class="monitor-view">
        <div class="monitor-tabs">
          <button
            class="logs-sub-tab"
            :class="{ active: activeMonitorTab === 'console' }"
            @click="activeMonitorTab = 'console'"
          >
            <i class="pi pi-terminal"></i> Console
            <span v-if="editorStore.consoleEntries?.length > 0" class="console-badge">
              {{ editorStore.consoleEntries.length }}
            </span>
          </button>
          <button
            class="logs-sub-tab"
            :class="{ active: activeMonitorTab === 'ai-console' }"
            @click="activeMonitorTab = 'ai-console'"
          >
            <i class="pi pi-desktop"></i> AI Console
          </button>
          <button
            class="logs-sub-tab"
            :class="{ active: activeMonitorTab === 'build-logs' }"
            @click="activeMonitorTab = 'build-logs'"
          >
            <i class="pi pi-sparkles"></i> Build Logs
          </button>
          <button
            class="logs-sub-tab"
            :class="{ active: activeMonitorTab === 'runtime-logs' }"
            @click="activeMonitorTab = 'runtime-logs'"
          >
            <i class="pi pi-server"></i> Runtime Logs
          </button>
          <button
            class="logs-sub-tab"
            :class="{ active: activeMonitorTab === 'usage' }"
            @click="activeMonitorTab = 'usage'"
          >
            <i class="pi pi-chart-line"></i> Usage
          </button>
        </div>
        <div class="monitor-content">
          <ConsolePanel v-show="activeMonitorTab === 'console'" />
          <div v-show="activeMonitorTab === 'ai-console'" class="ai-console-view">
            <AiConsoleView
              :messages="liveBuildMessages"
              :session="liveBuildSession"
              :is-busy="liveBuildIsBusy"
            />
          </div>
          <AppBuildLogs
            v-show="activeMonitorTab === 'build-logs'"
            ref="buildLogsRef"
            :app-id="resolvedAppId"
            :live-active-session="liveBuildSession"
            :live-messages="liveBuildMessages"
            :live-is-busy="liveBuildIsBusy"
          />
          <AppRuntimeLogs
            v-show="activeMonitorTab === 'runtime-logs'"
            ref="runtimeLogsRef"
            :app-id="resolvedAppId"
          />
          <div v-show="activeMonitorTab === 'usage'" class="usage-view">
            <AppUsageStats :app-id="resolvedAppId" :refresh-token="usageRefreshTick" />
          </div>
        </div>
      </div>

      <!-- Settings View -->
      <div v-show="activeTab === 'settings'" class="settings-view">
        <AppSettings />
      </div>

      <!-- Build View - single shared layout for Visual (Build tab) and Code modes (Code tab) -->
      <div v-show="activeTab === 'build' || activeTab === 'code'" class="build-content-wrapper">
        <Splitter
          class="build-outer-splitter"
          layout="horizontal"
          :gutter-size="8"
          @resize="handleSplitterResizing"
          @resizeend="handleRightPanelSplitterResize"
        >
          <!-- Left: main workspace (visual or code) -->
          <SplitterPanel :size="100 - rightPanelSize" :min-size="40" class="splitter-panel">
            <div class="build-main-content" :class="{ 'no-pointer-events': isResizing }">
              <!-- Visual mode workspace - shown in Build tab -->
              <div v-show="activeTab === 'build'" class="build-visual-view">
                <BuildVisualWorkspace :suspend-heavy-rendering="isVisualResizing" :runner-id="selectedRunnerId" />
              </div>

              <!-- Code mode: file tree + editor - shown in Code tab -->
              <Splitter
                v-show="activeTab === 'code'"
                class="editor-layout"
                layout="horizontal"
                :gutter-size="8"
                @resize="handleSplitterResizing"
                @resizeend="handleSplitterResize"
              >
                <!-- Left Panel: File Tree -->
                <SplitterPanel :size="panelSizes[0]" :min-size="10" class="splitter-panel">
                  <div class="file-panel">
                    <FileTree
                      @create-file="showCreateFileDialog = true"
                      @delete-file="handleDeleteFile"
                    />
                  </div>
                </SplitterPanel>

                <!-- Center Panel: Code Editor -->
                <SplitterPanel :size="panelSizes[1]" :min-size="30" class="splitter-panel">
                  <div class="editor-panel">
                    <!-- File Tabs -->
                    <div class="editor-tabs-container">
                      <div
                        v-for="file in editorStore.openFiles"
                        :key="file.path"
                        class="editor-tab"
                        :class="{ active: editorStore.activeFilePath === file.path }"
                        @click="editorStore.activeFilePath = file.path"
                      >
                        <span class="tab-name">
                          <i v-if="file.isBackendStep" class="pi pi-server mr-1" style="font-size: 0.7rem;"></i>
                          {{ file.isBackendStep ? file.stepName : file.path }}
                          <span v-if="file.isDirty" class="dirty-indicator">*</span>
                        </span>
                        <Button
                          icon="pi pi-times"
                          text
                          rounded
                          size="small"
                          class="tab-close"
                          @click="(e: Event) => handleCloseTab(e, file.path)"
                        />
                      </div>
                    </div>

                    <!-- Editor Content -->
                    <div class="editor-content">
                      <CodeEditor
                        v-if="editorStore.activeFilePath"
                        :key="editorStore.activeFilePath"
                        :file-path="editorStore.activeFilePath"
                        @save="handleSaveFile"
                      />
                      <div v-else class="no-file-selected">
                        <i class="pi pi-file text-4xl text-color-secondary mb-2"></i>
                        <p>Select a file to edit</p>
                        <Button
                          label="Create File"
                          icon="pi pi-plus"
                          size="small"
                          outlined
                          @click="showCreateFileDialog = true"
                        />
                      </div>
                    </div>
                  </div>
                </SplitterPanel>
              </Splitter>
            </div>
          </SplitterPanel>

          <!-- Right: single shared AI Assistant + Version History panel -->
          <SplitterPanel :size="rightPanelSize" :min-size="15" class="splitter-panel">
            <div class="right-panel-container" :class="{ 'no-pointer-events': isResizing }">
              <div class="right-panel-header">
                <button
                  class="right-panel-tab"
                  :class="{ active: rightPanelMode === 'aiAssistant' }"
                  @click="rightPanelMode = 'aiAssistant'"
                >
                  <i class="pi pi-sparkles"></i>
                  <span>AI Assistant</span>
                </button>
                <button
                  class="right-panel-tab"
                  :class="{ active: rightPanelMode === 'versionHistory' }"
                  @click="rightPanelMode = 'versionHistory'"
                >
                  <i class="pi pi-history"></i>
                  <span>History</span>
                </button>
              </div>
              <div class="ai-sidebar">
                <AiAssistant
                  v-show="rightPanelMode === 'aiAssistant'"
                  :key="`ai-assistant-${resolvedAppId}`"
                  resource-type="WebApp"
                  :resource-id="resolvedAppId"
                  :runner-id="selectedRunnerId"
                  :initial-prompt="initialPrompt"
                  :initial-mode="initialMode"
                  :initial-model="initialModel"
                  :system-prompt="aiSystemPrompt"
                  :suggestions="aiSuggestions"
                  :ui-logs="editorStore.uiLogEntries"
                  :tool-execution-handler="handleAiToolExecuted"
                  @session-created="handleVersionCreated"
                  @chat-message-sent="refreshChatStats"
                  @session-name-generated="handleSessionNameGenerated"
                  @initial-prompt-consumed="handleInitialPromptConsumed"
                  @live-chat-state-updated="handleLiveChatStateUpdated"
                />
                <AppVersionHistory
                  v-show="rightPanelMode === 'versionHistory'"
                  ref="versionHistoryRef"
                  :app-id="resolvedAppId"
                  @version-restored="handleVersionRestored"
                  @version-created="handleVersionCreated"
                />
              </div>
            </div>
          </SplitterPanel>
        </Splitter>
      </div>

      <!-- Status Bar -->
      <StatusBar />

      <!-- Full-screen Preview Overlay -->
      <PreviewOverlay v-if="editorStore.showPreviewOverlay" />
    </template>

    <!-- Error state -->
    <div v-else class="error-container">
      <i class="pi pi-exclamation-triangle text-5xl text-yellow-500 mb-3"></i>
      <p v-if="editorStore.error">{{ editorStore.error }}</p>
      <p v-else>App not found</p>
      <Button label="Back to Apps" icon="pi pi-arrow-left" @click="router.push('/apps')" />
    </div>

    <!-- Dialogs -->
    <PublishDialog
      v-model:visible="showPublishDialog"
      :current-slug="editorStore.currentApp?.publicUrl"
      :requires-auth="editorStore.currentApp?.requiresAuthentication"
      :app-name="editorStore.currentApp?.name"
      @publish="handlePublish"
    />

    <CreateFileDialog
      v-model:visible="showCreateFileDialog"
      :existing-files="existingFilePaths"
      @create="handleCreateFile"
    />
  </div>
</template>

<style scoped>

.app-editor-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 64px);
  background-color: var(--p-surface-ground);
  overflow: hidden;
}

.loading-container,
.error-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
}

.editor-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.app-info {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

/* View tabs (Code/Settings) */
.view-tabs {
  display: flex;
  gap: 0.25rem;
}

.view-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.375rem 0.75rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: var(--p-border-radius);
  transition: all 0.15s ease;
}

.view-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.view-tab.active {
  background: var(--p-primary-100);
  color: var(--p-primary-600);
}

.view-tab i {
  font-size: 0.75rem;
}

/* Right panel tabs */
.right-panel-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-width: 0;
}

.build-outer-splitter {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  height: 100%;
  contain: layout style paint;
  will-change: auto;
}

.no-pointer-events {
  pointer-events: none;
  user-select: none;
}

.right-panel-header {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.right-panel-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.75rem 1rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: 0;
  border-bottom: 2px solid transparent;
  transition: all 0.15s ease;
}

.right-panel-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.right-panel-tab.active {
  border-bottom-color: var(--p-primary-500);
  color: var(--p-primary-500);
}

.right-panel-tab i {
  font-size: 0.8rem;
}

.ai-sidebar {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

/* Settings view container */
.settings-view {
  flex: 1;
  overflow: hidden;
}

.usage-view {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
}

/* Runner label in header */
.runner-label {
  white-space: nowrap;
}

/* Monitor view */
.monitor-view {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.monitor-tabs {
  display: flex;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.monitor-content {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

:global(.app-dark) .monitor-tabs {
  border-color: var(--p-surface-700);
}

/* Console badge in tab */
.console-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.25rem;
  height: 1.25rem;
  padding: 0 0.375rem;
  margin-left: 0.375rem;
  font-size: 0.6875rem;
  font-weight: 600;
  background: var(--p-primary-500);
  color: white;
  border-radius: 0.625rem;
}

.app-name {
  font-size: 1.125rem;
  font-weight: 600;
  cursor: pointer;
  color: var(--p-text-color);
}

.app-name:hover {
  text-decoration: underline;
}

.app-name-input {
  font-size: 1.125rem;
  font-weight: 600;
  width: 200px;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

/* AI Console (terminal) view container */
.ai-console-view {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.build-content-wrapper {
  display: flex;
  flex-direction: row;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.build-main-content {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.build-visual-view {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.editor-layout {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  height: 100%;
  contain: layout style paint;
  will-change: auto;
}

/* Splitter panel styles */
.splitter-panel {
  display: flex !important;
  flex-direction: column !important;
  min-width: 0 !important;
  overflow: hidden !important;
  will-change: flex-basis;
}

.file-panel {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--p-surface-200);
}

.editor-panel {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.editor-tabs-container {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--p-surface-200);
  overflow-x: auto;
  flex-shrink: 0;
}

.editor-tab {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.5rem 0.75rem;
  border-right: 1px solid var(--p-surface-200);
  cursor: pointer;
  white-space: nowrap;
  transition: background-color 0.2s;
}

.editor-tab:hover {
  background-color: var(--p-surface-hover);
}

.editor-tab.active {
  border-bottom: 2px solid var(--p-primary-500);
}

.tab-name {
  font-size: 0.875rem;
}

.dirty-indicator {
  color: var(--p-orange-500);
  margin-left: 0.25rem;
}

.tab-close {
  opacity: 0.5;
}

.tab-close:hover {
  opacity: 1;
}

.editor-content {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.no-file-selected {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--p-text-secondary);
}

.ai-sidebar {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  border-left: 1px solid var(--p-surface-200);
}

/* Dark mode */
:global(.app-dark) .app-editor-view {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .editor-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .build-mode-toggle {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-panel {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tabs-container {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tab {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .ai-sidebar {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .view-tab:hover {
  background: var(--p-surface-700);
}

:global(.app-dark) .view-tab.active {
  background: var(--p-primary-900);
  color: var(--p-primary-300);
}

/* Splitter gutter styling */
:global(.p-splitter-gutter) {
  background-color: var(--p-surface-200);
  transition: background-color 0.2s ease;
}

:global(.p-splitter-gutter:hover) {
  background-color: var(--p-primary-300);
}

:global(.app-dark) .p-splitter-gutter {
  background-color: var(--p-surface-700);
}

:global(.app-dark) .p-splitter-gutter:hover {
  background-color: var(--p-primary-600);
}

/* Logs sub-tab (reused inside Monitor view tabs) */
.logs-sub-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.6rem 1rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  transition: all 0.15s ease;
}

.logs-sub-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.logs-sub-tab.active {
  border-bottom-color: var(--p-primary-500);
  color: var(--p-primary-500);
}

</style>
