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
import BuildVisualWorkspace from '../components/BuildVisualWorkspace.vue'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'

import { useAppEditorStore } from '../stores/app-editor.store'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import type { PublishAppRequest } from '../types/apps.types'
import type { PreviewConsoleMessage, StepResultMessage } from '../types/console.types'
import { aiChatHistoryService } from '@/shared/services/api/ai-chat-history.service'
import type { AppChatStats } from '@/shared/types/ai.types'
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

const resolvedAppId = computed(() => editorStore.currentApp?.id || appId.value)

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

// Active view tab (code, settings, console, or logs)
const activeTab = ref<'code' | 'settings' | 'console' | 'logs'>('code')
const BUILD_VIEW_MODE_STORAGE_KEY = 'app-editor-build-view-mode'
type BuildViewMode = 'visual' | 'code'

function loadBuildViewMode(): BuildViewMode {
  try {
    const saved = localStorage.getItem(BUILD_VIEW_MODE_STORAGE_KEY)
    if (saved === 'visual' || saved === 'code') {
      return saved
    }
  } catch {
    // Fallback to default
  }
  return 'visual'
}

const buildViewMode = ref<BuildViewMode>(loadBuildViewMode())

watch(buildViewMode, (mode) => {
  try {
    localStorage.setItem(BUILD_VIEW_MODE_STORAGE_KEY, mode)
  } catch {
    // Ignore storage write errors
  }
})

// Active sub-tab in logs view
const activeLogsTab = ref<'build' | 'runtime'>('build')

// Ref to runtime logs component for programmatic refresh
const runtimeLogsRef = ref<{ loadLogs: () => void } | null>(null)
const buildLogsRef   = ref<{ reload: () => void } | null>(null)

// Refresh runtime logs whenever the user navigates to that view
watch([activeTab, activeLogsTab], ([tab, logsTab]) => {
  if (tab === 'logs' && logsTab === 'runtime') {
    nextTick(() => runtimeLogsRef.value?.loadLogs())
  }
})

// Right panel mode (aiAssistant or versionHistory)
const rightPanelMode = ref<'aiAssistant' | 'versionHistory'>('aiAssistant')

// Ref to version history component for manual refresh
const versionHistoryRef = ref<any>(null)

// AI Chat app statistics
const chatStats = ref<AppChatStats | null>(null)

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

