/**
 * App Editor Pinia store
 * Manages the Visual IDE state for app editing
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { appsService } from '../services/apps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type {
  App,
  FileNode,
  OpenFile,
  PreviewMode,
  PublishAppRequest,
} from '../types/apps.types'
import { getLanguageFromExtension } from '../types/apps.types'
import type { ConsoleEntry } from '../types/console.types'

export const useAppEditorStore = defineStore('appEditor', () => {
  const authStore = useAuthStore()
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || ''

  // ============================================
  // State
  // ============================================

  // Current app being edited
  const currentApp = ref<App | null>(null)

  // File tree data
  const fileTree = ref<FileNode[]>([])

  // Open files in editor tabs
  const openFiles = ref<OpenFile[]>([])

  // Currently active file path (selected tab)
  const activeFilePath = ref<string | null>(null)

  // Preview HTML content
  const previewHtml = ref('')

  // Preview URL for iframe (HTTP-based preview)
  const previewUrl = ref('')

  // Preview warning message (missing assets, etc.)
  const previewWarning = ref<string | null>(null)

  // Preview mode (desktop, tablet, mobile)
  const previewMode = ref<PreviewMode>('desktop')

  // Loading states
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isLoadingPreview = ref(false)
  const isDeployingBackend = ref(false)

  // Error state
  const error = ref<string | null>(null)

  // Preview overlay visibility (full-screen preview)
  const showPreviewOverlay = ref(false)

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

  /**
   * Main HTML file (index.html)
   */
  const mainHtmlFile = computed(() => {
    return openFiles.value.find((f) => f.path === 'index.html')
  })

  /**
   * Check if app is published
   */
  const isPublished = computed(() => currentApp.value?.isPublished ?? false)

  /**
   * Public URL slug
   */
  const publicUrl = computed(() => currentApp.value?.publicUrl ?? '')

  /**
   * Check if app has a backend/ folder (deploy button should be visible)
   */
  const hasBackendFolder = computed(() =>
    fileTree.value.some((node) => node.type === 'folder' && node.name === 'backend'),
  )

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

      // Load file tree
      fileTree.value = await appsService.getFileTree(appId)

      // Reset editor state
      openFiles.value = []
      activeFilePath.value = null

      // Auto-open index.html if it exists
      const hasIndexHtml = findFileInTree(fileTree.value, 'index.html')
      if (hasIndexHtml) {
        await openFile('index.html')
      }

      // Load preview
      await refreshPreview()
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

    // Check if already open
    const existing = openFiles.value.find((f) => f.path === filePath)
    if (existing) {
      activeFilePath.value = filePath
      return
    }

    try {
      // Load file content
      const response = await appsService.readFile(currentApp.value.id, filePath)

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
        // Save regular app file
        await appsService.writeFile(currentApp.value.id, filePath, file.content)
      }

      file.isDirty = false
      file.originalContent = file.content

      // If saving index.html, refresh preview
      if (filePath === 'index.html') {
        await refreshPreview()
      }
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
        await appsService.writeFile(currentApp.value.id, file.path, file.content)
        file.isDirty = false
        file.originalContent = file.content
      }

      // Refresh preview after saving
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
      await appsService.createFile(currentApp.value.id, filePath, content)

      // Refresh file tree
      fileTree.value = await appsService.getFileTree(currentApp.value.id)

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
      await appsService.deleteFile(currentApp.value.id, filePath)

      // Close if open (force close)
      closeFile(filePath, true)

      // Refresh file tree
      fileTree.value = await appsService.getFileTree(currentApp.value.id)
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
    
    // Clear any previous preview warnings
    previewWarning.value = null

    try {
      const userId = currentApp.value.userId || authStore.userId
      if (!userId) {
        console.warn('Preview user ID is missing; unable to build preview URL')
        previewUrl.value = ''
        return
      }

      // Build preview URL (uses user ID and app ID)
      // Add cache-busting timestamp to force iframe reload
      const cacheBuster = Date.now()
      const nextPreviewUrl = `${apiBaseUrl}/app-preview/${userId}/${currentApp.value.id}/index.html?v=${cacheBuster}`

      const previewFiles = await buildPreviewFiles(currentApp.value.id)

      // Use current index.html from files (not stale htmlContent from app definition)
      const currentIndexHtml = previewFiles['index.html'] || undefined
      previewHtml.value = await appsService.getPreviewHtml(currentApp.value.id, currentIndexHtml)
      
      // Upload preview content to server for HTTP-based serving
      await appsService.uploadPreviewContent(currentApp.value.id, previewHtml.value, previewFiles)
      previewUrl.value = nextPreviewUrl
    } catch (err: unknown) {
      console.error('Failed to refresh preview:', err)
    } finally {
      isLoadingPreview.value = false
    }
  }

  async function buildPreviewFiles(appId: string): Promise<Record<string, string>> {
    const files: Record<string, string> = {}

    try {
      const response = await appsService.getFiles(appId)
      if (response.success && response.files) {
        const paths = response.files
          .map((file) => (typeof file === 'string' ? file : file.path))
          .filter((path): path is string => Boolean(path))
          .map((path) => path.replace(/\\/g, '/'))
          .filter((path) => !path.startsWith('versions/'))

        const contents = await Promise.all(
          paths.map(async (path) => {
            try {
              const result = await appsService.readFile(appId, path)
              return { path, content: result.content ?? '' }
            } catch {
              return { path, content: '' }
            }
          })
        )

        for (const entry of contents) {
          files[entry.path] = entry.content
        }
      }
    } catch (err: unknown) {
      console.warn('Failed to load preview files:', err)
    }

    for (const openFile of openFiles.value) {
      if (openFile.path) {
        const normalizedPath = openFile.path.replace(/\\/g, '/')
        files[normalizedPath] = openFile.content
      }
    }

    return files
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
   * Deploy backend scripts to the app's configured runner(s)
   */
  async function deployBackend(): Promise<{ deployedCount: number; totalScripts: number; runnerCount: number; message: string }> {
    if (!currentApp.value) throw new Error('No app loaded')
    isDeployingBackend.value = true
    try {
      const result = await appsService.deployBackend(currentApp.value.id)
      return result
    } finally {
      isDeployingBackend.value = false
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
      fileTree.value = await appsService.getFileTree(currentApp.value.id)
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
      const response = await appsService.readFile(currentApp.value.id, filePath)

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

      // Refresh preview if index.html was affected
      if (!affectedFiles || affectedFiles.includes('index.html')) {
        await refreshPreview()
      }
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
   * Open preview overlay
   */
  function openPreviewOverlay(): void {
    showPreviewOverlay.value = true
    if (!previewUrl.value && currentApp.value) {
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
    fileTree.value = []
    openFiles.value = []
    activeFilePath.value = null
    previewHtml.value = ''
    previewMode.value = 'desktop'
    isLoading.value = false
    isSaving.value = false
    isLoadingPreview.value = false
    error.value = null
    showPreviewOverlay.value = false
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
    fileTree,
    openFiles,
    activeFilePath,
    previewHtml,
    previewUrl,
    previewWarning,
    previewMode,
    isLoading,
    isSaving,
    isLoadingPreview,
    isDeployingBackend,
    error,
    showPreviewOverlay,
    consoleEntries,

    // Computed
    activeFile,
    hasUnsavedChanges,
    unsavedFilesCount,
    mainHtmlFile,
    isPublished,
    publicUrl,
    hasBackendFolder,

    // Actions
    loadApp,
    openFile,
    closeFile,
    updateFileContent,
    saveFile,
    saveAllFiles,
    createFile,
    deleteFile,
    refreshPreview,
    setPreviewMode,
    setPreviewWarning,
    publishApp,
    unpublishApp,
    deployBackend,
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
    clearError,
    reset,
  }
})
