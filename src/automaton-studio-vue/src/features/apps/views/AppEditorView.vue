<script setup lang="ts">
/**
 * AppEditorView.vue
 * Main Visual IDE for editing apps
 * Editor-first layout with on-demand AI assistant
 * Application opens as full-screen overlay
 */
import { ref, computed, onMounted, onUnmounted, watch, nextTick, defineAsyncComponent } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'

// Import components
import FileTree from '../components/FileTree.vue'
import AiAssistant from '../components/AiAssistant.vue'
import ApplicationOverlay from '../components/ApplicationOverlay.vue'
import PublishDialog from '../components/PublishDialog.vue'
import CreateFileDialog from '../components/CreateFileDialog.vue'
import StatusBar from '../components/StatusBar.vue'
import AppSettings from '../components/AppSettings.vue'
import AppVersionHistory from '../components/AppVersionHistory.vue'
import AppRuntimeLogs from '../components/AppRuntimeLogs.vue'
import BuildVisualWorkspace from '../components/BuildVisualWorkspace.vue'
import BackendServerFiles from '../components/BackendServerFiles.vue'
import AppEditorSideNav from '../components/AppEditorSideNav.vue'
import AppEditorWorkspaceBar from '../components/AppEditorWorkspaceBar.vue'

const CodeEditor = defineAsyncComponent(() => import('../components/CodeEditor.vue'))

import { useAppEditorStore } from '../stores/app-editor.store'
import { appsService } from '../services/apps.service'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import { aiConversationClient } from '@/shared/services/ai/ai-conversation-client'
import type { PublishAppRequest } from '../types/apps.types'
import { AppFileSystemScope } from '../types/apps.types'
import type { PreviewConsoleMessage, StepResultMessage } from '../types/console.types'
import { useSidebarState } from '@/shared/composables/useSidebarState'
import {
  executeUiAutomationAction,
  UiAutomationAction,
  type UiAutomationResult,
} from '../services/application-automation-bridge'
import { getOpenAppUrl, getPublishedAppUrl } from '../utils/public-app-url'

const route = useRoute()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()
const { closeMobile, setHovered } = useSidebarState()

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
const activeTab = ref<'build' | 'files' | 'versions' | 'services' | 'logs' | 'settings'>('build')
const appEditorNavGroups = [
  {
    label: 'Develop',
    items: [
      { key: 'build', label: 'App', icon: 'pi pi-sparkles' },
      { key: 'files', label: 'Files', icon: 'pi pi-file-edit' },
    ],
  },
  {
    label: 'Deploy',
    items: [
      { key: 'deployments', label: 'Deployments', icon: 'pi pi-cloud-upload', disabled: true },
      { key: 'published-app', label: 'Published App', icon: 'pi pi-globe', disabled: true },
      { key: 'domains', label: 'Domains', icon: 'pi pi-link', disabled: true },
    ],
  },
  {
    label: 'Manage',
    items: [
      { key: 'versions', label: 'Restore Points', icon: 'pi pi-history' },
      { key: 'storage', label: 'Storage', icon: 'pi pi-database', disabled: true },
      { key: 'variables', label: 'Variables', icon: 'pi pi-sliders-h', disabled: true },
      { key: 'services', label: 'Services', icon: 'pi pi-server', disabled: true },
      { key: 'logs', label: 'Logs', icon: 'pi pi-list' },
    ],
  },
  {
    items: [
      { key: 'settings', label: 'Settings', icon: 'pi pi-cog' },
    ],
  },
] as const

const activeNavItem = computed(() => {
  return activeTab.value
})
const applicationStatusTone = computed(() => {
  switch (editorStore.applicationState) {
    case 'Deployed':
      return 'success'
    case 'Source changed':
      return 'warn'
    case 'Build failed':
      return 'danger'
    case 'Runner offline':
      return 'contrast'
    default:
      return 'secondary'
  }
})

function formatUtcTimestamp(value?: string | null): string | null {
  if (!value) {
    return null
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return null
  }

  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(date)
}