// Handle splitter resize
function handleSplitterResize(event: any) {
  if (event && event.sizes && Array.isArray(event.sizes)) {
    // Normalize sizes to ensure they always add up to 100%
    const normalized = normalizeSizes(event.sizes)
    panelSizes.value = normalized
    savePanelSizes(normalized)
  }
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

  if (event.data?.type === 'previewConsoleEvent') {
    const message = event.data as PreviewConsoleMessage
    const level = message.data.level
    editorStore.addUiLogEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(message.data.timestamp),
      level,
      message: message.data.message,
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

// Handle AI tool execution for real-time file updates
async function handleAiToolExecuted(toolName: string, args: any, result?: any) {
  console.log('[AppEditor] AI tool executed:', { toolName, args, result })

  const uiActionNames = new Set<string>([
    UiAutomationAction.Navigate,
    UiAutomationAction.Click,
    UiAutomationAction.Type,
    UiAutomationAction.ReadText,
    UiAutomationAction.WaitFor,
    UiAutomationAction.Assert,
    UiAutomationAction.GetPageSnapshot,
    UiAutomationAction.GetConsoleLogs,
  ])

  if (uiActionNames.has(toolName)) {
    const envelope = result && typeof result === 'object' ? (result as Record<string, unknown>) : null
    const actionArgs =
      envelope && typeof envelope.arguments === 'object'
        ? (envelope.arguments as Record<string, unknown>)
        : ((args ?? {}) as Record<string, unknown>)

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

    return {
      success: actionResult.success,
      action: toolName,
      details: actionResult.details,
      value: actionResult.value,
      error: actionResult.error,
      logCount: actionResult.logs?.length,
      snapshotTitle: actionResult.snapshot?.title,
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
            <!-- Tab buttons for Build/Console/Settings -->
            <div class="view-tabs">
              <button
                class="view-tab"
                :class="{ active: activeTab === 'code' }"
                @click="activeTab = 'code'"
              >
                <i class="pi pi-code"></i>
                Build
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'console' }"
                @click="activeTab = 'console'"
              >
                <i class="pi pi-terminal"></i>
                Console
                <span v-if="editorStore.consoleEntries?.length > 0" class="console-badge">
                  {{ editorStore.consoleEntries.length }}
                </span>
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'logs' }"
                @click="activeTab = 'logs'"
              >
                <i class="pi pi-history"></i>
                Logs
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
          <!-- Runner Selector (only in code view) -->
          <template v-if="activeTab === 'code'">
            <div class="build-mode-toggle">
              <button
                class="view-tab"
                :class="{ active: buildViewMode === 'visual' }"
                @click="buildViewMode = 'visual'"
              >
                <i class="pi pi-eye"></i>
                Visual
              </button>
              <button
                class="view-tab"
                :class="{ active: buildViewMode === 'code' }"
                @click="buildViewMode = 'code'"
              >
                <i class="pi pi-file-edit"></i>
                Code
              </button>
            </div>
            <div class="flex align-items-center gap-2">
              <label class="text-sm text-color-secondary">Runner:</label>
              <RunnerSelector
                v-model="selectedRunnerId"
                :disabled="editorStore.isSaving"
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
              v-tooltip.bottom="'Open full-screen preview'"
              icon="pi pi-eye"
              label="Preview"
              outlined
              severity="secondary"
              @click="editorStore.openPreviewOverlay()"
            />
            <Button
              v-if="editorStore.hasUnsavedChanges && buildViewMode === 'code'"
              label="Save All"
              icon="pi pi-save"
              :loading="editorStore.isSaving"
              @click="handleSaveAll"
            />
          </template>
          <a
            v-if="editorStore.isPublished"
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
            v-if="editorStore.isPublished"
            label="Unpublish"
            icon="pi pi-cloud-download"
            outlined
            severity="secondary"
            @click="handleUnpublish"
          />
          <Button
            v-else
            label="Publish"
            icon="pi pi-cloud-upload"
            @click="showPublishDialog = true"
          />
          <div v-if="editorStore.hasBackendFolder" class="deploy-backend-wrapper">
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
            <span class="deploy-status-label" :class="{ 'deploy-status-label--hidden': !editorStore.deployStatusMessage }">
              {{ editorStore.deployStatusMessage ?? '\u00a0' }}
            </span>
          </div>
        </div>
      </div>

      <!-- Settings View -->
      <div v-show="activeTab === 'settings'" class="settings-view">
        <AppSettings />
      </div>

      <!-- Console View -->
      <div v-show="activeTab === 'console'" class="console-view">
        <ConsolePanel />
      </div>

      <!-- Logs View -->
      <div v-show="activeTab === 'logs'" class="logs-view">
        <div class="logs-view-tabs">
          <button
            class="logs-sub-tab"
            :class="{ active: activeLogsTab === 'build' }"
            @click="activeLogsTab = 'build'"
          >
            <i class="pi pi-sparkles"></i> AI Build Logs
          </button>
          <button
            class="logs-sub-tab"
            :class="{ active: activeLogsTab === 'runtime' }"
            @click="activeLogsTab = 'runtime'"
          >
            <i class="pi pi-server"></i> Runtime Logs
          </button>
        </div>
        <div class="logs-view-content">
          <AppBuildLogs v-if="activeLogsTab === 'build'" ref="buildLogsRef" :app-id="resolvedAppId" />
          <AppRuntimeLogs v-else ref="runtimeLogsRef" :app-id="resolvedAppId" />
        </div>
      </div>

      <!-- Build Visual View -->
      <div v-show="activeTab === 'code' && buildViewMode === 'visual'" class="build-visual-view">
        <Splitter
          class="editor-layout"
          layout="horizontal"
          :gutter-size="8"
          @resizeend="handleSplitterResize"
        >
          <SplitterPanel :size="75" :min-size="40" class="splitter-panel">
            <BuildVisualWorkspace />
          </SplitterPanel>
          <SplitterPanel :size="25" :min-size="15" class="splitter-panel">
            <div class="right-panel-container">
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
                  resource-type="WebApp"
                  :resource-id="resolvedAppId"
                  :runner-id="selectedRunnerId"
                  :initial-prompt="initialPrompt"
                  :initial-mode="initialMode"
                  :initial-model="initialModel"
                  :system-prompt="aiSystemPrompt"
                  :suggestions="aiSuggestions"
                  @tool-executed="handleAiToolExecuted"
                  @session-created="handleVersionCreated"
                  @chat-message-sent="refreshChatStats"
                  @session-name-generated="handleSessionNameGenerated"
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

      <!-- Build Code View - Main editor layout with resizable splitters -->
      <Splitter
        v-show="activeTab === 'code' && buildViewMode === 'code'"
        class="editor-layout"
        layout="horizontal"
        :gutter-size="8"
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

        <!-- Right Panel: AI Assistant or Version History -->
        <SplitterPanel :size="panelSizes[2]" :min-size="15" class="splitter-panel">
          <div class="right-panel-container">
            <!-- Right panel header tabs -->
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

            <!-- Right panel content -->
            <div class="ai-sidebar">
              <AiAssistant
                v-show="rightPanelMode === 'aiAssistant'"
                resource-type="WebApp"
                :resource-id="resolvedAppId"
                :runner-id="selectedRunnerId"
                :initial-prompt="initialPrompt"
                :initial-mode="initialMode"
                :initial-model="initialModel"
                :system-prompt="aiSystemPrompt"
                :suggestions="aiSuggestions"
                @tool-executed="handleAiToolExecuted"
                @session-created="handleVersionCreated"
                @chat-message-sent="refreshChatStats"
                @session-name-generated="handleSessionNameGenerated"
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

/* Deploy backend button + inline status */
.deploy-backend-wrapper {
  position: relative;
}

.deploy-status-label {
  position: absolute;
  top: 100%;
  left: 50%;
  transform: translateX(-50%);
  margin-top: 2px;
  font-size: 0.7rem;
  color: var(--p-surface-400);
  white-space: nowrap;
}

.deploy-status-label--hidden {
  visibility: hidden;
}

/* Console view container */
.console-view {
  flex: 1;
  overflow: hidden;
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

.build-mode-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.125rem;
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
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
}

/* Splitter panel styles */
.splitter-panel {
  display: flex !important;
  flex-direction: column !important;
  min-width: 0 !important;
  overflow: hidden !important;
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

/* Logs view */
.logs-view {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.logs-view-tabs {
  display: flex;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

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

.logs-view-content {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
</style>
