<script setup lang="ts">
/**
 * AppEditorView.vue
 * Main Visual IDE for editing apps
 * Editor-first layout with on-demand AI assistant
 */
import { ref, computed, onMounted, onUnmounted, watch, nextTick, defineAsyncComponent } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import ProgressSpinner from 'primevue/progressspinner'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'

// Import components
import FileTree from '../components/FileTree.vue'
import AiAssistant from '../components/AiAssistant.vue'
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
import AppActionContextBar from '../components/AppActionContextBar.vue'
import ActionSchedulingPanel from '../components/ActionSchedulingPanel.vue'

const CodeEditor = defineAsyncComponent(() => import('../components/CodeEditor.vue'))

import { useAppEditorStore } from '../stores/app-editor.store'
import { tokenStorage } from '@/shared/services/api/axios-instance'
import { aiConversationClient } from '@/shared/services/ai/ai-conversation-client'
import { backendRuntimeLogClient } from '../services/backend-runtime-log-client'
import { appsService } from '../services/apps.service'
import type { AppDeploymentTarget, PublishAppRequest } from '../types/apps.types'
import { AppFileSystemScope } from '../types/apps.types'
import type { StepResultMessage } from '../types/console.types'
import { useSidebarState } from '@/shared/composables/useSidebarState'
import {
  executeUiAutomationAction,
  UiAutomationAction,
  type UiAutomationResult,
} from '../services/application-automation-bridge'
import { getOpenAppUrl, getPublishedAppUrl } from '../utils/public-app-url'
import { resolveViewingTarget } from '../utils/viewing-target'
import { getDeployableTargets, getLiveTarget, getDeployTargetOptionLabel } from '../utils/deployment-target-actions'
import { isRemovableLegacyDefault } from '../utils/environment-display'

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
const selectedActionKey = computed(() => {
  const match = editorStore.activeFilePath?.replace(/\\/g, '/').match(/(?:^|\/)actions\/([a-z][a-z0-9-]{0,63})(?:\/|$)/)
  return match?.[1] ?? null
})

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
const selectedLogTargetId = ref<string | null>(null)
const deploymentTargets = ref<AppDeploymentTarget[]>([])
const selectedViewingTarget = ref<AppDeploymentTarget | null>(null)
const targetsRevision = ref(0)
const showDeployTargetDialog = ref(false)
const selectedDeployTargetSlug = ref<string | null>(null)

const deployableTargets = computed(() => getDeployableTargets(deploymentTargets.value))
const selectedDeployTarget = computed(() => deployableTargets.value.find((target) => target.slug === selectedDeployTargetSlug.value) ?? null)

// Active view tab
const activeTab = ref<'build' | 'files' | 'actions' | 'versions' | 'services' | 'logs' | 'settings'>('build')
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
      { key: 'actions', label: 'Actions', icon: 'pi pi-bolt' },
      { key: 'storage', label: 'Storage', icon: 'pi pi-database', disabled: true },
      { key: 'variables', label: 'Variables', icon: 'pi pi-sliders-h', disabled: true },
      { key: 'services', label: 'Services', icon: 'pi pi-server', disabled: true },
      { key: 'logs', label: 'Activity', icon: 'pi pi-list' },
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
let unsubscribeBackendRuntimeLogs: (() => void) | null = null
watch(activeTab, (tab) => {
  if (tab === 'logs') {
    nextTick(() => runtimeLogsRef.value?.loadLogs())
  }
})

// Right panel mode
const rightPanelMode = ref<'aiAssistant'>('aiAssistant')

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
const RIGHT_PANEL_OPEN_KEY = 'app-editor-right-panel-open'

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

function loadRightPanelOpen(): boolean {
  try {
    const stored = localStorage.getItem(RIGHT_PANEL_OPEN_KEY)
    if (stored === 'false') return false
    if (stored === 'true') return true
  } catch {
    // ignore
  }

  return true
}

function saveRightPanelOpen(isOpen: boolean) {
  try {
    localStorage.setItem(RIGHT_PANEL_OPEN_KEY, String(isOpen))
  } catch {
    // ignore
  }
}

const rightPanelSize = ref<number>(loadRightPanelSize())
const isRightPanelOpen = ref<boolean>(loadRightPanelOpen())

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
  saveRightPanelOpen(isRightPanelOpen.value)
}

