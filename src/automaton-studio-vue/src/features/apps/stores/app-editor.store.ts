/**
 * App Editor Pinia store
 * Manages the Visual IDE state for app editing
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { appsService } from '../services/apps.service'
import type {
  App,
  FileNode,
  OpenFile,
  LinkedStepInfo,
  CreateAppStepRequest,
  PreviewMode,
  PublishAppRequest,
} from '../types/apps.types'
import { getLanguageFromExtension } from '../types/apps.types'

export const useAppEditorStore = defineStore('appEditor', () => {
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

  // Linked backend steps
  const linkedSteps = ref<LinkedStepInfo[]>([])

  // Preview HTML content
  const previewHtml = ref('')

  // Preview mode (desktop, tablet, mobile)
  const previewMode = ref<PreviewMode>('desktop')

  // Loading states
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isLoadingPreview = ref(false)

  // Error state
  const error = ref<string | null>(null)

  // Preview overlay visibility (full-screen preview)
  const showPreviewOverlay = ref(false)

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

      // Load linked steps
      linkedSteps.value = await appsService.getLinkedSteps(appId)

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
   * Open a backend step file in the editor
   */
  async function openBackendStep(step: LinkedStepInfo): Promise<void> {
    // Create a virtual path for the step
    const virtualPath = `backend/${step.name}.${getStepExtension(step.language)}`

    // Check if already open
    const existing = openFiles.value.find((f) => f.path === virtualPath)
    if (existing) {
      activeFilePath.value = virtualPath
      return
    }

    try {
      // Load step code
      const { code, language } = await appsService.getStepCode(step.id)

      // Create open file entry for backend step
      const openFile: OpenFile = {
        path: virtualPath,
        content: code,
        language: getEditorLanguage(language),
        isDirty: false,
        originalContent: code,
        isBackendStep: true,
        stepId: step.id,
        stepName: step.displayName || step.name,
      }

      openFiles.value.push(openFile)
      activeFilePath.value = virtualPath
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Get file extension for step language
   */
  function getStepExtension(language: string): string {
    switch (language?.toLowerCase()) {
      case 'python':
        return 'py'
      case 'powershell':
        return 'ps1'
      case 'batch':
        return 'bat'
      default:
        return 'py'
    }
  }

  /**
   * Get Monaco editor language for step language
   */
  function getEditorLanguage(language: string): string {
    switch (language?.toLowerCase()) {
      case 'python':
        return 'python'
      case 'powershell':
        return 'powershell'
      case 'batch':
        return 'bat'
      default:
        return 'python'
    }
  }

  /**
   * Close a file tab
   */
  function closeFile(filePath: string, force = false): boolean {
    const index = openFiles.value.findIndex((f) => f.path === filePath)
    if (index === -1) return true

    const file = openFiles.value[index]

    // Check for unsaved changes unless forcing
    if (file.isDirty && !force) {
      return false // Caller should prompt user
    }

    // Remove from open files
    openFiles.value.splice(index, 1)

    // If closing active file, switch to another
    if (activeFilePath.value === filePath) {
      activeFilePath.value = openFiles.value.length > 0 ? openFiles.value[0].path : null
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

    try {
      previewHtml.value = await appsService.getPreviewHtml(currentApp.value.id)
    } catch (err: unknown) {
      console.error('Failed to refresh preview:', err)
    } finally {
      isLoadingPreview.value = false
    }
  }

  /**
   * Set preview mode
   */
  function setPreviewMode(mode: PreviewMode): void {
    previewMode.value = mode
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
   * Create a new backend step and link it to the app
   */
  async function createBackendStep(request: CreateAppStepRequest): Promise<LinkedStepInfo> {
    if (!currentApp.value) throw new Error('No app loaded')

    try {
      const step = await appsService.createStepForApp(currentApp.value.id, request)
      linkedSteps.value.push(step)
      return step
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Unlink a step from the app
   */
  async function unlinkStep(stepId: string): Promise<void> {
    if (!currentApp.value) return

    try {
      await appsService.unlinkStep(currentApp.value.id, stepId)
      linkedSteps.value = linkedSteps.value.filter((s) => s.id !== stepId)
    } catch (err: unknown) {
      error.value = getErrorMessage(err)
      throw err
    }
  }

  /**
   * Refresh linked steps from the server
   */
  async function refreshLinkedSteps(): Promise<void> {
    if (!currentApp.value) return

    try {
      linkedSteps.value = await appsService.getLinkedSteps(currentApp.value.id)
    } catch (err: unknown) {
      console.error('Failed to refresh linked steps:', err)
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
  }

  /**
   * Close preview overlay
   */
  function closePreviewOverlay(): void {
    showPreviewOverlay.value = false
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
    linkedSteps.value = []
    previewHtml.value = ''
    previewMode.value = 'desktop'
    isLoading.value = false
    isSaving.value = false
    isLoadingPreview.value = false
    error.value = null
    showPreviewOverlay.value = false
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
    linkedSteps,
    previewHtml,
    previewMode,
    isLoading,
    isSaving,
    isLoadingPreview,
    error,
    showPreviewOverlay,

    // Computed
    activeFile,
    hasUnsavedChanges,
    unsavedFilesCount,
    mainHtmlFile,
    isPublished,
    publicUrl,

    // Actions
    loadApp,
    openFile,
    openBackendStep,
    closeFile,
    updateFileContent,
    saveFile,
    saveAllFiles,
    createFile,
    deleteFile,
    refreshPreview,
    setPreviewMode,
    publishApp,
    unpublishApp,
    updateAppRunner,
    createBackendStep,
    unlinkStep,
    refreshLinkedSteps,
    refreshFileTree,
    reloadFile,
    reloadActiveFile,
    refreshFilesAfterAiUpdate,
    togglePreviewOverlay,
    openPreviewOverlay,
    closePreviewOverlay,
    clearError,
    reset,
  }
})