const applicationRuntimeTimestampText = computed(() => {
  const deployedAt = formatUtcTimestamp(editorStore.applicationDeployedAtUtc)
  return deployedAt ? `Deployed at ${deployedAt}` : null
})

const applicationSourceTimestampText = computed(() => {
  const frontendModifiedAt = formatUtcTimestamp(editorStore.frontendSourceModifiedAtUtc)
  const backendModifiedAt = formatUtcTimestamp(editorStore.backendSourceModifiedAtUtc)

  if (frontendModifiedAt && backendModifiedAt) {
    return `Source updated at ${frontendModifiedAt} (frontend), ${backendModifiedAt} (backend)`
  }

  if (frontendModifiedAt) {
    return `Frontend source updated at ${frontendModifiedAt}`
  }

  if (backendModifiedAt) {
    return `Backend source updated at ${backendModifiedAt}`
  }

  return null
})

const applicationStatusMessage = computed(() => {
  switch (editorStore.applicationState) {
    case 'Deployed':
      return 'Running application is up to date.'
    case 'Source changed':
      return 'Source changed since last deploy. Deploy to update the running application.'
    case 'Runner offline':
      return 'The assigned runner is offline. Runtime updates and backend execution may be unavailable until it reconnects.'
    case 'Build failed':
      return editorStore.lastDeployError
    case 'Not deployed':
      return 'Deploy this app to create the running application.'
    default:
      return null
  }
})

watch(
  () => route.query.settings,
  (section) => {
    if (section === 'publishing') {
      activeTab.value = 'settings'
    }
  },
  { immediate: true },
)

// Ref to runtime logs component for programmatic refresh
const runtimeLogsRef = ref<{ loadLogs: () => void } | null>(null)
watch(activeTab, (tab) => {
  if (tab === 'logs') {
    nextTick(() => runtimeLogsRef.value?.loadLogs())
  }
})

// Right panel mode
const rightPanelMode = ref<'aiAssistant'>('aiAssistant')
const isRightPanelOpen = ref(false)

// Ref to version history component for manual refresh
const versionHistoryRef = ref<any>(null)

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
const DEFAULT_RIGHT_PANEL_SIZE = 24

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

function toggleAssistantPanel() {
  if (activeTab.value === 'services') {
    return
  }

  if (!isRightPanelOpen.value) {
    rightPanelMode.value = 'aiAssistant'
  }

  isRightPanelOpen.value = !isRightPanelOpen.value
}

