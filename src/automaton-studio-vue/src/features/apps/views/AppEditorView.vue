<script setup lang="ts">
/**
 * AppEditorView.vue
 * Main Visual IDE for editing apps
 * AI-first layout: Files | Editor | AI Assistant
 * Preview opens as full-screen overlay
 */
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import ConfirmDialog from 'primevue/confirmdialog'
import Toast from 'primevue/toast'
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
import CreateStepDialog from '../components/CreateStepDialog.vue'
import CreateFileDialog from '../components/CreateFileDialog.vue'
import StatusBar from '../components/StatusBar.vue'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'

import { useAppEditorStore } from '../stores/app-editor.store'
import type { CreateAppStepRequest, PublishAppRequest } from '../types/apps.types'

const route = useRoute()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

// Get app ID from route
const appId = computed(() => route.params.id as string)

// Dialog visibility
const showPublishDialog = ref(false)
const showCreateStepDialog = ref(false)
const showCreateFileDialog = ref(false)

// App name editing
const isEditingName = ref(false)
const editingName = ref('')

// Runner selection for AI tool execution
const selectedRunnerId = ref<string | null>(null)

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
      // Update the app's execution runner ID
      await editorStore.updateAppRunner(newRunnerId)
      toast.add({
        severity: 'success',
        summary: 'Runner Updated',
        detail: 'Execution runner has been updated',
        life: 3000,
      })
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

// Add beforeunload listener
onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
})

onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
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
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'File saved successfully',
      life: 3000,
    })
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

// Handle create step
async function handleCreateStep(request: CreateAppStepRequest) {
  try {
    await editorStore.createBackendStep(request)
    showCreateStepDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `Step "${request.name}" created and linked`,
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to create step',
      life: 5000,
    })
  }
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
function saveAppName() {
  // TODO: Implement app name update
  isEditingName.value = false
}

// Handle AI tool execution for real-time file updates
async function handleAiToolExecuted(toolName: string, args: any, result?: any) {
  // File modification tools that should trigger refresh
  const fileModificationTools = [
    'write_file',
    'save_app_code',
    'delete_file',
    'create_backend_step',
    'save_step_code',
  ]

  if (fileModificationTools.includes(toolName)) {
    console.log(`AI tool "${toolName}" executed, refreshing files...`, args)

    // Determine which files were affected
    const affectedFiles: string[] = []

    if (toolName === 'write_file' && args.path) {
      affectedFiles.push(args.path)
    } else if (toolName === 'save_app_code') {
      affectedFiles.push('index.html')
    } else if (toolName === 'delete_file' && args.path) {
      // File was deleted, just refresh tree
      await editorStore.refreshFileTree()
      return
    } else if (toolName === 'create_backend_step' || toolName === 'save_step_code') {
      // Backend step created/updated, refresh linked steps
      await editorStore.refreshLinkedSteps()
      return
    }

    // Refresh files after AI update
    await editorStore.refreshFilesAfterAiUpdate(affectedFiles)

    // Show toast notification
    if (affectedFiles.length > 0) {
      toast.add({
        severity: 'info',
        summary: 'Files Updated',
        detail: `AI updated: ${affectedFiles.join(', ')}`,
        life: 3000,
      })
    }
  }
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
            icon="pi pi-arrow-left"
            text
            rounded
            severity="secondary"
            @click="handleBack"
            v-tooltip.bottom="'Back to Apps'"
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
            <div class="app-description text-sm text-color-secondary">
              {{ editorStore.currentApp.description || 'No description' }}
            </div>
          </div>
        </div>

        <div class="header-right">
          <!-- Runner Selector -->
          <div class="flex align-items-center gap-2">
            <label class="text-sm text-color-secondary">Runner:</label>
            <RunnerSelector
              v-model="selectedRunnerId"
              :disabled="editorStore.isSaving"
            />
          </div>
          
          <Button
            icon="pi pi-eye"
            label="Preview"
            outlined
            severity="secondary"
            @click="editorStore.openPreviewOverlay()"
            v-tooltip.bottom="'Open full-screen preview'"
          />
          <Button
            v-if="editorStore.hasUnsavedChanges"
            label="Save All"
            icon="pi pi-save"
            :loading="editorStore.isSaving"
            @click="handleSaveAll"
          />
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
        </div>
      </div>

      <!-- Main editor layout with resizable splitters -->
      <Splitter
        class="editor-layout"
        layout="horizontal"
        :gutterSize="8"
        @resizeend="handleSplitterResize"
      >
        <!-- Left Panel: File Tree -->
        <SplitterPanel :size="panelSizes[0]" :minSize="10" class="splitter-panel">
          <div class="file-panel">
            <FileTree
              @create-file="showCreateFileDialog = true"
              @delete-file="handleDeleteFile"
              @create-step="showCreateStepDialog = true"
            />
          </div>
        </SplitterPanel>

        <!-- Center Panel: Code Editor -->
        <SplitterPanel :size="panelSizes[1]" :minSize="30" class="splitter-panel">
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

        <!-- Right Panel: AI Assistant -->
        <SplitterPanel :size="panelSizes[2]" :minSize="15" class="splitter-panel">
          <div class="ai-sidebar">
            <AiAssistant
              :app-id="appId"
              :runner-id="selectedRunnerId"
              @tool-executed="handleAiToolExecuted"
            />
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

    <CreateStepDialog
      v-model:visible="showCreateStepDialog"
      @create="handleCreateStep"
    />

    <CreateFileDialog
      v-model:visible="showCreateFileDialog"
      :existing-files="existingFilePaths"
      @create="handleCreateFile"
    />

    <ConfirmDialog />
    <Toast />
  </div>
</template>

<style scoped>
/* Override parent .app-main padding and scrolling for this full-screen editor */
:global(.app-main) {
  padding: 0 !important;
  overflow: hidden !important;
}

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
  background-color: var(--p-surface-0);
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
  gap: 0.25rem;
}

.app-name {
  font-size: 1.125rem;
  font-weight: 600;
  cursor: pointer;
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
  background-color: var(--p-surface-0);
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
  background-color: var(--p-surface-100);
  border-bottom: 1px solid var(--p-surface-200);
  overflow-x: auto;
  flex-shrink: 0;
}

.editor-tab {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.5rem 0.75rem;
  background-color: var(--p-surface-100);
  border-right: 1px solid var(--p-surface-200);
  cursor: pointer;
  white-space: nowrap;
  transition: background-color 0.2s;
}

.editor-tab:hover {
  background-color: var(--p-surface-200);
}

.editor-tab.active {
  background-color: var(--p-surface-0);
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
  background-color: var(--p-surface-0);
}

/* Dark mode */
:global(.app-dark) .editor-header {
  background-color: var(--p-surface-900);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-panel {
  background-color: var(--p-surface-900);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tabs-container {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tab {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tab:hover {
  background-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tab.active {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .ai-sidebar {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
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
</style>
