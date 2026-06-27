/**
 * App Editor Pinia store
 * Manages the Visual IDE state for app editing
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { appsService } from '../services/apps.service'
import { runnersService } from '@/features/runners/services/runners.service'
import { RunnerStatus } from '@/features/runners/types/runners.types'
import type {
  App,
  AppFileSystemScope,
  FileNode,
  OpenFile,
  PreviewMode,
  PublishAppRequest,
} from '../types/apps.types'
import {
  AppDevelopmentMode as DevelopmentMode,
  AppFileSystemScope as FileSystemScope,
  getLanguageFromExtension,
} from '../types/apps.types'
import type { ConsoleEntry } from '../types/console.types'
import type { UiLogEntry } from '../types/console.types'

export const UiLogFilter = {
  All: 'all',
  Errors: 'errors',
  Console: 'console',
  Runtime: 'runtime',
} as const

export type UiLogFilter = typeof UiLogFilter[keyof typeof UiLogFilter]

export const useAppEditorStore = defineStore('appEditor', () => {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || ''

  // ============================================
  // State
  // ============================================

  // Current app being edited
  const currentApp = ref<App | null>(null)
  const currentFileScope = ref<AppFileSystemScope>(FileSystemScope.Source)

  // File tree data
  const fileTree = ref<FileNode[]>([])

  // Open files in editor tabs
  const openFiles = ref<OpenFile[]>([])

  // Currently active file path (selected tab)
  const activeFilePath = ref<string | null>(null)

  // Preview URL for iframe (HTTP-based preview)
  const previewUrl = ref('')
  const sourceChangedSinceDeploy = ref(false)
  const lastDeployError = ref<string | null>(null)
  const assignedRunnerOnline = ref<boolean | null>(null)

  // Preview warning message (missing assets, etc.)
  const previewWarning = ref<string | null>(null)

  // Preview mode (desktop, tablet, mobile)
  const previewMode = ref<PreviewMode>('desktop')

  // Loading states
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isLoadingPreview = ref(false)
  const isDeployingApp = ref(false)
  const isDeployingBackend = ref(false)
  const deployStatusMessage = ref<string | null>(null)
  const deployAppStatusMessage = ref<string | null>(null)

  // Error state
  const error = ref<string | null>(null)

  // Preview overlay visibility (full-screen preview)
  const showPreviewOverlay = ref(false)

  // Console entries for step execution results
  const consoleEntries = ref<ConsoleEntry[]>([])
  const uiLogEntries = ref<UiLogEntry[]>([])
  const uiLogFilter = ref<UiLogFilter>(UiLogFilter.All)
  const uiLogFingerprintWindow = ref<string[]>([])

  const UI_LOG_FINGERPRINT_WINDOW_SIZE = 120

  function buildUiLogFingerprint(entry: UiLogEntry): string {
    return `${entry.source}|${entry.level}|${entry.message.trim()}`
  }

  // ============================================
  // Computed
  // ============================================

  /**
   * Currently active file object
   */
  const activeFile = computed(() => {
    if (!activeFilePath.value) return null
    return openFiles.value.find((f) => f.path === activeFilePath.value) || null
  })

  /**
   * Check if there are any unsaved changes
   */
  const hasUnsavedChanges = computed(() => {
    return openFiles.value.some((f) => f.isDirty)
  })

  /**
   * Get unsaved files count
   */
  const unsavedFilesCount = computed(() => {
    return openFiles.value.filter((f) => f.isDirty).length
  })

  /**
   * Main HTML file (index.html)
   */
  const mainHtmlFile = computed(() => {
    return openFiles.value.find((f) => f.scope === FileSystemScope.Source && f.path === 'source/frontend/index.html')
  })

  /**
   * Check if app is published
   */
  const isPublished = computed(() => currentApp.value?.isPublished ?? false)

  /**
   * Public URL slug
   */
  const publicUrl = computed(() => currentApp.value?.publicUrl ?? '')
  const hasApplicationRuntime = computed(() => {
    if (!currentApp.value) {
      return false
    }

    return currentApp.value.frontend.runtimeAvailable || currentApp.value.hasDeployedFrontend
  })
  const hasDeployedFrontend = computed(() => hasApplicationRuntime.value)
  const applicationDeployedAtUtc = computed(() => {
    const frontendDeployed = currentApp.value?.frontend.runtimeDeployedAtUtc ?? null
    const backendDeployed = currentApp.value?.backend.runtimeDeployedAtUtc ?? null
    return getLatestIsoTimestamp(frontendDeployed, backendDeployed)
  })
  const frontendSourceModifiedAtUtc = computed(() => currentApp.value?.frontend.sourceModifiedAtUtc ?? null)
  const backendSourceModifiedAtUtc = computed(() => currentApp.value?.backend.sourceModifiedAtUtc ?? null)

  /**
   * Check if app has a backend/ folder (deploy button should be visible)
   */
  const hasBackendFolder = computed(() =>
    fileTree.value.some((node) =>
      node.type === 'folder' &&
      node.name === 'source' &&
      node.children?.some((child) => child.type === 'folder' && child.name === 'backend'),
    ),
  )

  const currentScopeEditable = computed(() => currentFileScope.value !== FileSystemScope.Versions)
  const defaultEditorScope = computed<AppFileSystemScope>(() => {
    return FileSystemScope.Source
  })
  const canDeployFrontendFromStudio = computed(() => currentApp.value?.frontend.sourceAvailable ?? false)
  const canDeployBackendFromStudio = computed(() => currentApp.value?.backend.sourceAvailable ?? false)
  const canDeployFromStudio = computed(() => canDeployFrontendFromStudio.value || canDeployBackendFromStudio.value)
  const runnerOffline = computed(() => !!currentApp.value?.executionRunnerId && assignedRunnerOnline.value === false)
  const applicationState = computed(() => {
    if (lastDeployError.value) return 'Build failed'
    if (runnerOffline.value) return 'Runner offline'
    if (sourceChangedSinceDeploy.value) return 'Source changed'
    if (hasApplicationRuntime.value) return 'Deployed'
    return 'Not deployed'
  })
  const isLocalManagedApp = computed(() => currentApp.value?.developmentMode === DevelopmentMode.Local)
  const showsLocalLiveBanner = computed(() => isLocalManagedApp.value && !currentApp.value?.frontend.sourceAvailable)
  const showsSourceDeployBanner = computed(() => currentApp.value?.developmentMode === DevelopmentMode.Studio || canDeployFromStudio.value)
  const editorModeNoticeLabel = computed(() => {
    if (showsLocalLiveBanner.value) {
      return 'Source files unavailable'
    }

    if (showsSourceDeployBanner.value) {
      return 'Editing source files'
    }

    return null
  })
  const editorModeNoticeText = computed(() => {
    if (showsLocalLiveBanner.value) {
      return 'This app was deployed from local files. Source files are not available in Studio, so the Files tab only shows uploaded source content. Use automaton pull or upload source files to edit the app here.'
    }

    if (!showsSourceDeployBanner.value || !currentApp.value) {
      return null
    }

    return `You are editing source files. Frontend source ${currentApp.value.frontend.sourceAvailable ? 'will update live/frontend' : 'is empty, so live/frontend will be preserved'}. Backend source ${currentApp.value.backend.sourceAvailable ? 'will update live/backend' : 'is empty, so live/backend will be preserved'}.`
  })

  function toUtcMillis(value?: string | null): number | null {
    if (!value) return null
    const millis = Date.parse(value)
    return Number.isFinite(millis) ? millis : null
  }

  function getLatestIsoTimestamp(...values: Array<string | null | undefined>): string | null {
    let latestValue: string | null = null
    let latestMillis: number | null = null

    for (const value of values) {
      const millis = toUtcMillis(value)
      if (millis == null) continue
      if (latestMillis == null || millis > latestMillis) {
        latestMillis = millis
        latestValue = value ?? null
      }
    }

    return latestValue
  }

  function hasPersistedSourceDivergence(app: App | null): boolean {
    if (!app) return false

    const frontendChangedAfterDeploy =
      app.frontend.sourceAvailable &&
      compareTimestamps(app.frontend.sourceModifiedAtUtc, app.frontend.runtimeDeployedAtUtc) > 0

    const backendChangedAfterDeploy =
      app.backend.sourceAvailable &&
      compareTimestamps(app.backend.sourceModifiedAtUtc, app.backend.runtimeDeployedAtUtc) > 0

    return frontendChangedAfterDeploy || backendChangedAfterDeploy
  }

  function compareTimestamps(left?: string | null, right?: string | null): number {
    const leftMillis = toUtcMillis(left)
    const rightMillis = toUtcMillis(right)

    if (leftMillis == null && rightMillis == null) return 0
    if (leftMillis == null) return -1
    if (rightMillis == null) return 1
    return leftMillis - rightMillis
  }

  function syncSourceChangeStateFromMetadata(): void {
    if (!hasUnsavedChanges.value) {
      sourceChangedSinceDeploy.value = hasPersistedSourceDivergence(currentApp.value)
    }

    previewWarning.value = getPreviewWarningMessage()
  }

  function getPreviewWarningMessage(): string | null {
    if (!currentApp.value) {
      return null
    }

    if (!hasApplicationRuntime.value) {
      return sourceChangedSinceDeploy.value
        ? 'Source has changed. Deploy to update the running application.'
        : 'Application has not been deployed yet.'
    }

    if (sourceChangedSinceDeploy.value) {
      return 'Source has changed. Deploy to update the running application.'
    }

    if (lastDeployError.value) {
      return lastDeployError.value
    }

    return null
  }

  function getScopePrefix(scope: AppFileSystemScope): string {
    return `${scope}/`
  }

  function getScopeFromScopedPath(path: string): AppFileSystemScope {
    const normalizedPath = path.replace(/\\/g, '/')
    if (normalizedPath.startsWith(getScopePrefix(FileSystemScope.Source))) return FileSystemScope.Source
    if (normalizedPath.startsWith(getScopePrefix(FileSystemScope.Versions))) return FileSystemScope.Versions
    return currentFileScope.value
  }

  function getRelativePathFromScopedPath(path: string, scope: AppFileSystemScope): string {
    const normalizedPath = path.replace(/\\/g, '/')
    const prefix = getScopePrefix(scope)
    return normalizedPath.startsWith(prefix) ? normalizedPath.slice(prefix.length) : normalizedPath
  }

  function isEditableScope(scope: AppFileSystemScope): boolean {
    return scope === FileSystemScope.Source
  }

  async function loadUnifiedFileTree(appId: string): Promise<void> {
    fileTree.value = await appsService.getRootFileTree(appId)
  }

  async function refreshAssignedRunnerStatus(): Promise<void> {
    if (!currentApp.value?.executionRunnerId) {
      assignedRunnerOnline.value = null
      return
    }

    try {
      const runners = await runnersService.list()
      const assignedRunner = runners.find((runner) => runner.id === currentApp.value?.executionRunnerId)
      assignedRunnerOnline.value = assignedRunner ? assignedRunner.status === RunnerStatus.Online : null
    } catch {
      assignedRunnerOnline.value = null
    }
  }

  async function refreshCurrentAppMetadata(): Promise<void> {
    if (!currentApp.value) {
      return
    }

    currentApp.value = await appsService.get(currentApp.value.id)
    await refreshAssignedRunnerStatus()
    syncSourceChangeStateFromMetadata()
  }

  // ============================================
  // Actions
  // ============================================

  /**
   * Load an app for editing
   */
  async function loadApp(appId: string): Promise<void> {
    isLoading.value = true
    error.value = null

    try {
      // Load app metadata
      currentApp.value = await appsService.get(appId)
      await refreshAssignedRunnerStatus()

      // Load file tree
      currentFileScope.value = defaultEditorScope.value
      await loadUnifiedFileTree(appId)

      // Reset editor state
      openFiles.value = []
      activeFilePath.value = null

      // Auto-open index.html if it exists
      const preferredFile =
        findFileInTree(fileTree.value, 'source/frontend/index.html') ??
        findFileInTree(fileTree.value, 'source/index.html')
      if (preferredFile) {
        await openFile(preferredFile.path)
      }

      // Load preview
      await refreshPreview()
      syncSourceChangeStateFromMetadata()
      lastDeployError.value = null
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    } finally {
      isLoading.value = false
    }
  }

  /**
   * Find a file in the tree by path
   */
  function findFileInTree(nodes: FileNode[], path: string): FileNode | null {
    for (const node of nodes) {
      if (node.path === path) return node
      if (node.children) {
        const found = findFileInTree(node.children, path)
        if (found) return found
      }
    }
    return null
  }

  /**
   * Open a file in the editor
   */
  async function openFile(filePath: string): Promise<void> {
    if (!currentApp.value) return

    const fileScope = getScopeFromScopedPath(filePath)
    const relativePath = getRelativePathFromScopedPath(filePath, fileScope)
    currentFileScope.value = fileScope

    // Check if already open
    const existing = openFiles.value.find((f) => f.path === filePath && f.scope === fileScope)
    if (existing) {
      activeFilePath.value = filePath
      return
    }

    try {
      // Load file content
      const response =
        filePath === relativePath
          ? await appsService.readRootFile(currentApp.value.id, relativePath)
          : fileScope === FileSystemScope.Source
            ? await appsService.readFile(currentApp.value.id, relativePath)
            : await appsService.readScopedFile(currentApp.value.id, fileScope, relativePath)

      if (!response.success) {
        throw new Error(`Failed to load file: ${filePath}`)
      }

      const content = response.content || ''

      // Create open file entry
      const openFile: OpenFile = {
        path: filePath,
        content,
        language: getLanguageFromExtension(filePath),
        isDirty: false,
        originalContent: content,
        scope: fileScope,
        isReadOnly: filePath === relativePath || fileScope === FileSystemScope.Versions,
      }

      openFiles.value.push(openFile)
      activeFilePath.value = filePath
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Close a file tab
   */
  function closeFile(filePath: string, force = false): boolean {
    const index = openFiles.value.findIndex((f) => f.path === filePath)
    if (index === -1) return true

    const file = openFiles.value[index]
    if (!file) return true

    // Check for unsaved changes unless forcing
    if (file.isDirty && !force) {
      return false // Caller should prompt user
    }

    // Remove from open files
    openFiles.value.splice(index, 1)

    // If closing active file, switch to another
    if (activeFilePath.value === filePath) {
      const firstFile = openFiles.value[0]
      activeFilePath.value = firstFile ? firstFile.path : null
    }

    return true
  }

  /**
   * Update file content (from editor)
   */
  function updateFileContent(filePath: string, content: string): void {
    const file = openFiles.value.find((f) => f.path === filePath)
    if (file) {
      file.content = content
      file.isDirty = content !== file.originalContent
    }
  }

  /**
   * Save a single file
   */
  async function saveFile(filePath: string): Promise<void> {
    if (!currentApp.value) return

    const file = openFiles.value.find((f) => f.path === filePath)
    if (!file) return

    try {
      if (file.isBackendStep && file.stepId) {
        // Save backend step code
        await appsService.updateStepCode(file.stepId, file.content)
      } else {
        const fileScope = file.scope ?? FileSystemScope.Source
        if (fileScope === FileSystemScope.Source) {
          await appsService.writeFile(
            currentApp.value.id,
            getRelativePathFromScopedPath(filePath, fileScope),
            file.content,
          )
        } else {
          throw new Error('Version snapshots are read-only')
        }
      }

      file.isDirty = false
      file.originalContent = file.content
      sourceChangedSinceDeploy.value = true
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshPreview()
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Save all open files with unsaved changes
   */
  async function saveAllFiles(): Promise<void> {
    if (!currentApp.value) return

    isSaving.value = true

    try {
      const dirtyFiles = openFiles.value.filter((f) => f.isDirty)

      for (const file of dirtyFiles) {
        const fileScope = file.scope ?? FileSystemScope.Source
        if (fileScope === FileSystemScope.Source) {
          await appsService.writeFile(currentApp.value.id, getRelativePathFromScopedPath(file.path, fileScope), file.content)
        } else {
          throw new Error('Version snapshots are read-only')
        }
        file.isDirty = false
        file.originalContent = file.content
      }

      sourceChangedSinceDeploy.value = dirtyFiles.length > 0
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshPreview()
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    } finally {
      isSaving.value = false
    }
  }

  /**
   * Create a new file
   */
  async function createFile(filePath: string, content: string = ''): Promise<void> {
    if (!currentApp.value) return

    try {
      const fileScope = getScopeFromScopedPath(filePath)
      currentFileScope.value = fileScope
      if (fileScope === FileSystemScope.Source) {
        await appsService.createFile(
          currentApp.value.id,
          getRelativePathFromScopedPath(filePath, fileScope),
          content,
        )
      } else {
        throw new Error('Version snapshots are read-only')
      }
      await loadUnifiedFileTree(currentApp.value.id)
      sourceChangedSinceDeploy.value = true
      await refreshCurrentAppMetadata()
      lastDeployError.value = null

      // Open the new file
      await openFile(filePath)
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Delete a file
   */
  async function deleteFile(filePath: string): Promise<void> {
    if (!currentApp.value) return

    try {
      const fileScope = getScopeFromScopedPath(filePath)
      currentFileScope.value = fileScope
      if (fileScope === FileSystemScope.Source) {
        await appsService.deleteFile(
          currentApp.value.id,
          getRelativePathFromScopedPath(filePath, fileScope),
        )
      } else {
        throw new Error('Version snapshots are read-only')
      }

      // Close if open (force close)
      closeFile(filePath, true)

      // Refresh file tree
      await loadUnifiedFileTree(currentApp.value.id)
      sourceChangedSinceDeploy.value = true
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  async function deleteFolder(folderPath: string): Promise<void> {
    if (!currentApp.value) return

    try {
      const folderScope = getScopeFromScopedPath(folderPath)
      currentFileScope.value = folderScope
      const relativePath = getRelativePathFromScopedPath(folderPath, folderScope)

      if (folderScope === FileSystemScope.Source) {
        await appsService.deleteScopedDirectory(currentApp.value.id, folderScope, relativePath)
      } else {
        throw new Error('Version snapshots are read-only')
      }

      const folderPrefix = `${folderPath.replace(/\\/g, '/').replace(/\/+$/, '')}/`
      const openFilePaths = openFiles.value
        .map((file) => file.path)
        .filter((path) => path === folderPath || path.startsWith(folderPrefix))

      for (const openFilePath of openFilePaths) {
        closeFile(openFilePath, true)
      }

      await loadUnifiedFileTree(currentApp.value.id)
      sourceChangedSinceDeploy.value = true
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Refresh the preview iframe
   */
  async function refreshPreview(): Promise<void> {
    if (!currentApp.value) return

    isLoadingPreview.value = true

    try {
      if (!hasApplicationRuntime.value) {
        previewUrl.value = ''
        previewWarning.value = getPreviewWarningMessage()
        return
      }

      const { runtimeToken } = await appsService.createRuntimeLaunch(currentApp.value.id)
      const runtimeUrl = new URL(`${apiBaseUrl}/a/${currentApp.value.id}/`, window.location.origin)
      runtimeUrl.searchParams.set('token', runtimeToken)
      runtimeUrl.searchParams.set('v', Date.now().toString())
      previewUrl.value = runtimeUrl.toString()
      previewWarning.value = getPreviewWarningMessage()
    } catch (err: unknown) {
      console.error('Failed to refresh preview:', err)
      previewUrl.value = ''
      previewWarning.value = getPreviewWarningMessage()
    } finally {
      isLoadingPreview.value = false
    }
  }

  /**
   * Set preview mode
   */
  function setPreviewMode(mode: PreviewMode): void {
    if (mode) {
      previewMode.value = mode
    }
  }

  /**
   * Set preview warning message
   */
  function setPreviewWarning(message: string | null): void {
    previewWarning.value = message
  }

  /**
   * Publish the app
   */
  async function publishApp(request: PublishAppRequest): Promise<void> {
    if (!currentApp.value) return

    // Save all files first
    if (hasUnsavedChanges.value) {
      await saveAllFiles()
    }

    try {
      await appsService.publish(currentApp.value.id, request)

      // Update local state
      currentApp.value = {
        ...currentApp.value,
        isPublished: true,
        publicUrl: request.publicUrl,
        requiresAuthentication: request.requiresAuthentication,
        publishedAt: new Date().toISOString(),
        category: request.category ?? currentApp.value.category,
        tags: request.tags ?? currentApp.value.tags,
      }

      // Refresh preview with published API
      await refreshPreview()
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Unpublish the app
   */
  async function unpublishApp(): Promise<void> {
    if (!currentApp.value) return

    try {
      await appsService.unpublish(currentApp.value.id)

      // Update local state
      currentApp.value = {
        ...currentApp.value,
        isPublished: false,
        publishedAt: undefined,
      }
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Deploy backend scripts to the app's configured runner(s).
   * Checks for requirements.txt up-front and surfaces a status label beneath the button
   * for the duration of the operation — no blocking toast needed.
   */
  async function deployBackend(): Promise<{ deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }> {
    if (!currentApp.value) throw new Error('No app loaded')
    isDeployingBackend.value = true
    deployStatusMessage.value = 'Deploying...'
    try {
      const info = await appsService.getDeployInfo(currentApp.value.id)
      if (info.hasRequirements) {
        deployStatusMessage.value = 'Installing packages…'
      }
      const result = await appsService.deployBackend(currentApp.value.id)
      lastDeployError.value = null
      return result
    } catch (err: unknown) {
      lastDeployError.value = getErrorMessage(err)
      throw err
    } finally {
      isDeployingBackend.value = false
      deployStatusMessage.value = null
    }
  }

  async function deployApp(): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    backendDeploy: { success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }
    message?: string
  }> {
    if (!currentApp.value) throw new Error('No app loaded')
    if (!canDeployFromStudio.value) {
      throw new Error('This app is managed locally. Use automaton deploy from your local project or upload source files first.')
    }

    isDeployingApp.value = true
    deployAppStatusMessage.value = 'Syncing source to live...'
    try {
      if (hasUnsavedChanges.value) {
        deployAppStatusMessage.value = 'Saving source files...'
        await saveAllFiles()
        deployAppStatusMessage.value = 'Syncing source to live...'
      }

      const result = await appsService.deployApp(currentApp.value.id)
      await loadUnifiedFileTree(currentApp.value.id)
      currentApp.value = await appsService.get(currentApp.value.id)
      await refreshAssignedRunnerStatus()
      syncSourceChangeStateFromMetadata()
      lastDeployError.value = null
      await refreshPreview()
      return result
    } catch (err: unknown) {
      lastDeployError.value = getErrorMessage(err)
      throw err
    } finally {
      isDeployingApp.value = false
      deployAppStatusMessage.value = null
    }
  }

  async function deployFrontend(): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    message?: string
  }> {
    if (!currentApp.value) throw new Error('No app loaded')
    if (!canDeployFrontendFromStudio.value) {
      throw new Error('Frontend source is not available in Studio. Existing live/frontend will be preserved.')
    }

    isDeployingApp.value = true
    deployAppStatusMessage.value = 'Syncing frontend source to live...'
    try {
      if (hasUnsavedChanges.value) {
        deployAppStatusMessage.value = 'Saving source files...'
        await saveAllFiles()
        deployAppStatusMessage.value = 'Syncing frontend source to live...'
      }

      const result = await appsService.deployFrontend(currentApp.value.id)
      await loadUnifiedFileTree(currentApp.value.id)
      currentApp.value = await appsService.get(currentApp.value.id)
      await refreshAssignedRunnerStatus()
      syncSourceChangeStateFromMetadata()
      lastDeployError.value = null
      await refreshPreview()
      return result
    } catch (err: unknown) {
      lastDeployError.value = getErrorMessage(err)
      throw err
    } finally {
      isDeployingApp.value = false
      deployAppStatusMessage.value = null
    }
  }

  async function deployBackendComponent(): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    backendDeploy: { success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }
    message?: string
  }> {
    if (!currentApp.value) throw new Error('No app loaded')

    isDeployingBackend.value = true
    deployStatusMessage.value = canDeployBackendFromStudio.value
      ? 'Syncing backend source to live...'
      : 'Redeploying existing live backend...'
    try {
      if (hasUnsavedChanges.value && canDeployBackendFromStudio.value) {
        deployStatusMessage.value = 'Saving source files...'
        await saveAllFiles()
        deployStatusMessage.value = 'Syncing backend source to live...'
      }

      const result = await appsService.redeployBackendComponent(currentApp.value.id)
      await loadUnifiedFileTree(currentApp.value.id)
      currentApp.value = await appsService.get(currentApp.value.id)
      await refreshAssignedRunnerStatus()
      syncSourceChangeStateFromMetadata()
      lastDeployError.value = null
      return result
    } catch (err: unknown) {
      lastDeployError.value = getErrorMessage(err)
      throw err
    } finally {
      isDeployingBackend.value = false
      deployStatusMessage.value = null
    }
  }

  /**
   * Update the name of the current app
   */
  async function updateAppName(name: string): Promise<void> {
    if (!currentApp.value) return

    try {
      await appsService.updateSettings(currentApp.value.id, { name })
      currentApp.value = { ...currentApp.value, name }
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Update the execution runner for the current app
   */
  async function updateAppRunner(runnerId: string | null): Promise<void> {
    if (!currentApp.value) return

    try {
      await appsService.updateRunner(currentApp.value.id, runnerId)

      // Update local state - executionRunnerId can be undefined according to the type
      if (currentApp.value) {
        currentApp.value = {
          ...currentApp.value,
          executionRunnerId: runnerId || undefined
        }
      }

      await refreshAssignedRunnerStatus()
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Refresh the file tree without closing open files
   */
  async function refreshFileTree(): Promise<void> {
    if (!currentApp.value) return

    try {
      await loadUnifiedFileTree(currentApp.value.id)
    } catch (err: unknown) {
      console.error('Failed to refresh file tree:', err)
    }
  }

  /**
   * Reload a specific file from the server (updates content if file is open)
   */
  async function reloadFile(filePath: string): Promise<void> {
    if (!currentApp.value) return

    const fileIndex = openFiles.value.findIndex((f) => f.path === filePath)
    if (fileIndex === -1) {
      console.log(`File ${filePath} not open, skipping reload`)
      return
    }

    const openFile = openFiles.value[fileIndex]
    if (!openFile) {
      console.log(`File ${filePath} not found in open files`)
      return
    }

    // Don't reload if file has unsaved changes (user is editing)
    if (openFile.isDirty) {
      console.log(`Skipping reload of ${filePath} - file has unsaved changes`)
      return
    }

    try {
      console.log(`Reloading file ${filePath} from server...`)
      const fileScope = openFile.scope ?? FileSystemScope.Source
      const response = fileScope === FileSystemScope.Source
        ? await appsService.readFile(currentApp.value.id, getRelativePathFromScopedPath(filePath, fileScope))
        : await appsService.readScopedFile(currentApp.value.id, fileScope, getRelativePathFromScopedPath(filePath, fileScope))

      if (response.success && response.content !== undefined) {
        console.log(`File ${filePath} reloaded successfully, updating content (${response.content.length} chars)`)

        // Force reactivity by creating a completely new array with new object
        const newOpenFiles = [...openFiles.value]
        newOpenFiles[fileIndex] = {
          path: openFile.path,
          content: response.content,
          language: openFile.language,
          isDirty: false,
          originalContent: response.content,
          scope: openFile.scope,
          isReadOnly: openFile.isReadOnly,
          isBackendStep: openFile.isBackendStep,
          stepId: openFile.stepId,
          stepName: openFile.stepName,
        }
        openFiles.value = newOpenFiles

        console.log(`File ${filePath} state updated in store`)
      } else {
        console.warn(`File ${filePath} reload response invalid:`, response)
      }
    } catch (err: unknown) {
      console.error(`Failed to reload file ${filePath}:`, err)
    }
  }

  /**
   * Reload the currently active file
   */
  async function reloadActiveFile(): Promise<void> {
    if (activeFilePath.value) {
      await reloadFile(activeFilePath.value)
    }
  }

  /**
   * Refresh files after AI tool execution
   * @param affectedFiles - List of file paths that were modified
   */
  async function refreshFilesAfterAiUpdate(affectedFiles?: string[]): Promise<void> {
    if (!currentApp.value) return

    try {
      // Always refresh the file tree
      await refreshFileTree()

      // If specific files were affected, reload them if open
      if (affectedFiles && affectedFiles.length > 0) {
        for (const filePath of affectedFiles) {
          await reloadFile(filePath)
        }
      } else {
        // If no specific files mentioned, reload all open files
        for (const file of openFiles.value) {
          if (!file.isBackendStep && !file.isDirty) {
            await reloadFile(file.path)
          }
        }
      }

      sourceChangedSinceDeploy.value = true
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshPreview()
    } catch (err: unknown) {
      console.error('Failed to refresh files after AI update:', err)
    }
  }

  /**
   * Toggle preview overlay visibility
   */
  function togglePreviewOverlay(): void {
    showPreviewOverlay.value = !showPreviewOverlay.value
  }

  /**
   * Open preview overlay — always refreshes so the latest saved files are shown.
   */
  function openPreviewOverlay(): void {
    showPreviewOverlay.value = true
    if (currentApp.value) {
      void refreshPreview()
    }
  }

  /**
   * Close preview overlay
   */
  function closePreviewOverlay(): void {
    showPreviewOverlay.value = false
  }

  /**
   * Add a console entry for step execution result
   */
  function addConsoleEntry(entry: ConsoleEntry): void {
    consoleEntries.value.unshift(entry)
    // Keep last 100 entries
    if (consoleEntries.value.length > 100) {
      consoleEntries.value = consoleEntries.value.slice(0, 100)
    }
  }

  /**
   * Clear all console entries
   */
  function clearConsole(): void {
    consoleEntries.value = []
  }

  function addUiLogEntry(entry: UiLogEntry): void {
    // De-duplicate repeated messages by content fingerprint (more reliable than timestamp heuristics)
    const fingerprint = buildUiLogFingerprint(entry)
    if (uiLogFingerprintWindow.value.includes(fingerprint)) {
      return
    }

    uiLogFingerprintWindow.value.unshift(fingerprint)
    if (uiLogFingerprintWindow.value.length > UI_LOG_FINGERPRINT_WINDOW_SIZE) {
      uiLogFingerprintWindow.value = uiLogFingerprintWindow.value.slice(0, UI_LOG_FINGERPRINT_WINDOW_SIZE)
    }

    uiLogEntries.value.unshift(entry)
    if (uiLogEntries.value.length > 200) {
      uiLogEntries.value = uiLogEntries.value.slice(0, 200)
    }
  }

  function clearUiLogs(): void {
    uiLogEntries.value = []
    uiLogFingerprintWindow.value = []
  }

  function setUiLogFilter(filter: UiLogFilter): void {
    uiLogFilter.value = filter
  }

  /**
   * Clear error
   */
  function clearError(): void {
    error.value = null
  }

  /**
   * Reset store state
   */
  function reset(): void {
    currentApp.value = null
    currentFileScope.value = FileSystemScope.Source
    fileTree.value = []
    openFiles.value = []
    activeFilePath.value = null
    previewUrl.value = ''
    sourceChangedSinceDeploy.value = false
    lastDeployError.value = null
    assignedRunnerOnline.value = null
    previewMode.value = 'desktop'
    isLoading.value = false
    isSaving.value = false
    isLoadingPreview.value = false
    error.value = null
    showPreviewOverlay.value = false
    consoleEntries.value = []
    uiLogEntries.value = []
    uiLogFingerprintWindow.value = []
    uiLogFilter.value = UiLogFilter.All
  }

  /**
   * Helper to extract error message
   */
  function getErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      const axiosError = err as {
        response?: { data?: { message?: string }; status?: number }
        message?: string
      }
      if (axiosError.response?.data?.message) {
        return axiosError.response.data.message
      }
      if (axiosError.message) {
        return axiosError.message
      }
    }
    return 'An unexpected error occurred'
  }

  return {
    // State
    currentApp,
    currentFileScope,
    fileTree,
    openFiles,
    activeFilePath,
    previewUrl,
    sourceChangedSinceDeploy,
    lastDeployError,
    previewWarning,
    previewMode,
    isLoading,
    isSaving,
    isLoadingPreview,
    isDeployingApp,
    isDeployingBackend,
    error,
    showPreviewOverlay,
    consoleEntries,
    uiLogEntries,
    uiLogFilter,

    // Computed
    activeFile,
    hasUnsavedChanges,
    unsavedFilesCount,
    mainHtmlFile,
    isPublished,
    publicUrl,
    hasApplicationRuntime,
    hasDeployedFrontend,
    applicationDeployedAtUtc,
    frontendSourceModifiedAtUtc,
    backendSourceModifiedAtUtc,
    hasBackendFolder,
    currentScopeEditable,
    defaultEditorScope,
    canDeployFrontendFromStudio,
    canDeployBackendFromStudio,
    canDeployFromStudio,
    runnerOffline,
    applicationState,
    isLocalManagedApp,
    showsLocalLiveBanner,
    showsSourceDeployBanner,
    editorModeNoticeLabel,
    editorModeNoticeText,
    isEditableScope,

    // Actions
    loadApp,
    openFile,
    closeFile,
    updateFileContent,
    saveFile,
    saveAllFiles,
    createFile,
    deleteFile,
    deleteFolder,
    refreshPreview,
    setPreviewMode,
    setPreviewWarning,
    publishApp,
    unpublishApp,
    deployFrontend,
    deployBackendComponent,
    deployApp,
    deployBackend,
    deployAppStatusMessage,
    deployStatusMessage,
    updateAppName,
    updateAppRunner,
    refreshFileTree,
    reloadFile,
    reloadActiveFile,
    refreshFilesAfterAiUpdate,
    togglePreviewOverlay,
    openPreviewOverlay,
    closePreviewOverlay,
    addConsoleEntry,
    clearConsole,
    addUiLogEntry,
    clearUiLogs,
    setUiLogFilter,
    clearError,
    reset,
  }
})