function handleSideNavSelect(target: string) {
  switch (target) {
    case 'build':
    case 'files':
    case 'versions':
    case 'services':
    case 'logs':
    case 'settings':
      activeTab.value = target
      rightPanelMode.value = 'aiAssistant'
      return
    default:
      return
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
  closeMobile()
  setHovered(false)

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
    try {
      const response = await fetch(
        `${import.meta.env.VITE_API_BASE_URL}/api/Token/RefreshAccessToken`,
        {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({}),
        }
      )
      if (response.ok) {
        const data = await response.json()
        tokenStorage.setAccessToken(data.accessToken)
        token = data.accessToken
      }
    } catch {
      // Best-effort; fall through with the stale token
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
    const encodedPayload = token.split('.')[1]
    if (!encodedPayload) {
      return true
    }
    const payload = JSON.parse(atob(encodedPayload))
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
      editorStore.setApplicationWarning(`Missing application asset: ${label}${url}`)
    } else {
      editorStore.setApplicationWarning('Missing application asset: a referenced file could not be loaded')
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

function getOpenAppLabel(): string {
  if (editorStore.currentApp?.isPublished) {
    return 'Open Public App'
  }

  if (editorStore.currentApp?.hasDeployedFrontend) {
    return 'Open Runtime'
  }

  return 'Open App'
}

function getPrivateAppUrl(): string {
  return editorStore.currentApp ? getOpenAppUrl({
    id: editorStore.currentApp.id,
    isPublished: false,
    publicUrl: null,
  }) : ''
}

function getPublicAppUrl(): string {
  return editorStore.publicUrl ? getPublishedAppUrl(editorStore.publicUrl) : ''
}

async function getPrivateAppLaunchUrl(): Promise<string> {
  if (!editorStore.currentApp?.id) {
    return ''
  }

  const privateUrl = getPrivateAppUrl()
  if (!privateUrl) {
    return ''
  }

  const { runtimeToken } = await appsService.createRuntimeLaunch(editorStore.currentApp.id)
  const url = new URL(privateUrl)
  url.searchParams.set('token', runtimeToken)
  return url.toString()
}

async function copyUrl(label: string, url: string) {
  if (!url) {
    return
  }

  try {
    await navigator.clipboard.writeText(url)
    toast.add({
      severity: 'success',
      summary: 'Link Copied',
      detail: `${label}: ${url}`,
      life: 4000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Copy Failed',
      detail: 'Unable to copy the link to your clipboard',
      life: 4000,
    })
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
    const result = await editorStore.deployBackendComponent()
    if (result.backendDeploy.totalScripts === 0) {
      toast.add({ severity: 'info', summary: 'No Scripts', detail: 'No backend scripts found to deploy', life: 4000 })
    } else if (!result.backendDeploy.deployedCount) {
      toast.add({ severity: 'warn', summary: 'Deploy Failed', detail: result.backendDeploy.message, life: 5000 })
    } else {
      const installLogs = result.backendDeploy.installLogs
      const lastInstallLog = installLogs && installLogs.length > 0
        ? installLogs[installLogs.length - 1]
        : undefined
      const packagesNote = lastInstallLog
        ? ` — ${lastInstallLog}`
        : (result.backendDeploy.hasRequirements ? ' (packages installed)' : '')
      toast.add({
        severity: 'success',
        summary: 'Backend Deployed',
        detail: `${result.backendAction === 'updated' ? `Updated ${result.backendFilesSynced} backend file(s) from source and ` : ''}deployed ${result.backendDeploy.deployedCount} script(s) to ${result.backendDeploy.runnerCount} runner(s)${packagesNote}`,
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

async function handleDeployFrontend() {
  try {
    const result = await editorStore.deployFrontend()
    toast.add({
      severity: 'success',
      summary: 'Frontend Deployed',
      detail: result.frontendAction === 'updated'
        ? `Updated live/frontend from source/frontend with ${result.frontendFilesSynced} file(s).`
        : 'Preserved existing live/frontend because source/frontend is empty.',
      life: 4000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Deploy Failed',
      detail: err instanceof Error ? err.message : 'Failed to deploy frontend',
      life: 5000,
    })
  }
}

async function handleDeployApp() {
  try {
    const result = await editorStore.deployApp()
    const openAppUrl = editorStore.currentApp ? getOpenAppUrl(editorStore.currentApp) : ''
    const backendMessage = result.backendDeploy.totalScripts > 0
      ? ` Backend: ${result.backendDeploy.deployedCount}/${result.backendDeploy.totalScripts} scripts to ${result.backendDeploy.runnerCount} runner(s).`
      : ' No backend scripts found.'

      toast.add({
        severity: 'success',
        summary: 'App Deployed',
        detail: `${getOpenAppLabel()}: ${openAppUrl} Frontend ${result.frontendAction === 'updated' ? `updated from source (${result.frontendFilesSynced} file(s))` : 'preserved'}; backend ${result.backendAction === 'updated' ? `updated from source (${result.backendFilesSynced} file(s))` : 'preserved'}.${backendMessage}`,
        life: 7000,
      })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Deploy Failed',
      detail: err instanceof Error ? err.message : 'Failed to deploy app',
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
      detail: `App published at ${getPublishedAppUrl(request.publicUrl)}`,
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

function openPublishingSettings() {
  activeTab.value = 'settings'
  void router.replace({ query: { ...route.query, settings: 'publishing' } })
}

function openPublishedApp() {
  const publicUrl = getPublicAppUrl()
  if (!publicUrl) {
    return
  }

  window.open(publicUrl, '_blank', 'noopener')
}

function openPrivateApp() {
  void (async () => {
    try {
      const privateUrl = await getPrivateAppLaunchUrl()
      if (!privateUrl) {
        return
      }

      window.open(privateUrl, '_blank', 'noopener')
    } catch (err: unknown) {
      toast.add({
        severity: 'error',
        summary: 'Open Failed',
        detail: err instanceof Error ? err.message : 'Failed to open restricted runtime',
        life: 5000,
      })
    }
  })()
}

async function copyPrivateAppLink() {
  await copyUrl('Restricted runtime URL', getPrivateAppUrl())
}

async function copyPublicAppLink() {
  await copyUrl('Public URL', getPublicAppUrl())
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
    const normalizedPath = path.replace(/\\/g, '/')
    const scopedPath =
      normalizedPath.startsWith(`${AppFileSystemScope.Source}/`) ||
      normalizedPath.startsWith(`${AppFileSystemScope.Versions}/`)
        ? normalizedPath
        : `${editorStore.currentFileScope}/${normalizedPath}`

    await editorStore.createFile(scopedPath, content)
    showCreateFileDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `File "${scopedPath}" created`,
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

async function handleDeleteFolder(path: string) {
  confirm.require({
    message: `Are you sure you want to delete the folder "${path}" and all of its contents? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.deleteFolder(path)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `Folder "${path}" deleted`,
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to delete folder',
          life: 5000,
        })
      }
    },
  })
}

async function handleDeleteSelection(entries: Array<{ path: string; type: 'file' | 'folder' }>) {
  const normalizedEntries = entries
    .map((entry) => ({
      ...entry,
      normalizedPath: entry.path.replace(/\\/g, '/').replace(/\/+$/, ''),
    }))
    .sort((a, b) => {
      if (a.type !== b.type) {
        return a.type === 'folder' ? -1 : 1
      }

      return b.normalizedPath.length - a.normalizedPath.length
    })
    .filter((entry, index, list) => {
      return !list.some((candidate, candidateIndex) => {
        if (candidateIndex === index || candidate.type !== 'folder') {
          return false
        }

        return entry.normalizedPath === candidate.normalizedPath ||
          entry.normalizedPath.startsWith(`${candidate.normalizedPath}/`)
      })
    })

  if (normalizedEntries.length === 0) {
    return
  }

  const itemLabel = normalizedEntries.length === 1 ? 'item' : 'items'

  confirm.require({
    message: `Are you sure you want to delete ${normalizedEntries.length} selected ${itemLabel}? Any selected folders will be deleted with all of their contents. This action cannot be undone.`,
    header: 'Confirm Bulk Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        for (const entry of normalizedEntries) {
          if (entry.type === 'folder') {
            await editorStore.deleteFolder(entry.path)
          } else {
            await editorStore.deleteFile(entry.path)
          }
        }

        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `${normalizedEntries.length} selected ${itemLabel} deleted`,
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to delete selected items',
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

// Reload versions when the dedicated page becomes visible
watch(activeTab, async (tab) => {
  if (tab === 'versions' && versionHistoryRef.value?.loadVersions) {
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
      <AppEditorWorkspaceBar
        :active-tab="activeTab"
        :app-name="editorStore.currentApp.name"
        :is-editing-name="isEditingName"
        :editing-name="editingName"
        :is-published="editorStore.isPublished"
        :has-deployed-frontend="editorStore.hasDeployedFrontend"
        :can-open-private-app="editorStore.hasDeployedFrontend"
        :can-open-public-app="editorStore.isPublished"
        :has-runners="hasRunners"
        :selected-runner-id="selectedRunnerId"
        :is-saving="editorStore.isSaving"
        :has-unsaved-changes="editorStore.hasUnsavedChanges"
        :can-deploy-from-studio="editorStore.canDeployFromStudio"
        :can-deploy-frontend-from-studio="editorStore.canDeployFrontendFromStudio"
        :is-deploying-app="editorStore.isDeployingApp"
        :is-deploying-backend="editorStore.isDeployingBackend"
        :is-assistant-open="isRightPanelOpen"
        @back="handleBack"
        @update:editing-name="editingName = $event"
        @start-editing="startEditingName"
        @save-name="saveAppName"
        @cancel-editing="cancelEditingName"
        @update:selected-runner-id="selectedRunnerId = $event"
        @runners-loaded="(count) => hasRunners = count > 0"
        @save-all="handleSaveAll"
        @preview="editorStore.openApplicationOverlay()"
        @deploy-app="handleDeployApp"
        @deploy-frontend="handleDeployFrontend"
        @deploy-backend="handleDeployBackend"
        @publish="showPublishDialog = true"
        @open-private-app="openPrivateApp"
        @open-public-app="openPublishedApp"
        @copy-private-link="copyPrivateAppLink"
        @copy-public-link="copyPublicAppLink"
        @open-publishing-settings="openPublishingSettings"
        @unpublish="handleUnpublish"
        @toggle-assistant="toggleAssistantPanel"
      />
      <div v-if="activeTab === 'build'" class="application-status-banner">
        <span class="application-status-banner__label">Application</span>
        <Button
          :label="editorStore.applicationState"
          :severity="applicationStatusTone"
          size="small"
          text
        />
        <span
          v-if="applicationStatusMessage"
          class="application-status-banner__message"
        >
          {{ applicationStatusMessage }}
        </span>
        <span
          v-if="applicationRuntimeTimestampText"
          class="application-status-banner__meta"
        >
          {{ applicationRuntimeTimestampText }}
        </span>
        <span
          v-if="editorStore.sourceChangedSinceDeploy && applicationSourceTimestampText"
          class="application-status-banner__meta"
        >
          {{ applicationSourceTimestampText }}
        </span>
      </div>

      <div v-if="editorStore.showsLocalLiveBanner" class="mode-banner mode-banner-live">
        This app was deployed from local files. Source files are not available in Studio, so the Files tab can only show files that were uploaded into <code>source/</code>. Use <code>automaton pull</code> or upload source files to edit the app here.
      </div>

      <div class="app-editor-shell">
        <AppEditorSideNav
          :active-item="activeNavItem"
          :groups="appEditorNavGroups"
          @select="handleSideNavSelect"
        />

        <div class="app-editor-content">
          <div v-show="activeTab === 'logs'" class="panel-view">
            <AppRuntimeLogs
              ref="runtimeLogsRef"
              :app-id="resolvedAppId"
            />
          </div>

          <div v-show="activeTab === 'versions'" class="panel-view">
            <AppVersionHistory
              ref="versionHistoryRef"
              :app-id="resolvedAppId"
              title="Restore Points"
              full-page
              @version-restored="handleVersionRestored"
              @version-created="handleVersionCreated"
            />
          </div>

          <!-- Settings View -->
          <div v-show="activeTab === 'settings'" class="settings-view">
            <AppSettings />
          </div>

          <div v-if="activeTab === 'services'" class="backend-server-view">
            <BackendServerFiles :app-id="resolvedAppId" />
          </div>

          <!-- Build View - single shared layout for Visual (Build tab) and Files modes (Files tab) -->
          <div v-show="activeTab === 'build' || activeTab === 'files'" class="build-content-wrapper">
            <Splitter
              v-if="isRightPanelOpen"
              class="build-outer-splitter"
              layout="horizontal"
              :gutter-size="8"
              @resize="handleSplitterResizing"
              @resizeend="handleRightPanelSplitterResize"
            >
              <!-- Left: main workspace (visual or files) -->
              <SplitterPanel :size="100 - rightPanelSize" :min-size="40" class="splitter-panel">
                <div class="build-main-content" :class="{ 'no-pointer-events': isResizing }">
                  <!-- Visual mode workspace - shown in Build tab -->
                  <div v-show="activeTab === 'build'" class="build-visual-view">
                    <BuildVisualWorkspace :suspend-heavy-rendering="isVisualResizing" :runner-id="selectedRunnerId" />
                  </div>

                  <!-- Files mode: file tree + editor - shown in Files tab -->
                  <Splitter
                    v-show="activeTab === 'files'"
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
                          title="App Files"
                          :show-create="editorStore.currentScopeEditable"
                          :allow-delete="editorStore.currentScopeEditable"
                          empty-message="No app files yet"
                          @create-file="showCreateFileDialog = true"
                          @delete-file="handleDeleteFile"
                          @delete-folder="handleDeleteFolder"
                          @delete-selection="handleDeleteSelection"
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
                            :read-only="editorStore.activeFile?.isReadOnly"
                            @save="handleSaveFile"
                          />
                          <div v-else class="no-file-selected">
                            <i class="pi pi-file text-4xl text-color-secondary mb-2"></i>
                            <p>Select a file to edit</p>
                            <Button
                              v-if="editorStore.currentScopeEditable"
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

              <!-- Right: single shared AI Assistant panel -->
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
                  </div>
                  <div class="ai-sidebar">
                    <AiAssistant
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
                      @session-name-generated="handleSessionNameGenerated"
                      @initial-prompt-consumed="handleInitialPromptConsumed"
                    />
                  </div>
                </div>
              </SplitterPanel>
            </Splitter>

            <div v-else class="build-main-content build-main-content--full">
              <!-- Visual mode workspace - shown in Build tab -->
              <div v-show="activeTab === 'build'" class="build-visual-view">
                <BuildVisualWorkspace
                  :suspend-heavy-rendering="isVisualResizing"
                  :runner-id="selectedRunnerId"
                />
              </div>

              <!-- Files mode: file tree + editor - shown in Files tab -->
              <Splitter
                v-show="activeTab === 'files'"
                class="editor-layout"
                layout="horizontal"
                :gutter-size="8"
                @resize="handleSplitterResizing"
                @resizeend="handleSplitterResize"
              >
                <SplitterPanel :size="panelSizes[0]" :min-size="10" class="splitter-panel">
                  <div class="file-panel">
                    <FileTree
                      title="App Files"
                      :show-create="editorStore.currentScopeEditable"
                      :allow-delete="editorStore.currentScopeEditable"
                      empty-message="No app files yet"
                      @create-file="showCreateFileDialog = true"
                      @delete-file="handleDeleteFile"
                      @delete-folder="handleDeleteFolder"
                      @delete-selection="handleDeleteSelection"
                    />
                  </div>
                </SplitterPanel>

                <SplitterPanel :size="panelSizes[1]" :min-size="30" class="splitter-panel">
                  <div class="editor-panel">
                    <div class="editor-tabs-container">
                      <div
                        v-for="file in editorStore.openFiles"
                        :key="file.path"
                        class="editor-tab"
                        :class="{ active: editorStore.activeFilePath === file.path }"
                        @click="editorStore.activeFilePath = file.path"
                      >
                        <span class="tab-name">
                          <i
                            v-if="file.isBackendStep"
                            class="pi pi-server mr-1"
                            style="font-size: 0.7rem;"
                          ></i>
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

                    <div class="editor-content">
                      <CodeEditor
                        v-if="editorStore.activeFilePath"
                        :key="editorStore.activeFilePath"
                        :file-path="editorStore.activeFilePath"
                        :read-only="editorStore.activeFile?.isReadOnly"
                        @save="handleSaveFile"
                      />
                      <div v-else class="no-file-selected">
                        <i class="pi pi-file text-4xl text-color-secondary mb-2"></i>
                        <p>Select a file to edit</p>
                        <Button
                          v-if="editorStore.currentScopeEditable"
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

              <div class="assistant-collapsed-card">
                <div class="assistant-collapsed-copy">
                  <div class="assistant-collapsed-title">AI Assistant (Experimental)</div>
                  <div class="assistant-collapsed-text">
                    Bring your own AI provider to generate code, edit files, and help ship this app.
                  </div>
                </div>
                <div class="assistant-collapsed-actions">
                  <Button
                    label="Open Assistant"
                    icon="pi pi-sparkles"
                    @click="toggleAssistantPanel"
                  />
                  <Button
                    label="Configure AI"
                    icon="pi pi-cog"
                    severity="secondary"
                    outlined
                    @click="router.push('/ai-keys')"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Status Bar -->
      <StatusBar />

      <!-- Full-screen Preview Overlay -->
      <ApplicationOverlay v-if="editorStore.showApplicationOverlay" />
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
      :current-category="editorStore.currentApp?.category"
      :current-tags="editorStore.currentApp?.tags"
      :requires-auth="editorStore.currentApp?.requiresAuthentication"
      :is-forkable="editorStore.currentApp?.isForkable"
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

/* Right panel tabs */
.right-panel-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-width: 0;
}

.application-status-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.625rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  background: var(--p-surface-50);
}

.application-status-banner__label {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.application-status-banner__message {
  font-size: 0.9rem;
  color: var(--p-text-muted-color);
}

.application-status-banner__meta {
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  white-space: nowrap;
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

.backend-server-view {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.panel-view {
  flex: 1;
  display: flex;
  flex-direction: column;
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

.mode-banner {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  font-size: 0.85rem;
}

.mode-banner-source {
  background: color-mix(in srgb, var(--p-blue-100) 72%, white);
  color: var(--p-blue-900);
}

.mode-banner-live {
  background: color-mix(in srgb, var(--p-orange-100) 78%, white);
  color: var(--p-orange-900);
}

.app-editor-shell {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.app-editor-content {
  display: flex;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
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

.build-main-content--full {
  width: 100%;
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
  border-bottom: 1px solid color-mix(in srgb, var(--p-surface-300) 60%, transparent);
  overflow-x: auto;
  flex-shrink: 0;
}

.editor-tab {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.5rem 0.75rem;
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

.runtime-warning-banner {
  padding: 0.6rem 0.9rem;
  border-bottom: 1px solid var(--p-orange-300);
  background: color-mix(in srgb, var(--p-orange-100) 78%, white);
  color: var(--p-orange-900);
  font-size: 0.8rem;
  flex-shrink: 0;
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
}

.assistant-collapsed-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.9rem 1rem;
  border-top: 1px solid var(--p-surface-200);
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-cyan-500) 7%, transparent), transparent 70%),
    var(--p-surface-0);
  flex-shrink: 0;
}

.assistant-collapsed-copy {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.assistant-collapsed-title {
  font-weight: 700;
}

.assistant-collapsed-text {
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
  line-height: 1.5;
}

.assistant-collapsed-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

/* Dark mode */
:global(.app-dark) .app-editor-view {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .mode-banner {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .mode-banner-source {
  background: color-mix(in srgb, var(--p-blue-900) 58%, transparent);
  color: var(--p-blue-100);
}

:global(.app-dark) .mode-banner-live {
  background: color-mix(in srgb, var(--p-orange-900) 55%, transparent);
  color: var(--p-orange-100);
}

:global(.app-dark) .build-mode-toggle {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-panel {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .runtime-warning-banner {
  background: color-mix(in srgb, var(--p-orange-900) 55%, transparent);
  border-color: var(--p-orange-700);
  color: var(--p-orange-100);
}

:global(.app-dark) .assistant-collapsed-card {
  border-color: var(--p-surface-700);
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-cyan-500) 12%, transparent), transparent 70%),
    var(--p-surface-900);
}

:global(.app-dark) .editor-tab {
  border-color: var(--p-surface-700);
}

/* Splitter gutter styling */
:global(.p-splitter) {
  border-radius: 0 !important;
  overflow: hidden;
}

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

@media (max-width: 1100px) {
  .assistant-collapsed-card {
    flex-direction: column;
    align-items: flex-start;
  }
}

@media (max-width: 960px) {
  .app-editor-shell {
    flex-direction: column;
  }
}

</style>