function handleSideNavSelect(target: string) {
  switch (target) {
    case 'build':
    case 'files':
    case 'versions':
    case 'actions':
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

function getPendingChangeMarker(path: string): string | null {
  const changeType = editorStore.pendingDeployChanges[path]
  switch (changeType) {
    case 'modified':
      return '●'
    case 'added':
      return '+'
    case 'deleted':
      return '-'
    default:
      return null
  }
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
  unsubscribeBackendRuntimeLogs = backendRuntimeLogClient.onLog((event) => {
    if (event.appId !== resolvedAppId.value || (selectedLogTargetId.value && event.deploymentTargetId !== selectedLogTargetId.value)) {
      return
    }

    editorStore.addBackendRuntimeLogEntry(event)
  })

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
      await refreshDefaultTarget()
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

async function refreshDefaultTarget() {
  const targets = await appsService.listDeploymentTargets(appId.value)
  handleTargetsChanged(targets)
}

function handleTargetsChanged(targets: AppDeploymentTarget[]) {
  deploymentTargets.value = targets.filter((target) => !isRemovableLegacyDefault(target))
  if (!selectedRunnerId.value) {
    selectedRunnerId.value = (targets.find((target) => target.isDefault) ?? targets[0])?.runner?.id ?? null
  }
  void selectViewingTargetFromRoute()
}

async function selectViewingTargetFromRoute() {
  const requestedSlug = typeof route.query.target === 'string' ? route.query.target : ''
  const { target, usedFallback } = resolveViewingTarget(deploymentTargets.value, requestedSlug)
  if (!target) return

  await selectViewingTarget(target, usedFallback)
}

async function selectViewingTarget(target: AppDeploymentTarget, replaceRoute = false) {
  selectedViewingTarget.value = target
  selectedLogTargetId.value = target.id

  if (replaceRoute || route.query.target !== target.slug) {
    await router.replace({ query: { ...route.query, target: target.slug } })
  }

  try {
    if (target.runner) {
      await backendRuntimeLogClient.subscribe(target.appId, target.id)
    } else {
      await backendRuntimeLogClient.unsubscribe(target.appId)
    }
  } catch {
    toast.add({
      severity: 'warn',
      summary: 'Live logs unavailable',
      detail: 'The preview changed, but backend log streaming could not be updated.',
      life: 4000,
    })
  }
}

function handleSelectViewingTarget(target: AppDeploymentTarget) {
  void selectViewingTarget(target)
}

async function handleViewTargetLogs(target: AppDeploymentTarget) {
  await selectViewingTarget(target)
  activeTab.value = 'build'
  toast.add({ severity: 'info', summary: `${target.name} logs`, detail: `Backend logs now show ${target.slug}.`, life: 2500 })
}

watch(() => route.query.target, () => {
  if (deploymentTargets.value.length > 0) {
    void selectViewingTargetFromRoute()
  }
})

async function handleDeployTarget(target: AppDeploymentTarget) {
  await handleDeployApp(target.slug, target.name)
  targetsRevision.value += 1
}

function handleTopLevelDeploy() {
  const targets = deployableTargets.value
  if (targets.length === 1) {
    const target = targets[0]
    if (target) void handleDeployApp(target.slug, target.name)
    return
  }

  if (targets.length > 1) {
    selectedDeployTargetSlug.value = null
    showDeployTargetDialog.value = true
    return
  }

  toast.add({ severity: 'warn', summary: 'No deployable target', detail: 'Assign an online destination before deploying this App.', life: 4000 })
}

function confirmTopLevelDeploy() {
  const target = selectedDeployTarget.value
  if (!target) return
  showDeployTargetDialog.value = false
  void handleDeployApp(target.slug, target.name)
}

// Cleanup on unmount
onUnmounted(() => {
  void backendRuntimeLogClient.unsubscribe(appId.value)
  unsubscribeBackendRuntimeLogs?.()
  unsubscribeBackendRuntimeLogs = null
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

// Handle application iframe postMessage for console logging
function handleApplicationMessage(event: MessageEvent) {
  // Token refresh request from application iframe — reply with a fresh access token
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

  if (event.data?.type === 'applicationResourceError') {
    const url = event.data?.data?.url as string | undefined
    const tagName = event.data?.data?.tagName as string | undefined
    if (url) {
      const label = tagName ? `${tagName.toUpperCase()} ` : ''
      editorStore.setApplicationWarning(`Missing application asset: ${label}${url}`)
    } else {
      editorStore.setApplicationWarning('Missing application asset: a referenced file could not be loaded')
    }
  }

}

// Add beforeunload listener, fullscreen class, and message listener
onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  window.addEventListener('message', handleApplicationMessage)
  // Add class to app-main for fullscreen editor mode
  document.querySelector('.app-main')?.classList.add('app-main--fullscreen')
})

onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  window.removeEventListener('message', handleApplicationMessage)
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
    return 'Open Application'
  }

  return 'Open App'
}

function openAssistantPanel(): void {
  if (activeTab.value === 'services') {
    return
  }

  rightPanelMode.value = 'aiAssistant'
  isRightPanelOpen.value = true
  saveRightPanelOpen(true)
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
    if (!result.success) {
      toast.add({
        severity: result.requiresRetry ? 'info' : 'warn',
        summary: result.requiresRetry ? 'Preparing Managed Runtime' : 'Deploy Failed',
        detail: result.message ?? result.backendDeploy.message,
        life: 5000,
      })
    } else if (result.backendDeploy.totalScripts === 0) {
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

async function handleDeployApp(targetSlug?: string, targetName?: string) {
  try {
    const result = await editorStore.deployApp(targetSlug)
    if (!result.success) {
      toast.add({
        severity: result.requiresRetry ? 'info' : 'warn',
        summary: result.requiresRetry ? 'Preparing Managed Runtime' : 'Deploy Failed',
        detail: result.message ?? result.backendDeploy.message,
        life: 5000,
      })
      return
    }
    const openAppUrl = editorStore.currentApp ? getOpenAppUrl(editorStore.currentApp) : ''
    const backendMessage = result.backendDeploy.totalScripts > 0
      ? ` Backend: ${result.backendDeploy.deployedCount}/${result.backendDeploy.totalScripts} scripts to ${result.backendDeploy.runnerCount} runner(s).`
      : ' No backend scripts found.'

      toast.add({
        severity: 'success',
        summary: targetName ? `${targetName} deployed` : 'App Deployed',
        detail: `${targetName ? `Target ${targetSlug}.` : `${getOpenAppLabel()}: ${openAppUrl}`} Frontend ${result.frontendAction === 'updated' ? `updated from source (${result.frontendFilesSynced} file(s))` : 'preserved'}; backend ${result.backendAction === 'updated' ? `updated from source (${result.backendFilesSynced} file(s))` : 'preserved'}.${backendMessage}`,
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
  if (!getLiveTarget(deploymentTargets.value)) {
    toast.add({ severity: 'error', summary: 'Publish Live unavailable', detail: 'This App has no Live target.', life: 5000 })
    return
  }

  try {
    await editorStore.publishApp(request)
    showPublishDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: request.runtimeVisibility === 'public'
        ? `App published at ${getPublishedAppUrl(request.publicUrl)}`
        : request.catalogVisibility === 'listed'
          ? 'Private runtime listed with a media-only marketplace preview.'
          : 'Private runtime published as an unlisted media preview.',
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

    const actionResult: UiAutomationResult = await executeUiAutomationAction(
      toolName as UiAutomationAction,
      actionArgs,
    )

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
        :is-saving="editorStore.isSaving"
        :has-unsaved-changes="editorStore.hasUnsavedChanges"
        :can-deploy-from-studio="editorStore.canDeployFromStudio"
        :can-deploy-frontend-from-studio="editorStore.canDeployFrontendFromStudio"
        :can-deploy-backend-from-studio="editorStore.canDeployBackendFromStudio"
        :deploy-blocked-reason="editorStore.deployBlockedReason"
        :frontend-deploy-mode="editorStore.frontendDeployMode"
        :is-deploying-app="editorStore.isDeployingApp"
        :is-deploying-backend="editorStore.isDeployingBackend"
        :application-state="editorStore.applicationState"
        :source-changed-since-deploy="editorStore.sourceChangedSinceDeploy"
        :has-pending-frontend-changes="editorStore.hasPendingFrontendSourceChanges"
        :has-pending-backend-changes="editorStore.hasPendingBackendSourceChanges"
        @back="handleBack"
        @update:editing-name="editingName = $event"
        @start-editing="startEditingName"
        @save-name="saveAppName"
        @cancel-editing="cancelEditingName"
        @save-all="handleSaveAll"
        @deploy-app="handleTopLevelDeploy"
        @deploy-frontend="handleDeployFrontend"
        @deploy-backend="handleDeployBackend"
        @publish="showPublishDialog = true"
        @open-publishing-settings="openPublishingSettings"
        @unpublish="handleUnpublish"
      />
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
          <div v-if="activeTab === 'actions' && resolvedAppId" class="panel-view">
            <ActionSchedulingPanel :key="resolvedAppId" :app-id="resolvedAppId" />
          </div>

          <!-- Settings View -->
          <div v-show="activeTab === 'settings'" class="settings-view">
            <AppSettings
              :targets-revision="targetsRevision"
              @deploy-target="handleDeployTarget"
              @view-target-logs="handleViewTargetLogs"
              @targets-changed="handleTargetsChanged"
            />
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
                    <BuildVisualWorkspace
                      :suspend-heavy-rendering="isVisualResizing"
                      :runner-id="selectedRunnerId"
                      :targets="deploymentTargets"
                      :selected-target="selectedViewingTarget"
                      @select-target="handleSelectViewingTarget"
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
                    <!-- Left Panel: File Tree -->
                    <SplitterPanel :size="panelSizes[0]" :min-size="10" class="splitter-panel">
                      <div class="file-panel">
                        <FileTree
                          title="App Files"
                          :pending-changes="editorStore.pendingDeployChanges"
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
                              <span v-if="getPendingChangeMarker(file.path)" class="deploy-change-indicator">{{ getPendingChangeMarker(file.path) }}</span>
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

                        <AppActionContextBar
                          v-if="selectedActionKey && resolvedAppId"
                          :app-id="resolvedAppId"
                          :action-key="selectedActionKey"
                        />

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
                    <button class="right-panel-close" type="button" aria-label="Collapse AI Assistant" @click="toggleAssistantPanel">
                      <i class="pi pi-angle-right"></i>
                    </button>
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
                      :tool-execution-handler="handleAiToolExecuted"
                      @session-created="handleVersionCreated"
                      @session-name-generated="handleSessionNameGenerated"
                      @initial-prompt-consumed="handleInitialPromptConsumed"
                    />
                  </div>
                </div>
              </SplitterPanel>
            </Splitter>

            <div v-else class="build-main-content build-main-content--full build-main-content--collapsed">
              <div class="build-main-surface">
              <!-- Visual mode workspace - shown in Build tab -->
              <div v-show="activeTab === 'build'" class="build-visual-view">
                <BuildVisualWorkspace
                  :suspend-heavy-rendering="isVisualResizing"
                  :runner-id="selectedRunnerId"
                  :targets="deploymentTargets"
                  :selected-target="selectedViewingTarget"
                  @select-target="handleSelectViewingTarget"
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
                      :pending-changes="editorStore.pendingDeployChanges"
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
                          <span v-if="getPendingChangeMarker(file.path)" class="deploy-change-indicator">{{ getPendingChangeMarker(file.path) }}</span>
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
              </div>

              <button class="assistant-collapsed-rail" type="button" aria-label="Open AI Assistant" @click="openAssistantPanel">
                <i class="pi pi-sparkles"></i>
                <span>AI</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Status Bar -->
      <StatusBar />

    </template>

    <!-- Error state -->
    <div v-else class="error-container">
      <i class="pi pi-exclamation-triangle text-5xl text-yellow-500 mb-3"></i>
      <p v-if="editorStore.error">{{ editorStore.error }}</p>
      <p v-else>App not found</p>
      <Button label="Back to Apps" icon="pi pi-arrow-left" @click="router.push('/apps')" />
    </div>

    <!-- Dialogs -->
    <Dialog
      v-model:visible="showDeployTargetDialog"
      modal
      header="Deploy to environment"
      :style="{ width: '34rem', maxWidth: 'calc(100vw - 2rem)' }"
    >
      <p class="text-color-secondary mt-0">
        Choose where to deploy this App workspace. Deploying does not change which environment is Live.
      </p>
      <label for="top-level-deploy-target" class="font-semibold">Environment</label>
      <select
        id="top-level-deploy-target"
        v-model="selectedDeployTargetSlug"
        class="w-full mt-2 p-2 border-round surface-border"
      >
        <option :value="null" disabled>Select an environment</option>
        <option v-for="target in deployableTargets" :key="target.id" :value="target.slug">
          {{ getDeployTargetOptionLabel(target, selectedViewingTarget?.id) }}
        </option>
      </select>
      <p v-if="selectedViewingTarget" class="text-color-secondary text-sm mb-0 mt-3">
        Currently viewing: {{ selectedViewingTarget.name }}. Viewing does not choose an environment.
      </p>
      <template #footer>
        <Button label="Cancel" text @click="showDeployTargetDialog = false" />
        <Button
          :label="selectedDeployTarget ? `Deploy to ${selectedDeployTarget.name}` : 'Deploy to selected environment'"
          icon="pi pi-cloud-upload"
          :disabled="!selectedDeployTarget"
          @click="confirmTopLevelDeploy"
        />
      </template>
    </Dialog>

      <PublishDialog
      v-model:visible="showPublishDialog"
      :current-slug="editorStore.currentApp?.publicUrl"
      :current-category="editorStore.currentApp?.category"
      :current-tags="editorStore.currentApp?.tags"
      :requires-auth="editorStore.currentApp?.requiresAuthentication"
      :is-forkable="editorStore.currentApp?.isForkable"
      :current-runtime-visibility="editorStore.currentApp?.runtimeVisibility"
      :current-catalog-visibility="editorStore.currentApp?.catalogVisibility"
      :app-name="editorStore.currentApp?.name"
      :live-target-name="getLiveTarget(deploymentTargets)?.name"
      :live-target-slug="getLiveTarget(deploymentTargets)?.slug"
      :viewing-target-name="selectedViewingTarget?.name"
      :viewing-target-slug="selectedViewingTarget?.slug"
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
  align-items: center;
  justify-content: flex-start;
  gap: 0.75rem;
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

.right-panel-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border: none;
  border-radius: 0.5rem;
  background: transparent;
  color: var(--p-text-muted-color);
  cursor: pointer;
  transition: all 0.15s ease;
  margin-left: 0.5rem;
}

.right-panel-close:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
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

.build-main-content--collapsed {
  flex-direction: row;
}

.build-main-surface {
  display: flex;
  flex: 1;
  min-width: 0;
  min-height: 0;
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

.deploy-change-indicator {
  color: var(--p-orange-500);
  font-weight: 700;
  margin-left: 0.35rem;
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

.assistant-collapsed-rail {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.35rem;
  width: 2.1rem;
  min-width: 2.1rem;
  border: 0;
  border-left: 1px solid var(--p-surface-200);
  background:
    linear-gradient(180deg, color-mix(in srgb, var(--p-cyan-500) 9%, transparent), transparent 55%),
    var(--p-surface-0);
  color: var(--p-text-muted-color);
  cursor: pointer;
  transition: all 0.18s ease;
}

.assistant-collapsed-rail:hover {
  color: var(--p-text-color);
  background:
    linear-gradient(180deg, color-mix(in srgb, var(--p-cyan-500) 14%, transparent), transparent 55%),
    var(--p-surface-50);
}

.assistant-collapsed-rail i {
  font-size: 0.9rem;
}

.assistant-collapsed-rail span {
  writing-mode: vertical-rl;
  transform: rotate(180deg);
  font-size: 0.68rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  text-transform: uppercase;
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

:global(.app-dark) .right-panel-close:hover {
  background: var(--p-surface-800);
}

:global(.app-dark) .assistant-collapsed-rail {
  border-color: var(--p-surface-700);
  background:
    linear-gradient(180deg, color-mix(in srgb, var(--p-cyan-500) 14%, transparent), transparent 55%),
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
  .assistant-collapsed-rail {
    width: 2rem;
    min-width: 2rem;
  }
}

@media (max-width: 960px) {
  .app-editor-shell {
    flex-direction: column;
  }
}

</style>
