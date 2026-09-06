/**
 * App Editor Pinia store
 * Manages the Visual IDE state for app editing
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { appsService } from '../services/apps.service'
import type { DeployAppResult } from '../services/apps.service'
import type {
  App,
  AppDeployCapabilities,
  AppFileSystemScope,
  FileNode,
  OpenFile,
  ApplicationViewportMode,
  PublishAppRequest,
  PendingFileChangeType,
} from '../types/apps.types'
import {
  AppDevelopmentMode as DevelopmentMode,
  AppFileSystemScope as FileSystemScope,
  getLanguageFromExtension,
  PendingFileChangeType as PendingChangeType,
} from '../types/apps.types'
import type { BackendRuntimeLogEvent, ConsoleEntry } from '../types/console.types'
import { getOpenAppUrl } from '../utils/public-app-url'

export const useAppEditorStore = defineStore('appEditor', () => {
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
  const applicationUrl = ref('')
  const sourceChangedSinceDeploy = ref(false)
  const localDeployBaseline = ref<string | null>(null)
  const pendingDeployChanges = ref<Record<string, PendingFileChangeType>>({})
  const deployCapabilities = ref<AppDeployCapabilities | null>(null)
  const lastDeployError = ref<string | null>(null)
  const assignedRunnerOnline = ref<boolean | null>(null)
  const genericRuntimeDeployed = ref(false)

  // Preview warning message (missing assets, etc.)
  const applicationWarning = ref<string | null>(null)

  // Preview mode (desktop, tablet, mobile)
  const applicationViewportMode = ref<ApplicationViewportMode>('desktop')

  // Loading states
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isLoadingApplication = ref(false)
  const isDeployingApp = ref(false)
  const deployStatusMessage = ref<string | null>(null)
  const deployAppStatusMessage = ref<string | null>(null)

  // Error state
  const error = ref<string | null>(null)

  // Console entries for step execution results
  const consoleEntries = ref<ConsoleEntry[]>([])

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
  const pendingDeployChangeEntries = computed(() =>
    Object.entries(pendingDeployChanges.value).map(([path, type]) => ({ path, type })),
  )

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
    return genericRuntimeDeployed.value
  })
  const currentScopeEditable = computed(() => currentFileScope.value !== FileSystemScope.Versions)
  const defaultEditorScope = computed<AppFileSystemScope>(() => {
    return FileSystemScope.Source
  })
  const canDeployFromStudio = computed(() =>
    deployCapabilities.value?.canDeployApplicationFromStudio ?? false,
  )
  const deployBlockedReason = computed(() => canDeployFromStudio.value ? null : 'The application is not ready to deploy.')
  const runnerOffline = computed(() => assignedRunnerOnline.value === false)
  const applicationState = computed(() => {
    if (lastDeployError.value) return 'Build failed'
    if (runnerOffline.value) return 'Runner offline'
    if (sourceChangedSinceDeploy.value) return 'Source changed'
    if (hasApplicationRuntime.value) return 'Deployed'
    return 'Not deployed'
  })
  const isLocalManagedApp = computed(() => currentApp.value?.developmentMode === DevelopmentMode.Local)
  const showsLocalLiveBanner = computed(() => isLocalManagedApp.value && !currentApp.value?.sourceAvailable)
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

    return 'You are editing the application source. Save and deploy the application to publish the current source and runtime contract.'
  })

  function toUtcMillis(value?: string | null): number | null {
    if (!value) return null
    const millis = Date.parse(value)
    return Number.isFinite(millis) ? millis : null
  }

  function hasPersistedSourceDivergence(app: App | null): boolean {
    return Boolean(app?.sourceAvailable && sourceChangedSinceDeploy.value)
  }

  function hasSourceChangedSinceLocalDeploy(app: App | null): boolean {
    if (!app || !localDeployBaseline.value) {
      return false
    }

    return compareTimestamps(app.updated, localDeployBaseline.value) > 0
  }

  function syncLocalDeployBaselineFromMetadata(): void {
    if (!currentApp.value || hasPersistedSourceDivergence(currentApp.value)) {
      localDeployBaseline.value = null
      return
    }

    localDeployBaseline.value = currentApp.value.updated
  }

  function markSourceChangedAfterDeploy(): void {
    localDeployBaseline.value = null
    sourceChangedSinceDeploy.value = true
  }

  function markPendingDeployChange(path: string, type: PendingFileChangeType): void {
    const normalizedPath = path.replace(/\\/g, '/')
    pendingDeployChanges.value = {
      ...pendingDeployChanges.value,
      [normalizedPath]: type,
    }
  }

  function clearPendingDeployChanges(): void {
    pendingDeployChanges.value = {}
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
      sourceChangedSinceDeploy.value = localDeployBaseline.value
        ? hasSourceChangedSinceLocalDeploy(currentApp.value)
        : hasPersistedSourceDivergence(currentApp.value)
    }

    applicationWarning.value = getApplicationWarningMessage()
  }

  function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => {
      window.setTimeout(resolve, ms)
    })
  }

  async function finalizeSuccessfulDeployment(): Promise<void> {
    // Clear the pending-deploy warning immediately after a successful deploy.
    // The API metadata can lag briefly, which otherwise leaves the UI orange
    // even though the deploy toast already reported success.
    localDeployBaseline.value = currentApp.value?.updated ?? null
    sourceChangedSinceDeploy.value = false
    clearPendingDeployChanges()
    lastDeployError.value = null

    await refreshCurrentAppMetadata()
    await refreshApplication()

    if (!localDeployBaseline.value || hasSourceChangedSinceLocalDeploy(currentApp.value)) {
      await sleep(1500)
      await refreshCurrentAppMetadata()
      await refreshApplication()
    }
  }

  function getApplicationWarningMessage(): string | null {
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
    if (!currentApp.value) {
      assignedRunnerOnline.value = null
      genericRuntimeDeployed.value = false
      return
    }

    try {
      const targets = await appsService.listDeploymentTargets(currentApp.value.id)
      const defaultTarget = targets.find((target) => target.isDefault)
      const defaultRunner = defaultTarget?.runner
      assignedRunnerOnline.value = defaultRunner ? defaultRunner.isOnline : null
      genericRuntimeDeployed.value = defaultTarget?.genericRuntimeDeployed ?? false
    } catch {
      assignedRunnerOnline.value = null
      genericRuntimeDeployed.value = false
    }
  }

  async function refreshDeployCapabilities(): Promise<void> {
    if (!currentApp.value) {
      deployCapabilities.value = null
      return
    }

    try {
      deployCapabilities.value = await appsService.getDeployCapabilities(currentApp.value.id)
    } catch {
      deployCapabilities.value = null
    }
  }

  async function refreshCurrentAppMetadata(): Promise<void> {
    if (!currentApp.value) {
      return
    }

    currentApp.value = await appsService.get(currentApp.value.id)
    await refreshAssignedRunnerStatus()
    await refreshDeployCapabilities()
    syncLocalDeployBaselineFromMetadata()
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
      await refreshDeployCapabilities()

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

      // Load running application
      await refreshApplication()
      syncLocalDeployBaselineFromMetadata()
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
      markSourceChangedAfterDeploy()
      markPendingDeployChange(filePath, PendingChangeType.Modified)
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshApplication()
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

      if (dirtyFiles.length > 0) {
        markSourceChangedAfterDeploy()
        for (const dirtyFile of dirtyFiles) {
          if (pendingDeployChanges.value[dirtyFile.path] !== PendingChangeType.Added) {
            markPendingDeployChange(dirtyFile.path, PendingChangeType.Modified)
          }
        }
      }
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshApplication()
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
      markSourceChangedAfterDeploy()
      markPendingDeployChange(filePath, PendingChangeType.Added)
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
      markSourceChangedAfterDeploy()
      markPendingDeployChange(filePath, PendingChangeType.Deleted)
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

      const deletedPaths = collectContainedFilePaths(fileTree.value, folderPath)
      await loadUnifiedFileTree(currentApp.value.id)
      markSourceChangedAfterDeploy()
      for (const deletedPath of deletedPaths) {
        markPendingDeployChange(deletedPath, PendingChangeType.Deleted)
      }
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
  async function refreshApplication(): Promise<void> {
    if (!currentApp.value) return

    isLoadingApplication.value = true

    try {
      if (!hasApplicationRuntime.value) {
        applicationUrl.value = ''
        applicationWarning.value = getApplicationWarningMessage()
        return
      }

      await appsService.createRuntimeLaunch(currentApp.value.id)
      applicationUrl.value = new URL(getOpenAppUrl(currentApp.value), window.location.origin).toString()
      applicationWarning.value = getApplicationWarningMessage()
    } catch (err: unknown) {
      console.error('Failed to refresh application:', err)
      applicationUrl.value = ''
      applicationWarning.value = getApplicationWarningMessage()
    } finally {
      isLoadingApplication.value = false
    }
  }

  /**
   * Set preview mode
   */
  function setApplicationViewportMode(mode: ApplicationViewportMode): void {
    if (mode) {
      applicationViewportMode.value = mode
    }
  }

  /**
   * Set preview warning message
   */
  function setApplicationWarning(message: string | null): void {
    applicationWarning.value = message
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

      // The API writes the Traefik route atomically, but the file provider
      // reload is asynchronous. Give it a moment before switching the iframe
      // from the private runtime route to the published host route.
      await new Promise(resolve => window.setTimeout(resolve, 750))

      // Refresh running application with published API
      await refreshApplication()
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
  async function deployApp(targetSlug?: string): Promise<DeployAppResult> {
    if (!currentApp.value) throw new Error('No app loaded')
    if (!canDeployFromStudio.value) {
      throw new Error(deployBlockedReason.value || 'This app cannot be fully deployed from Studio.')
    }

    isDeployingApp.value = true
    deployAppStatusMessage.value = 'Syncing source to live...'
    try {
      if (hasUnsavedChanges.value) {
        deployAppStatusMessage.value = 'Saving source files...'
        await saveAllFiles()
        deployAppStatusMessage.value = 'Syncing source to live...'
      }

      const result = await appsService.deployApp(currentApp.value.id, targetSlug)
      await loadUnifiedFileTree(currentApp.value.id)
      currentApp.value = await appsService.get(currentApp.value.id)
      await refreshAssignedRunnerStatus()
      if (result.success) {
        await finalizeSuccessfulDeployment()
      } else {
        lastDeployError.value = result.message ?? 'Application deployment failed.'
      }
      return result
    } catch (err: unknown) {
      lastDeployError.value = getErrorMessage(err)
      throw err
    } finally {
      isDeployingApp.value = false
      deployAppStatusMessage.value = null
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

  function collectContainedFilePaths(nodes: FileNode[], folderPath: string): string[] {
    const normalizedFolderPath = folderPath.replace(/\\/g, '/').replace(/\/+$/, '')
    const matches: string[] = []

    function walk(currentNodes: FileNode[]) {
      for (const node of currentNodes) {
        const normalizedNodePath = node.path.replace(/\\/g, '/')
        if (node.type === 'file' && normalizedNodePath.startsWith(`${normalizedFolderPath}/`)) {
          matches.push(normalizedNodePath)
        }

        if (node.children?.length) {
          walk(node.children)
        }
      }
    }

    walk(nodes)
    return matches
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

      markSourceChangedAfterDeploy()
      for (const filePath of affectedFiles ?? []) {
        if (pendingDeployChanges.value[filePath] !== PendingChangeType.Added) {
          markPendingDeployChange(filePath, PendingChangeType.Modified)
        }
      }
      await refreshCurrentAppMetadata()
      lastDeployError.value = null
      await refreshApplication()
    } catch (err: unknown) {
      console.error('Failed to refresh files after AI update:', err)
    }
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

  function addBackendRuntimeLogEntry(event: BackendRuntimeLogEvent): void {
    addConsoleEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(event.timestamp),
      type: 'runtime-log',
      stepName: event.actionName || event.controllerName || 'backend',
      inputs: {},
      result: {
        success: event.level !== 'error',
        status: event.level === 'error' ? 'Error' : event.level === 'warn' ? 'Warning' : 'Output',
        output: event.message,
      },
      source: 'application',
      level: event.level,
      runnerName: event.runnerName ?? undefined,
      deploymentTargetId: event.deploymentTargetId,
    })
  }

  /**
   * Clear all console entries
   */
  function clearConsole(): void {
    consoleEntries.value = []
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
    applicationUrl.value = ''
    localDeployBaseline.value = null
    clearPendingDeployChanges()
    deployCapabilities.value = null
    sourceChangedSinceDeploy.value = false
    lastDeployError.value = null
    assignedRunnerOnline.value = null
    genericRuntimeDeployed.value = false
    applicationViewportMode.value = 'desktop'
    isLoading.value = false
    isSaving.value = false
    isLoadingApplication.value = false
    error.value = null
    consoleEntries.value = []
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
    applicationUrl,
    sourceChangedSinceDeploy,
    pendingDeployChanges,
    pendingDeployChangeEntries,
    lastDeployError,
    applicationWarning,
    applicationViewportMode,
    isLoading,
    isSaving,
    isLoadingApplication,
    isDeployingApp,
    error,
    consoleEntries,

    // Computed
    activeFile,
    hasUnsavedChanges,
    unsavedFilesCount,
    mainHtmlFile,
    isPublished,
    publicUrl,
    hasApplicationRuntime,
    currentScopeEditable,
    defaultEditorScope,
    deployCapabilities,
    canDeployFromStudio,
    deployBlockedReason,
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
    refreshApplication,
    setApplicationViewportMode,
    setApplicationWarning,
    publishApp,
    unpublishApp,
    deployApp,
    deployAppStatusMessage,
    deployStatusMessage,
    updateAppName,
    refreshFileTree,
    reloadFile,
    reloadActiveFile,
    refreshFilesAfterAiUpdate,
    addConsoleEntry,
    addBackendRuntimeLogEntry,
    clearConsole,
    clearError,
    reset,
  }
})
